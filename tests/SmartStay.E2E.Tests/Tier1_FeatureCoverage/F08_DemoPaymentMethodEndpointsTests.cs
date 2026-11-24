using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F08")]
public class F08_DemoPaymentMethodEndpointsTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F08_DemoPaymentMethodEndpointsTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_01_DemoPaymentMethod_Activation()
    {
        var freshUser = "act_fresh_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, freshUser, "Password123!", "guest");
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Activating demo method for authenticated guest must return 200 OK");

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("id").GetString().Should().StartWith("dpm_");
        json.RootElement.GetProperty("label").GetString().Should().Be("SmartStay Demo");
        json.RootElement.GetProperty("balance").GetDecimal().Should().Be(1000000.00m);
        json.RootElement.GetProperty("currency").GetString().Should().Be("USD");
        json.RootElement.GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_02_DemoPaymentMethod_Retrieval()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        // Ensure activated
        await _gatewayClient.ActivateDemoPaymentMethodAsync(token);

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Retrieving existing demo method must return 200 OK");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("id").GetString().Should().StartWith("dpm_");
        doc.RootElement.GetProperty("label").GetString().Should().Be("SmartStay Demo");
        doc.RootElement.GetProperty("currency").GetString().Should().Be("USD");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_03_UnactivatedAccountQuery_ReturnsNotFound()
    {
        // Sign up a fresh guest who has never activated a demo card
        var freshUsername = "fresh_guest_" + Guid.NewGuid().ToString("N")[..8];
        var signUpResp = await _gatewayClient.SignUpRawAsync(freshUsername, "Password123!");
        new[] { HttpStatusCode.OK, HttpStatusCode.Created }.Should().Contain(signUpResp.StatusCode);

        var signInResp = await _gatewayClient.SignInRawAsync(freshUsername, "Password123!");
        signInResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await signInResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Querying unactivated demo method must return 404 Not Found");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_04_PciDssCompliance_NoPanCvvOrExpiryInResponse()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var lower = json.ToLowerInvariant();
        lower.Should().NotContain("cardnumber", "PCI DSS compliance: PAN/card number must never appear in demo card responses");
        lower.Should().NotContain("pan", "PCI DSS compliance: PAN must never appear in demo card responses");
        lower.Should().NotContain("cvv", "PCI DSS compliance: CVV must never appear in demo card responses");
        lower.Should().NotContain("cvc", "PCI DSS compliance: CVC must never appear in demo card responses");
        lower.Should().NotContain("expiry", "PCI DSS compliance: Expiry date must not appear in demo card responses");
        lower.Should().NotContain("expiration", "PCI DSS compliance: Expiration date must not appear in demo card responses");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_05_UnauthenticatedRequest_ReturnsUnauthorized()
    {
        // Calling without bearer token must return 401 Unauthorized
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Unauthenticated request to activate demo method must return 401 Unauthorized");
    }
}
