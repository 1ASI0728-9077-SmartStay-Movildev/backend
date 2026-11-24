using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F17")]
public class F17_CandidateSelectionTieBreakTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ConciergeClient _conciergeClient;
    private readonly GatewayClient _gatewayClient;

    public F17_CandidateSelectionTieBreakTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_01_LowestStayTotalCandidate_PrioritizedFirst()
    {
        // 1. Search rooms via Accommodations for district and guests = 1 (matches Room 101, 103, 102)
        var searchResp = await _gatewayClient.SearchRoomsAsync(district: "Cercado de Lima", guests: 1);
        searchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rooms = await searchResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        rooms.Should().NotBeNull();
        rooms!.Count.Should().BeGreaterThanOrEqualTo(2, "Multiple room options must be available for candidate ranking");

        // Unconditionally assert ordering by lowest stay total / nightly price (no silent skip)
        var price0 = rooms[0].GetProperty("nightlyPrice").GetDecimal();
        var price1 = rooms[1].GetProperty("nightlyPrice").GetDecimal();
        price0.Should().BeLessThanOrEqualTo(price1, "Candidate with lowest nightly price / stay total must be prioritized first");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_02_DeterministicTieBreak_OnLowestRoomId()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        // Pick dynamic date with unique offset
        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 200, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f17-tie-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentDoc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = intentDoc.RootElement.GetProperty("id").GetString()!;

        // Poll for candidate evaluation resolution
        var resolved = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var status = doc.RootElement.GetProperty("status").GetString();
                return status is "Reserving" or "AwaitingConfirmation" or "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        resolved.Should().NotBeNull("Purchase intent must evaluate candidates");
        var roomId = resolved!.RootElement.GetProperty("roomId").GetInt32();
        roomId.Should().Be(101, "When prices tie ($85 for 101 and 103), lowest roomId (101) must be selected as tie-breaker");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_03_FallbackOnPreLockedCandidate_EvaluatesNext()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 210, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        // 1. Pre-lock Room 101 on these dates by booking it directly
        var lockResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Pre-locker",
            guestEmail = "prelock@example.com",
            checkInDate = checkInDt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOutDt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        lockResp.StatusCode.Should().Be(HttpStatusCode.Created, "Pre-locking room 101 must succeed");

        // 2. Submit purchase intent for the same dates; candidate 1 (Room 101) is locked, so it must evaluate next (Room 103)
        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f17-fall-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentDoc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = intentDoc.RootElement.GetProperty("id").GetString()!;

        // Poll for candidate evaluation resolution
        var resolved = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var status = doc.RootElement.GetProperty("status").GetString();
                return status is "Reserving" or "AwaitingConfirmation" or "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        resolved.Should().NotBeNull("Purchase intent must evaluate candidates");
        var status = resolved!.RootElement.GetProperty("status").GetString();
        status.Should().BeOneOf("Reserving", "AwaitingConfirmation", "Confirmed");
        var roomId = resolved.RootElement.GetProperty("roomId").GetInt32();
        roomId.Should().Be(103, "When Room 101 is pre-locked, worker must evaluate and select next candidate (Room 103)");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_04_MaxThreeCandidateEvaluations_Enforced()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 220, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        // 1. Pre-lock all 3 rooms in Cercado de Lima (101, 103, 102) on these dates
        foreach (var rId in new[] { 101, 103, 102 })
        {
            var lResp = await _gatewayClient.CreateBookingAsync(new
            {
                roomId = rId,
                guestName = $"Pre-locker-{rId}",
                guestEmail = $"prelock{rId}@example.com",
                checkInDate = checkInDt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                checkOutDate = checkOutDt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, token);
            lResp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 2. Submit purchase intent: all 3 candidates are locked, reaching candidate evaluation limit
        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f17-max3-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentDoc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = intentDoc.RootElement.GetProperty("id").GetString()!;

        // 3. Poll until terminal state Failed
        var failedDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Failed" ? doc : null;
            },
            doc => doc != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        failedDoc.Should().NotBeNull("Purchase intent must transition to Failed after exhausting candidate evaluations");
        var reason = failedDoc!.RootElement.GetProperty("failureReason").GetString();
        reason.Should().NotBeNullOrWhiteSpace("Failure reason must explain exhausted candidates");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_05_ImmediateBudgetFailure_WhenZeroCandidatesWithinBudget()
    {
        var checkIn = DateTime.UtcNow.Date.AddDays(75).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(77).ToString("yyyy-MM-dd");

        // 1. Verify conversational recommendation endpoint with budget lower than all catalog rooms
        var messageRequest = new
        {
            message = "Busco habitación ultrabarata.",
            checkIn,
            checkOut,
            guests = 2,
            district = "Cercado de Lima",
            budgetTotal = 10.00m, // Below all rooms ($85, $150, $320)
            currency = "USD"
        };

        var msgResponse = await _conciergeClient.SendMessageAsync(messageRequest);
        msgResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var msgDoc = JsonDocument.Parse(await msgResponse.Content.ReadAsStringAsync());
        var offers = msgDoc.RootElement.GetProperty("offers").EnumerateArray().ToList();
        offers.Should().BeEmpty("Zero offers must be returned when all available rooms exceed user budget");
        msgDoc.RootElement.GetProperty("fallbackUsed").GetBoolean().Should().BeTrue();

        // 2. Verify purchase intent endpoint transitions to Failed for budget failure
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var intentPayload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 10.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(intentPayload, token, $"cpi-f17-budget-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentDoc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = intentDoc.RootElement.GetProperty("id").GetString()!;

        // Poll until terminal state Failed
        var failedDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Failed" ? doc : null;
            },
            doc => doc != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        failedDoc.Should().NotBeNull("Purchase intent with budget below catalog rooms must fail");
        var reason = failedDoc!.RootElement.GetProperty("failureReason").GetString();
        reason.Should().ContainEquivalentOf("budget", "Failure reason must state that budget was exceeded");
    }
}
