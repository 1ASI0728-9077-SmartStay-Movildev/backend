using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 5: Worker & Broker Failure Recovery.
/// Simulates crash recovery, reconciliation of in-flight purchase intents,
/// TTL mandate expiration handling, and cleanup of stranded room locks.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario5_WorkerBrokerRecovery")]
public class Scenario5_WorkerBrokerRecoveryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public Scenario5_WorkerBrokerRecoveryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario5_Part1_InFlightIntentReconciliation_ReachesTerminalStatus()
    {
        // Setup user and card
        var user = "recov_usr_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "RecovPass123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 9400 + Random.Shared.Next(10, 500), stayDuration: 2);
        var response = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = $"{user}@example.com",
            contactPhone = "+51999999999"
        }, token);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // The background Concierge worker automatically polls pending/active intents and drives them to Confirmed
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(25),
            description: "Awaiting worker reconciliation to terminal state"
        );

        terminalDoc.Should().NotBeNull();
        terminalDoc!.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        terminalDoc.RootElement.GetProperty("receiptVoucher").GetString().Should().StartWith("VCH-");
    }

    [Fact]
    [Trait("Tier", "Tier4")]
    public async Task Scenario5_Part2_MandateTtlExpiration_WorkerReconcilesToExpiredCleanly()
    {
        // Create an intent directly in MySQL with expires_at_utc already in the past
        var user = "recov_exp_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "ExpiredPass123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 9600 + Random.Shared.Next(10, 500), stayDuration: 2);
        var intentResp = await _conciergeClient.CreatePurchaseIntentAsync(new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = $"{user}@example.com",
            contactPhone = "+51999999999"
        }, token);

        var intentId = JsonDocument.Parse(await intentResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Artificially expire the mandate TTL in MySQL
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE purchase_intents SET expires_at_utc = @past WHERE id = @id";
            cmd.Parameters.AddWithValue("@past", DateTime.UtcNow.AddMinutes(-5));
            cmd.Parameters.AddWithValue("@id", intentId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Poll until Concierge Worker marks the intent Expired
        var expiredDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Expired" or "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20),
            description: "Awaiting expired mandate reconciliation"
        );

        expiredDoc.Should().NotBeNull("Purchase intent tracking must return a terminal document");
        var finalStatus = expiredDoc!.RootElement.GetProperty("status").GetString();
        finalStatus.Should().Be("Expired", "Concierge worker must reconcile expired mandate TTL into Expired status");
    }
}
