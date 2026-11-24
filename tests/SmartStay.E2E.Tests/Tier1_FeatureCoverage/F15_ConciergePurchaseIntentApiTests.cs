using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F15")]
public class F15_ConciergePurchaseIntentApiTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ConciergeClient _conciergeClient;
    private readonly GatewayClient _gatewayClient;

    public F15_ConciergePurchaseIntentApiTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        // Use DirectConciergeUrl to avoid Gateway rate limits on test execution
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_01_CreatePurchaseIntent_ReturnsAccepted()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        cardRes.IsSuccessStatusCode.Should().BeTrue();
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(30).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(32).ToString("yyyy-MM-dd");
        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f15-01-{Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "Valid mandate must be accepted for asynchronous orchestration");

        var content = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var intentId = root.GetProperty("id").GetString();
        intentId.Should().NotBeNullOrWhiteSpace();
        intentId.Should().StartWith("cpi_");

        root.GetProperty("status").GetString().Should().Be("Pending");
        root.GetProperty("guestUserId").GetInt32().Should().Be(2);

        var expiresAtUtc = root.GetProperty("expiresAtUtc").GetDateTime();
        var createdAtUtc = root.GetProperty("createdAtUtc").GetDateTime();
        var ttlMinutes = (expiresAtUtc - createdAtUtc).TotalMinutes;
        ttlMinutes.Should().BeInRange(14.0, 16.0, "Mandate TTL must be approximately 15 minutes");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_02_GuardOnInvalidOrUnownedDemoCard_Returns422()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var checkIn = DateTime.UtcNow.Date.AddDays(35).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(37).ToString("yyyy-MM-dd");

        var payloadWithInvalidCard = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = "dpm_00000000000000000000000000000000"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payloadWithInvalidCard, token, $"cpi-f15-02-{Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Unowned or invalid demo card must be rejected by pre-flight validation");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Payment method not found");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_03_RejectionWhenPolicyNotAccepted_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(40).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(42).ToString("yyyy-MM-dd");

        var payloadUnacceptedPolicy = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = false, // Explicitly rejected
            demoPaymentMethodId = cardId
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payloadUnacceptedPolicy, token, $"cpi-f15-03-{Guid.NewGuid():N}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Mandate submission without cancellation policy acceptance must return 400 Bad Request");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().ContainEquivalentOf("cancellation policy");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_04_QueryIntentById_EnforcesGuestIsolation()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(guest1Token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(45).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(47).ToString("yyyy-MM-dd");
        var createPayload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(createPayload, guest1Token, $"cpi-f15-04-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Guest 2 attempts to query Guest 1's purchase intent
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);
        var getRes = await _conciergeClient.GetPurchaseIntentAsync(intentId, guest2Token);
        getRes.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cross-user purchase intent access must be blocked with 403 Forbidden");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F15")]
    public async Task TC_F15_05_CancelIntent_InitiatesCancellation()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(50).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(52).ToString("yyyy-MM-dd");
        var createPayload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(createPayload, token, $"cpi-f15-05-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Immediate cancellation by owner
        var cancelRes = await _conciergeClient.CancelPurchaseIntentAsync(intentId, token);
        new[] { HttpStatusCode.OK, HttpStatusCode.Accepted }.Should().Contain(cancelRes.StatusCode, "Cancellation must be acknowledged with 200 OK or 202 Accepted");

        var cancelDoc = JsonDocument.Parse(await cancelRes.Content.ReadAsStringAsync());
        var status = cancelDoc.RootElement.GetProperty("status").GetString();
        status.Should().BeOneOf("Cancelling", "Cancelled", "Failed");
    }
}
