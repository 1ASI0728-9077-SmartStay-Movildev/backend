using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 1: Full Happy Path Guest Journey.
/// Simulates complete guest lifecycle: Registration -> Activation -> Discovery -> Intent -> Autonomous Fulfillment -> Voucher.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario1_FullHappyPath")]
public class Scenario1_FullHappyPathGuestJourneyTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public Scenario1_FullHappyPathGuestJourneyTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario1_CompleteAutonomousFulfillment_GuestJourney()
    {
        // 1. Guest Registration & Authentication
        var username = "journey_guest_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "SecurePass123!", "Guest");
        token.Should().NotBeNullOrWhiteSpace("Registered guest must receive valid JWT");

        // 2. Demo Payment Method Activation
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        cardResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;
        var initialBalance = cardDoc.RootElement.GetProperty("balance").GetDecimal();
        initialBalance.Should().Be(1000000.00m, "Demo card must initialize with 1,000,000.00 USD");

        // 3. Accommodation & Room Discovery
        var searchResp = await _gatewayClient.SearchRoomsAsync(district: "Cercado de Lima", guests: 1);
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchDoc = JsonDocument.Parse(await searchResp.Content.ReadAsStringAsync());
        searchDoc.RootElement.GetArrayLength().Should().BeGreaterThan(0, "Accommodations search must return matching rooms in district");

        // 4. Create Autonomous Purchase Intent Mandate
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 5000 + Random.Shared.Next(10, 1000), stayDuration: 2);
        var intentPayload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactName = "Autonomous Guest",
            contactEmail = $"{username}@example.com",
            contactPhone = "+51999888777"
        };

        var mandateResp = await _conciergeClient.CreatePurchaseIntentAsync(intentPayload, token);
        mandateResp.StatusCode.Should().Be(HttpStatusCode.Accepted, "Purchase intent mandate must be accepted for processing");
        var intentId = JsonDocument.Parse(await mandateResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // 5. Polling Autonomous Orchestration until Confirmed
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!r.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(25),
            description: "Awaiting autonomous fulfillment completion"
        );

        terminalDoc.Should().NotBeNull("Autonomous orchestration must reach a terminal state within timeout");
        terminalDoc!.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Intent must be fulfilled successfully");

        // 6. Verify Voucher Receipt Invariant
        var voucher = terminalDoc.RootElement.GetProperty("receiptVoucher").GetString();
        voucher.Should().NotBeNullOrWhiteSpace();
        voucher.Should().StartWith("VCH-", "Receipt voucher must bear standard 'VCH-' prefix");

        var bookingId = terminalDoc.RootElement.GetProperty("bookingId").GetInt32();
        var paymentId = terminalDoc.RootElement.GetProperty("paymentId").GetInt32();
        var stayTotal = terminalDoc.RootElement.GetProperty("stayTotal").GetDecimal();
        bookingId.Should().BeGreaterThan(0);
        paymentId.Should().BeGreaterThan(0);
        stayTotal.Should().BeGreaterThan(0m);

        // 7. Verify Exact Algebraic Balance Deduction on Demo Card
        var postCheckResp = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = JsonDocument.Parse(await postCheckResp.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(initialBalance - stayTotal, "Demo card balance must be decremented by exactly stayTotal");

        // 8. Database Persistence Audit
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Check booking table with async event processing polling
        int bookingStatus = -1;
        decimal totalAmount = 0m;
        await PollingHelper.PollUntilConditionAsync(async () =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT status, total_amount FROM bookings WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", bookingId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                bookingStatus = reader.GetInt32(0);
                totalAmount = reader.GetDecimal(1);
                return bookingStatus == 1;
            }
            return false;
        }, timeout: TimeSpan.FromSeconds(5), description: "Booking transitions to Confirmed state (1)");

        bookingStatus.Should().Be(1, "Booking in database must be in Confirmed state (1)");
        totalAmount.Should().Be(stayTotal);

        // Check payment ledger movement
        string? opType = null;
        decimal ledgerAmount = 0m;
        decimal ledgerBalanceAfter = 0m;
        await PollingHelper.PollUntilConditionAsync(async () =>
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT operation_type, amount, balance_after FROM payment_ledger_movements WHERE payment_id = @pid";
            cmd.Parameters.AddWithValue("@pid", paymentId);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                opType = reader.GetString(0);
                ledgerAmount = reader.GetDecimal(1);
                ledgerBalanceAfter = reader.GetDecimal(2);
                return true;
            }
            return false;
        }, timeout: TimeSpan.FromSeconds(5), description: "Payment ledger movement recorded");

        opType.Should().Be("Debit");
        ledgerAmount.Should().Be(stayTotal);
        ledgerBalanceAfter.Should().Be(finalBalance);
    }
}
