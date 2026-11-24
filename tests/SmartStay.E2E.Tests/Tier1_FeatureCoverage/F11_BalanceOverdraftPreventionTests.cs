using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F11")]
public class F11_BalanceOverdraftPreventionTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F11_BalanceOverdraftPreventionTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task SetDemoCardBalanceAsync(string cardId, decimal newBalance)
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE demo_payment_methods SET balance = @bal WHERE id = @id";
        cmd.Parameters.AddWithValue("@bal", newBalance);
        cmd.Parameters.AddWithValue("@id", cardId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(int BookingId, decimal TotalAmount)> CreateEligibleBookingAsync(string token, int roomId = 101, int offset = 10)
    {
        var randomOffset = Random.Shared.Next(100, 10000);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(offset + randomOffset, stayDuration: 2);
        var resp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = roomId,
            guestName = "F11 Overdraft Guest",
            guestEmail = $"f11guest-{Guid.NewGuid():N}@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        if (resp.StatusCode != HttpStatusCode.Created)
        {
            var err = await resp.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"CreateBookingAsync failed with {resp.StatusCode}: {err}");
        }
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var id = doc.GetProperty("id").GetInt32();
        var total = doc.GetProperty("totalAmount").GetDecimal();
        return (id, total);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_01_ConcurrentCompetingPurchases_ExceedingBalance()
    {
        var username = $"f11_u1_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var (b1, amt1) = await CreateEligibleBookingAsync(token, roomId: 101, offset: 10);
        var (b2, amt2) = await CreateEligibleBookingAsync(token, roomId: 102, offset: 20);
        var bookings = new[] { (b1, amt1), (b2, amt2) };

        // Set card balance to amt1 + 10: enough for 1 booking, but not both
        var partialBalance = amt1 + 10.00m;
        await SetDemoCardBalanceAsync(cardId, partialBalance);

        var results = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            2,
            async workerId =>
            {
                var req = new
                {
                    bookingId = bookings[workerId].Item1,
                    amount = bookings[workerId].Item2,
                    currency = "USD",
                    paymentMethod = "DemoCard",
                    demoPaymentMethodId = cardId
                };
                return await _gatewayClient.ProcessPaymentAsync(req, token, $"pay-f11-01-{workerId}-{Guid.NewGuid()}");
            },
            TimeSpan.FromSeconds(15)
        );

        // Exactly one should succeed (201/200) and one must fail with 409 Conflict due to insufficient balance
        var successful = results.Count(r => r.IsSuccessStatusCode);
        var conflicted = results.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        successful.Should().Be(1, "Exactly one payment should succeed within available balance");
        conflicted.Should().HaveCount(1, "The competing payment must fail with 409 Conflict");

        var conflictContent = await conflicted[0].Content.ReadAsStringAsync();
        conflictContent.Should().Contain("Insufficient balance", "Conflict must be specifically due to insufficient balance/overdraft");

        // Verify balance remains non-negative
        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(10.00m, "Balance must reflect exactly one debit from partialBalance");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_02_ConcurrentPurchases_WithinTotalBalance()
    {
        var username = $"f11_u2_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Start with 1,000,000 USD (default)
        var (b1, amt1) = await CreateEligibleBookingAsync(token, roomId: 101, offset: 30);
        var (b2, amt2) = await CreateEligibleBookingAsync(token, roomId: 102, offset: 40);
        var bookings = new[] { (b1, amt1), (b2, amt2) };

        var results = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            2,
            async workerId =>
            {
                var req = new
                {
                    bookingId = bookings[workerId].Item1,
                    amount = bookings[workerId].Item2,
                    currency = "USD",
                    paymentMethod = "DemoCard",
                    demoPaymentMethodId = cardId
                };
                return await _gatewayClient.ProcessPaymentAsync(req, token, $"pay-f11-02-{workerId}-{Guid.NewGuid()}");
            },
            TimeSpan.FromSeconds(15)
        );

        results.Should().HaveCount(2);
        results.All(r => r.IsSuccessStatusCode).Should().BeTrue("Both payments must succeed when balance is sufficient");

        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(1000000.00m - amt1 - amt2, "Both payments must be debited from initial balance");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_03_SinglePayment_ExceedingAvailableBalance()
    {
        var username = $"f11_u3_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var (bookingId, totalAmount) = await CreateEligibleBookingAsync(token, roomId: 101, offset: 50);

        // Reduce card balance to less than totalAmount so payment exceeds available balance
        var insufficientBalance = totalAmount - 50.00m;
        await SetDemoCardBalanceAsync(cardId, insufficientBalance);

        var req = new
        {
            bookingId = bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        };

        var response = await _gatewayClient.ProcessPaymentAsync(req, token, "pay-f11-03-" + Guid.NewGuid());
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "Overdraft on eligible booking must be rejected with 409 Conflict");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Insufficient balance", "409 Conflict must be specifically for insufficient balance");

        // Verify balance was untouched
        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(insufficientBalance, "Balance must remain unchanged after rejected overdraft payment");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_04_ExactBalanceDepletion_BoundaryContract()
    {
        var username = $"f11_u4_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var (bookingId1, totalAmount1) = await CreateEligibleBookingAsync(token, roomId: 101, offset: 60);

        // Set card balance to exactly totalAmount1 (exact match for 1 booking)
        await SetDemoCardBalanceAsync(cardId, totalAmount1);

        // 1. Pay exact balance
        var payResp1 = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId = bookingId1,
            amount = totalAmount1,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token, "pay-f11-04a-" + Guid.NewGuid());
        payResp1.IsSuccessStatusCode.Should().BeTrue("Payment of exact balance must succeed");

        // Balance must now be exactly 0.00 USD
        var cardAfter1 = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var bal1 = (await cardAfter1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        bal1.Should().Be(0.00m, "Balance must be exactly 0.00 after depleting payment");

        // 2. Attempt subsequent payment on second eligible booking with 0.00 USD balance
        var (bookingId2, totalAmount2) = await CreateEligibleBookingAsync(token, roomId: 102, offset: 70);
        var payResp2 = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId = bookingId2,
            amount = totalAmount2,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token, "pay-f11-04b-" + Guid.NewGuid());

        payResp2.StatusCode.Should().Be(HttpStatusCode.Conflict, "Payment with 0.00 balance must return 409 Conflict");
        var content2 = await payResp2.Content.ReadAsStringAsync();
        content2.Should().Contain("Insufficient balance");

        var cardAfter2 = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var bal2 = (await cardAfter2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        bal2.Should().Be(0.00m, "Balance must remain 0.00, never negative");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F11")]
    public async Task TC_F11_05_HighContentionBarrierRace_20SimultaneousDebits()
    {
        var username = $"f11_u5_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Create 4 distinct eligible bookings for same room (identical price) on distinct dates
        var b1 = await CreateEligibleBookingAsync(token, roomId: 101, offset: 80);
        var b2 = await CreateEligibleBookingAsync(token, roomId: 101, offset: 90);
        var b3 = await CreateEligibleBookingAsync(token, roomId: 101, offset: 100);
        var b4 = await CreateEligibleBookingAsync(token, roomId: 101, offset: 110);
        var bookings = new[] { b1, b2, b3, b4 };

        // Set card balance to exactly sum of any two bookings (enough for exactly 2 of the 4 bookings)
        var totalForTwo = b1.TotalAmount * 2;
        await SetDemoCardBalanceAsync(cardId, totalForTwo);

        var responses = await ConcurrencyTestHelper.ExecuteConcurrentBarrierAsync(
            4,
            async i =>
            {
                var req = new
                {
                    bookingId = bookings[i].BookingId,
                    amount = bookings[i].TotalAmount,
                    currency = "USD",
                    paymentMethod = "DemoCard",
                    demoPaymentMethodId = cardId
                };
                return await _gatewayClient.ProcessPaymentAsync(req, token, $"pay-f11-05-{i}-{Guid.NewGuid()}");
            },
            TimeSpan.FromSeconds(15)
        );

        responses.Should().HaveCount(4);
        var successCount = responses.Count(r => r.IsSuccessStatusCode);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        successCount.Should().Be(2, "Exactly 2 payments should succeed for totalForTwo credit");
        conflictCount.Should().Be(2, "Remaining 2 payments must fail with 409 Conflict");

        // Verify final balance is 0.00 and never negative
        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(0.00m, "Balance must be exactly 0.00, never negative");
    }
}
