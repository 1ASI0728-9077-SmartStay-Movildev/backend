using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F17")]
public class F17_CandidateSelectionTieBreakBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly ConciergeClient _conciergeClient;

    public F17_CandidateSelectionTieBreakBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _conciergeClient = new ConciergeClient();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_B01_NonExistentDistrict_TransitionsToFailed()
    {
        var user = "bnd_f17_1_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "AtlantisUnderwaterDistrict", // Non-existent district boundary
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Failed" or "Confirmed" or "Expired" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Failed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_B02_PriceTieBreak_DeterministicLowestRoomId()
    {
        // Cercado de Lima has Room 101 ($85) and Room 103 ($85)
        var user = "bnd_f17_2_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 200.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15)
        );

        // Unconditional tie-breaker assertion: intent must confirm and select lowest room ID (101 over 103)
        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Intent must be fulfilled to evaluate tie-breaker");
        var roomId = terminalDoc.RootElement.GetProperty("roomId").GetInt32();
        roomId.Should().Be(101, "Tie-breaker between equal-price candidates must select lowest RoomId (101 over 103)");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_B03_GuestCapacityFilter_RejectsUnderCapacityRooms()
    {
        // Search rooms for 10 guests in Miraflores (no room has capacity 10)
        var response = await _gatewayClient.SearchRoomsAsync(district: "Miraflores", guests: 10);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetArrayLength().Should().Be(0, "Searching for 10 guests must return 0 matching rooms when max capacity is 4");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_B04_ExtremeBudgetShortfall_RejectsImmediately()
    {
        var user = "bnd_f17_4_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 1.00m, // Extreme low budget boundary ($1.00)
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "Extreme low budget intent is accepted and evaluated asynchronously by background worker");

        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Failed" or "Confirmed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Failed", "Intent with extreme low budget must transition to Failed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F17")]
    public async Task TC_F17_B05_MaxThreeCandidatesAttemptLimit()
    {
        var user = "bnd_f17_5_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();

        // Pre-lock all 3 rooms in Cercado de Lima (101, 103, 102)
        foreach (var roomId in new[] { 101, 103, 102 })
        {
            await _gatewayClient.CreateBookingAsync(new
            {
                roomId,
                guestName = "Pre-lock Guest",
                guestEmail = $"prelock{roomId}@example.com",
                checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }, token);
        }

        // Now create intent for the same dates and district
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 500.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _conciergeClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Failed" or "Confirmed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Failed", "When all candidates are unavailable, worker must exhaust attempts and mark intent Failed");
    }
}
