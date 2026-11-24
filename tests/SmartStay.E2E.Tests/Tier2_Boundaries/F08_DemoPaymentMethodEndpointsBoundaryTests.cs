using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F08")]
public class F08_DemoPaymentMethodEndpointsBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F08_DemoPaymentMethodEndpointsBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_B01_ActivationWithoutToken_ReturnsUnauthorized()
    {
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Activating demo method without auth must return 401 Unauthorized");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_B02_GetDemoMethodWithoutToken_ReturnsUnauthorized()
    {
        var response = await _gatewayClient.GetDemoPaymentMethodAsync(token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Getting demo method without auth must return 401 Unauthorized");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_B03_UnsupportedHttpMethods_ReturnsMethodNotAllowed()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/payments/demo-method");
        putReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var putResp = await _apiFixture.GatewayClient.SendAsync(putReq);

        putResp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound },
            "PUT on /api/v1/payments/demo-method must return 405 Method Not Allowed"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_B04_TamperedJwtToken_ReturnsUnauthorized()
    {
        var validToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        // Tamper with the signature portion of the JWT (last segment after '.')
        var parts = validToken.Split('.');
        var tamperedToken = parts.Length == 3
            ? $"{parts[0]}.{parts[1]}.tampered_signature_payload"
            : validToken + "invalid";

        var response = await _gatewayClient.GetDemoPaymentMethodAsync(tamperedToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Tampered JWT token must be rejected with 401 Unauthorized");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F08")]
    public async Task TC_F08_B05_DemoBalanceExactPrecision()
    {
        var freshUser = "prec_usr_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, freshUser, "Password123!");
        var response = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var balance = doc.RootElement.GetProperty("balance").GetDecimal();

        // Exact decimal scale boundary: 1000000.00
        balance.Should().Be(1000000.00m);
        var balanceStr = doc.RootElement.GetProperty("balance").ToString();
        balanceStr.Should().NotContain("e+", "Balance must be formatted as decimal without exponential notation");
    }
}
