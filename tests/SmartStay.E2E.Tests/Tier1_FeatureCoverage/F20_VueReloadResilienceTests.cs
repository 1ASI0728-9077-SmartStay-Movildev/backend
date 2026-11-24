using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F20")]
public class F20_VueReloadResilienceTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public F20_VueReloadResilienceTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_01_IntentCreation_PersistsCpiIdToLocalStorageContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 400, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

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

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-store-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString();
        intentId.Should().NotBeNullOrWhiteSpace("Creation must return persistent intent ID for localStorage storage");

        // Verify that the ID returned is directly retrievable via GET endpoint
        var getRes = await _conciergeClient.GetPurchaseIntentAsync(intentId!, token);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_02_PageRefreshSimulation_RecoversInFlightStateViaApi()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 410, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

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

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-refresh-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Simulate page refresh: Vue component calls GET /api/v1/concierge/purchase-intents/{savedIntentId} onMounted
        var recoverRes = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
        recoverRes.StatusCode.Should().Be(HttpStatusCode.OK, "Saved purchase intent ID must recover state on page refresh with 200 OK");

        var recoverDoc = JsonDocument.Parse(await recoverRes.Content.ReadAsStringAsync());
        recoverDoc.RootElement.GetProperty("id").GetString().Should().Be(intentId);
        recoverDoc.RootElement.GetProperty("status").GetString().Should().BeOneOf("Pending", "Reserving", "AwaitingConfirmation", "Confirmed", "Failed");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_03_ConfirmationScreen_DisplaysReceiptVoucherAndBalance()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 420, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

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

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-conf-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Poll until Confirmed
        var confirmedDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return currentDoc.RootElement.GetProperty("status").GetString() == "Confirmed" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(25)
        );

        confirmedDoc.Should().NotBeNull("Intent must reach Confirmed to verify confirmation receipt details");
        confirmedDoc!.RootElement.GetProperty("roomId").GetInt32().Should().BeGreaterThan(0);
        confirmedDoc.RootElement.GetProperty("bookingId").GetInt32().Should().BeGreaterThan(0);
        confirmedDoc.RootElement.GetProperty("receiptVoucher").GetString().Should().StartWith("VCH-");

        // Verify demo card balance is accessible
        var cardAfter = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        cardAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalCardDoc = JsonDocument.Parse(await cardAfter.Content.ReadAsStringAsync());
        finalCardDoc.RootElement.GetProperty("balance").GetDecimal().Should().BeGreaterThanOrEqualTo(0.00m);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_04_TerminalFailure_RemovesLocalStorageItemContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 430, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        // Budget below all rooms
        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 5.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var createRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-fail-{Guid.NewGuid():N}");
        createRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var createDoc = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync());
        var intentId = createDoc.RootElement.GetProperty("id").GetString()!;

        // Poll until terminal state Failed
        var failedDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return currentDoc.RootElement.GetProperty("status").GetString() == "Failed" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        failedDoc.Should().NotBeNull("Intent with insufficient budget must reach terminal Failed state");
        failedDoc!.RootElement.GetProperty("status").GetString().Should().Be("Failed");
        failedDoc.RootElement.GetProperty("failureReason").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F20")]
    public async Task TC_F20_05_ReceiptDismissal_ResetsTrackingAndState()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 440, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

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

        // Create first intent
        var res1 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-reset1-{Guid.NewGuid():N}");
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var id1 = doc1.RootElement.GetProperty("id").GetString()!;

        // After dismissing/resetting tracking state in client, creating a new intent with a fresh idempotency key produces a new independent intent
        var res2 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f20-reset2-{Guid.NewGuid():N}");
        res2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
        var id2 = doc2.RootElement.GetProperty("id").GetString()!;

        id2.Should().NotBe(id1, "After resetting tracking state, new intent must be created independently");
    }
}
