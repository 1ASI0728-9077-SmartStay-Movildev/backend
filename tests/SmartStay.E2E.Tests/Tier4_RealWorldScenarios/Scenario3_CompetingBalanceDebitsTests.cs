using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 3: Competing Balance Debits & Overdraft Prevention.
/// Executes concurrent payment attempts against a shared DemoCard balance with total demand exceeding capacity.
/// Asserts non-negative balance invariant, atomic debit movements, and zero overdraft under race conditions.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario3_CompetingBalanceDebits")]
public class Scenario3_CompetingBalanceDebitsTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public Scenario3_CompetingBalanceDebitsTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario3_ConcurrentPaymentsExhaustingBalance_PreventsOverdraft()
    {
        const int concurrentAttempts = 5;
        const decimal initialCappedBalance = 350.00m; // Enough for 2 payments of 170.00 (340.00), but not 3

        // 1. Setup single guest and activated demo card
        var user = "compete_debit_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "CompetePass123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // 2. Adjust card balance to 350.00 USD in DB
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE demo_payment_methods SET balance = @bal WHERE id = @id";
            cmd.Parameters.AddWithValue("@bal", initialCappedBalance);
            cmd.Parameters.AddWithValue("@id", cardId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Create 5 distinct valid bookings for distinct date windows
        var bookings = new (int BookingId, decimal Amount)[concurrentAttempts];
        for (var i = 0; i < concurrentAttempts; i++)
        {
            var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 8000 + (i * 20) + Random.Shared.Next(1, 10), stayDuration: 2);
            var bookResp = await _gatewayClient.CreateBookingAsync(new
            {
                roomId = 101,
                guestName = $"Debit Guest {i}",
                guestEmail = $"debit_{i}@example.com",
                checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, token);

            bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
            var doc = JsonDocument.Parse(await bookResp.Content.ReadAsStringAsync());
            bookings[i] = (doc.RootElement.GetProperty("id").GetInt32(), doc.RootElement.GetProperty("totalAmount").GetDecimal());
        }

        // 4. Synchronize 5 concurrent payment attempts on the same demo card
        using var barrier = new ManualResetEventSlim(false);
        var paymentResults = new ConcurrentBag<(int Index, HttpStatusCode StatusCode, string Body)>();

        var tasks = Enumerable.Range(0, concurrentAttempts).Select(async i =>
        {
            await Task.Yield();
            barrier.Wait(); // Release together

            var payResp = await _gatewayClient.ProcessPaymentAsync(new
            {
                bookingId = bookings[i].BookingId,
                amount = bookings[i].Amount,
                currency = "USD",
                paymentMethod = "DemoCard",
                demoPaymentMethodId = cardId
            }, token, idempotencyKey: $"pay_compete_{i}_{Guid.NewGuid():N}");

            var body = await payResp.Content.ReadAsStringAsync();
            paymentResults.Add((i, payResp.StatusCode, body));
        }).ToArray();

        barrier.Set();
        await Task.WhenAll(tasks);

        // 5. Assertions: Exactly 2 succeed, exactly 3 fail due to insufficient funds
        var successful = paymentResults.Where(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created).ToList();
        var rejected = paymentResults.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        successful.Count.Should().Be(2, "Only 2 payments of 170.00 can be satisfied from a 350.00 balance");
        rejected.Count.Should().Be(3, "The remaining 3 payments must be rejected with 409 Conflict (insufficient balance)");

        // 6. Verify non-negative balance invariant
        var cardCheckResp = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBal = JsonDocument.Parse(await cardCheckResp.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        finalBal.Should().Be(10.00m, "Balance must be exactly 350.00 - (2 * 170.00) = 10.00 USD");
        finalBal.Should().BeGreaterOrEqualTo(0m, "Demo card balance must never be negative (overdraft strictly prohibited)");

        // 7. Verify ledger movements in database
        await using var dbConn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await dbConn.OpenAsync();
        await using var dbCmd = dbConn.CreateCommand();
        dbCmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(amount), 0.00) FROM payment_ledger_movements WHERE demo_payment_method_id = @id AND operation_type = 'Debit'";
        dbCmd.Parameters.AddWithValue("@id", cardId);
        await using var reader = await dbCmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetInt64(0).Should().Be(2, "Exactly 2 debit ledger movements must be recorded");
        reader.GetDecimal(1).Should().Be(340.00m, "Sum of recorded debits must match exactly 340.00 USD");
    }
}
