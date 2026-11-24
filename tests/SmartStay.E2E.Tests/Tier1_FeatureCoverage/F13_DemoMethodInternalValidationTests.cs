using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartStay.Contracts.Http;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F13")]
public class F13_DemoMethodInternalValidationTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly InternalPaymentsClient _internalPaymentsClient;

    public F13_DemoMethodInternalValidationTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _internalPaymentsClient = new InternalPaymentsClient(_apiFixture.DirectPaymentsClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_01_ValidActiveDemoCard_Returns200Ok()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var actResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        actResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cardId = (await actResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Call internal validation endpoint for guest 1 (UserId=2)
        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync(cardId, 2);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "Valid owned demo card must return 200 OK from internal validation");

        var val = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>();
        val.Should().NotBeNull();
        val!.IsValid.Should().BeTrue();
        val.Status.Should().Be("Active");
        val.Balance.Should().BeGreaterThan(0);
        val.Currency.Should().Be("USD");
        val.Reason.Should().BeNull();
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_02_NonExistentDemoCard_ReturnsNotFound()
    {
        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync("dpm_nonexistent_9999", 2);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Non-existent demo card ID must return 404 Not Found");

        var val = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>();
        val.Should().NotBeNull();
        val!.IsValid.Should().BeFalse();
        val.Reason.Should().Contain("not found");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_03_CardOwnershipMismatch_ReturnsUnprocessableEntity()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var actResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await actResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Card is owned by Guest 1 (UserId=2), validate with guestUserId=3 (Guest 2)
        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync(cardId, 3);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Card ownership mismatch must return 422 Unprocessable Entity");

        var val = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>();
        val.Should().NotBeNull();
        val!.IsValid.Should().BeFalse();
        val.Reason.Should().Contain("not owned by guest");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_04_InactiveOrUnknownDemoCard_ValidationContract()
    {
        var response = await _internalPaymentsClient.ValidateDemoPaymentMethodAsync("dpm_inactive_test", 2);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.UnprocessableEntity);

        var val = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>();
        val.Should().NotBeNull();
        val!.IsValid.Should().BeFalse();
        val.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F13")]
    public async Task TC_F13_05_GatewayBlocksExternalAccess_With403Forbidden()
    {
        var internalRouteThroughGateway = "/internal/v1/payments/demo-methods/dpm_01/validate?guestUserId=2";
        var response = await _apiFixture.GatewayClient.GetAsync(internalRouteThroughGateway);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Gateway Nginx configuration must block external access to /internal/* with 403 Forbidden");
    }
}
