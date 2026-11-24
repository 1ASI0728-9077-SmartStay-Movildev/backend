using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F15")]
public class F15_ConciergePurchaseIntentApiBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public F15_ConciergePurchaseIntentApiBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_B01_UnacceptedCancellationPolicy_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = false, // Boundary: not accepted
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Creating intent without accepted cancellation policy must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_B02_InvertedDates_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkOut.ToString("yyyy-MM-dd"), // Boundary: inverted dates
            checkOut = checkIn.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Inverted stay dates must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_B03_ZeroGuests_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 0, // Boundary: guests < 1
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Zero guests must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_B04_InvalidCardGuid_Returns422()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();

        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = "dpm_00000000-0000-0000-0000-000000000000", // Non-existent card
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Unowned or invalid demo card must return 422 Unprocessable Entity");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_B05_MandateTtl_StrictFifteenMinuteBoundary()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var beforeReq = DateTime.UtcNow;

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

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expiresAtUtc = doc.RootElement.GetProperty("expiresAtUtc").GetDateTime();

        var ttl = expiresAtUtc - beforeReq;
        ttl.TotalMinutes.Should().BeInRange(13.5, 16.5, "Mandate TTL must be approximately 15 minutes");
    }
}
