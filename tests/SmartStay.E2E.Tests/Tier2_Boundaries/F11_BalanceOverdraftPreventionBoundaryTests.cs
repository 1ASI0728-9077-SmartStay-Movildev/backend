using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F11")]
public class F11_BalanceOverdraftPreventionBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F11_BalanceOverdraftPreventionBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, decimal TotalAmount)> CreateEligibleBookingAsync(string token, int roomId = 101)
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            roomId,
            guestName = "Overdraft Guest",
            guestEmail = "overdraft@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bookingId = doc.RootElement.GetProperty("id").GetInt32();
        var total = doc.RootElement.GetProperty("totalAmount").GetDecimal();
        return (bookingId, total);
    }

    private async Task SetCardBalanceAsync(string cardId, decimal balance)
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE demo_payment_methods SET balance = @bal WHERE id = @id";
        cmd.Parameters.AddWithValue("@bal", balance);
        cmd.Parameters.AddWithValue("@id", cardId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_B01_OverdraftBoundary_ZeroBalanceRejection()
    {
        var user = "bnd_ovd_1_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Explicit boundary: Balance = $0.00
        await SetCardBalanceAsync(cardId, 0.00m);

        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        payResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await payResp.Content.ReadAsStringAsync();
        body.Should().Contain("Insufficient balance", "Payment on 0.00 balance must return 409 Conflict with 'Insufficient balance'");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_B02_OverdraftBoundary_OneCentShortRejection()
    {
        var user = "bnd_ovd_2_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Explicit boundary: Balance is exactly 0.01 less than required
        var balance = total - 0.01m;
        await SetCardBalanceAsync(cardId, balance);

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        payResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await payResp.Content.ReadAsStringAsync();
        body.Should().Contain("Insufficient balance", "Payment when available balance is 0.01 less than total must return 409 Conflict");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_B03_ExactZeroRemainingBalance_BoundarySuccess()
    {
        var user = "bnd_ovd_3_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Exact boundary: Balance equals total amount exactly
        await SetCardBalanceAsync(cardId, total);

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = total,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token);

        payResp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.OK, HttpStatusCode.Created },
            "Payment where balance == total must succeed"
        );

        // Verify remaining balance in MySQL is exactly 0.00
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT balance FROM demo_payment_methods WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", cardId);
        var remaining = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
        remaining.Should().Be(0.00m, "Remaining balance after exact depletion must be exactly 0.00");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_B04_ConcurrentRacingDebits_NeverLeavesNegativeBalance()
    {
        var user = "bnd_ovd_4_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (booking1, total1) = await CreateEligibleBookingAsync(token, roomId: 101);
        var (booking2, total2) = await CreateEligibleBookingAsync(token, roomId: 102);

        // Balance is $200.00; two competing purchases of $170 each (total $340 > $200)
        await SetCardBalanceAsync(cardId, 200.00m);

        var bookings = new[] { (booking1, total1), (booking2, total2) };

        var statusCodes = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(2, async idx =>
        {
            var (bId, amt) = bookings[idx];
            var resp = await _gatewayClient.ProcessPaymentAsync(new
            {
                bookingId = bId,
                amount = amt,
                currency = "USD",
                paymentMethod = "DemoCard",
                demoPaymentMethodId = cardId
            }, token);
            return resp.StatusCode;
        });

        // Exactly one must succeed (200 OK or 201 Created), one must fail (409 Conflict)
        statusCodes.Should().Contain(code => code == HttpStatusCode.OK || code == HttpStatusCode.Created);
        statusCodes.Should().Contain(HttpStatusCode.Conflict);

        // Final balance must be non-negative ($30.00)
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT balance FROM demo_payment_methods WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", cardId);
        var finalBal = Convert.ToDecimal(await cmd.ExecuteScalarAsync());

        finalBal.Should().Be(30.00m, "Final balance must be non-negative and reflect exactly one successful debit");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_B05_ZeroCreditPurchaseAttempt_ConsistentlyRejected()
    {
        var user = "bnd_ovd_5_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        await SetCardBalanceAsync(cardId, 0.00m);

        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Multiple attempts on zero credit must all be rejected consistently
        for (var i = 0; i < 3; i++)
        {
            var resp = await _gatewayClient.ProcessPaymentAsync(new
            {
                bookingId,
                amount = total,
                currency = "USD",
                paymentMethod = "DemoCard",
                demoPaymentMethodId = cardId
            }, token);

            resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }
}
