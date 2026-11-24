using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F13")]
public class F13_DemoMethodInternalValidationBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly InternalPaymentsClient _internalPaymentsClient;

    public F13_DemoMethodInternalValidationBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _internalPaymentsClient = new InternalPaymentsClient(_apiFixture.DirectPaymentsClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_B01_NonExistentCardId_ReturnsNotFound()
    {
        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync("dpm_non_existent_999", 2);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity },
            "Non-existent demo card validation must return 404 or 422"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_B02_NegativeGuestUserId_ReturnsNotFoundOr422()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync(cardId, -1);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity },
            "Validation with negative guestUserId must return 404 or 422"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_B03_ZeroGuestUserId_ReturnsNotFoundOr422()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync(cardId, 0);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity },
            "Validation with zero guestUserId must return 404 or 422"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_B04_GatewayBlocksInternalEndpointWith403()
    {
        var response = await _apiFixture.GatewayClient.GetAsync("/internal/v1/payments/demo-methods/dpm_test/validate?guestUserId=2");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "Gateway must block public access to internal validation endpoints"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_B05_ValidCard_ReturnsValidationPayload_WithExactBalance()
    {
        var user = "val_usr_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");

        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        // IAM assigns user ID >= 2; get userId from token claims
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        var userId = int.Parse(jwt.Claims.First(c => c.Type is System.Security.Claims.ClaimTypes.Sid or System.Security.Claims.ClaimTypes.NameIdentifier or "sid" or "nameid" or "sub").Value);

        var valResp = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync(cardId, userId);
        valResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var valDoc = JsonDocument.Parse(await valResp.Content.ReadAsStringAsync());
        valDoc.RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();
        valDoc.RootElement.GetProperty("status").GetString().Should().Be("Active");
        valDoc.RootElement.GetProperty("currency").GetString().Should().Be("USD");
        valDoc.RootElement.GetProperty("balance").GetDecimal().Should().Be(1000000.00m);
    }
}
