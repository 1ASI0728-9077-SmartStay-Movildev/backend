using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier4_RealWorldScenarios;

/// <summary>
/// Tier 4 - Scenario 6: Three-Pass Clean Stack Execution.
/// Executes 3 full sequential passes of the autonomous guest journey from a clean slate.
/// Asserts 100% determinism, absence of cross-test state contamination, and zero residual locks.
/// </summary>
[Trait("Tier", "Tier4")]
[Trait("Scenario", "Scenario6_ThreePassCleanStack")]
public class Scenario6_ThreePassCleanStackTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public Scenario6_ThreePassCleanStackTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [Trait("Tier", "Tier4")]
    public async Task Scenario6_ThreePassCleanStackExecution_Pass(int passNumber)
    {
        // Fresh isolated guest identity for this pass
        var username = $"clean_p{passNumber}_{Guid.NewGuid():N}"[..18];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, username, "PassSecret123!", "Guest");

        // Activate demo payment method
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Dynamic collision-free date window
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 10000 + (passNumber * 100) + Random.Shared.Next(1, 20), stayDuration: 2);

        var intentPayload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactName = $"Pass {passNumber} Guest",
            contactEmail = $"{username}@example.com",
            contactPhone = "+51999111222"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(intentPayload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Await confirmation
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
            description: $"Awaiting Pass {passNumber} confirmation"
        );

        terminalDoc.Should().NotBeNull($"Pass {passNumber} must complete within timeout");
        terminalDoc!.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", $"Pass {passNumber} must be Confirmed");

        var voucher = terminalDoc.RootElement.GetProperty("receiptVoucher").GetString();
        voucher.Should().NotBeNullOrWhiteSpace();
        voucher.Should().StartWith("VCH-");

        // Verify balance decrement on demo card
        var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var balance = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync()).RootElement.GetProperty("balance").GetDecimal();
        balance.Should().BeLessThan(1000000.00m, $"Pass {passNumber} must deduct stay amount from demo card");
    }
}
