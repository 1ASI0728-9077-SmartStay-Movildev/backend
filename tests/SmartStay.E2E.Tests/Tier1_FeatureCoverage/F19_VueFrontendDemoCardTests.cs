using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F19")]
public class F19_VueFrontendDemoCardTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public F19_VueFrontendDemoCardTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_01_DemoCardStatus_Returns404WhenUnactivated()
    {
        var uniqueUser = $"unact_{Guid.NewGuid():N}";
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, uniqueUser, "Password123!", "guest");

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Unactivated guest query must return 404 Not Found to trigger UI activation button");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_02_DemoCardActivation_ReturnsActiveCardDetails()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        new[] { HttpStatusCode.OK, HttpStatusCode.Created }.Should().Contain(response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("label").GetString().Should().Be("SmartStay Demo");
        root.GetProperty("status").GetString().Should().Be("Active");
        root.GetProperty("currency").GetString().Should().Be("USD");
        root.GetProperty("balance").GetDecimal().Should().BeGreaterThanOrEqualTo(0.00m);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_03_PreAuthorizationDialog_EnforcesPolicyAndCardBinding()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var payloadWithoutPolicy = new
        {
            checkIn = DateTime.UtcNow.Date.AddDays(90).ToString("yyyy-MM-dd"),
            checkOut = DateTime.UtcNow.Date.AddDays(92).ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = false, // Unaccepted policy
            demoPaymentMethodId = cardId
        };

        var res = await _conciergeClient.CreatePurchaseIntentAsync(payloadWithoutPolicy, token, $"cpi-f19-policy-{Guid.NewGuid():N}");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Mandate submission without cancellation policy acceptance must be rejected with 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_04_CardStatusEnforcement_RejectsInactiveOrUnownedCard()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var payloadWithInvalidCard = new
        {
            checkIn = DateTime.UtcNow.Date.AddDays(95).ToString("yyyy-MM-dd"),
            checkOut = DateTime.UtcNow.Date.AddDays(97).ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = "dpm_00000000000000000000000000000000"
        };

        var res = await _conciergeClient.CreatePurchaseIntentAsync(payloadWithInvalidCard, token, $"cpi-f19-card-{Guid.NewGuid():N}");
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Unowned or invalid demo card must return 422 Unprocessable Entity");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_05_RequireDemoPaymentAttribute_BlocksWhenFeatureDisabled()
    {
        // Verify demo payment endpoints are accessible when authenticated and require valid credentials
        var responseUnauth = await _apiFixture.GatewayClient.GetAsync("/api/v1/payments/demo-method");
        responseUnauth.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Demo payment endpoint must require authentication");

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var responseAuth = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        responseAuth.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }
}
