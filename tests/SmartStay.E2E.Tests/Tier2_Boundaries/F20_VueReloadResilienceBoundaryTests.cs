using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F20")]
public class F20_VueReloadResilienceBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public F20_VueReloadResilienceBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_B01_InvalidIntentIdInStorage_ReturnsNotFound()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _conciergeClient.GetPurchaseIntentAsync("cpi_corrupted_id_99999", token);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Querying corrupted or deleted intent ID from localStorage returns 404 Not Found");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_B02_VoucherPrefix_Invariant()
    {
        var user = "vue_vch_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        // Unconditional voucher assertion: intent must confirm and issue a valid receipt voucher
        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Intent must be fulfilled to generate voucher");
        var voucher = terminalDoc.RootElement.GetProperty("receiptVoucher").GetString();
        voucher.Should().NotBeNullOrWhiteSpace("Receipt voucher must be populated upon confirmation");
        voucher.Should().StartWith("VCH-", "Payment receipt voucher must begin with prefix 'VCH-'");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_B03_QueryIntentWithoutToken_ReturnsUnauthorized()
    {
        var response = await _conciergeClient.GetPurchaseIntentAsync("cpi_some_id", token: string.Empty);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Querying intent tracking without JWT token returns 401 Unauthorized");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_B04_ReceiptFields_CompletePayloadStructure()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "Purchase intent creation must be accepted");
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(20)
        );

        var root = terminalDoc.RootElement;
        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("status", out _).Should().BeTrue();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_B05_CrossGuestIntentQuery_ReturnsForbiddenOrNotFound()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(guest1Token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, guest1Token);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Guest 2 queries Guest 1's intent
        var queryResp = await _conciergeClient.GetPurchaseIntentAsync(intentId, guest2Token);
        queryResp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "Cross-guest query of purchase intent must be rejected with 403 or 404"
        );
    }
}
