using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F19")]
public class F19_VueFrontendDemoCardBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F19_VueFrontendDemoCardBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_B01_UnauthenticatedUser_DemoCardForbidden()
    {
        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Unauthenticated user cannot view demo card widget");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_B02_PreActivationState_Returns404NotFound()
    {
        var freshUser = "vue_fresh_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, freshUser, "Password123!");

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Before activation, GET /demo-method returns 404 to prompt 'Activar tarjeta de prueba'");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_B03_PostActivationState_EnablesAuthorization()
    {
        var freshUser = "vue_act_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, freshUser, "Password123!");

        var actResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        actResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await actResp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("Active");
        doc.RootElement.GetProperty("balance").GetDecimal().Should().Be(1000000.00m);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_B04_PolicyRejection_BlocksIntentSubmission()
    {
        var freshUser = "vue_pol_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, freshUser, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();

        var payload = new
        {
            checkIn = "2030-08-01",
            checkOut = "2030-08-03",
            district = "Miraflores",
            guests = 1,
            budgetTotal = 300.00m,
            currency = "USD",
            cancellationPolicyAccepted = false, // Policy unaccepted in UI
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Submitting authorization dialog without accepting policy returns 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F19")]
    public async Task TC_F19_B05_DemoPaymentHeader_Contract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var resp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("SmartStay Demo");
    }
}
