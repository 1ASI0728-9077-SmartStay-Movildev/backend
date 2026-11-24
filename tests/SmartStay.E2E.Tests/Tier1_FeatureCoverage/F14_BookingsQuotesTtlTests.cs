using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F14")]
public class F14_BookingsQuotesTtlTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;
    private readonly InternalBookingsClient _internalBookingsClient;

    public F14_BookingsQuotesTtlTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
        _internalBookingsClient = new InternalBookingsClient(_apiFixture.DirectBookingsClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_01_ValidQuoteCreation_With2MinuteExpiry()
    {
        var requestTime = DateTime.UtcNow;
        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(100);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            roomId = 102,
            guestUserId = 2,
            purchaseIntentId = "cpi_test_01",
            checkIn,
            checkOut,
            guests = 2
        };

        var response = await _internalBookingsClient.CreateQuoteAsync(payload);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "Valid quote request must return 201 Created");

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var quoteId = doc.RootElement.GetProperty("quoteId").GetString();
        quoteId.Should().StartWith("qte_");

        var stayTotal = doc.RootElement.GetProperty("stayTotal").GetDecimal();
        stayTotal.Should().BeGreaterThan(0);

        var expiresAtUtc = doc.RootElement.GetProperty("expiresAtUtc").GetDateTime();
        var ttl = expiresAtUtc - requestTime;
        ttl.TotalSeconds.Should().BeInRange(105, 125, "Quote TTL must be exactly 2 minutes (120 seconds)");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_02_ExceededRoomCapacity_ReturnsUnprocessableEntity()
    {
        var dOffset = 2100 + Random.Shared.Next(1, 100);
        var payload = new
        {
            roomId = 102,
            guestUserId = 2,
            purchaseIntentId = "cpi_test_02",
            checkIn = DateTime.UtcNow.Date.AddDays(dOffset).ToString("yyyy-MM-dd"),
            checkOut = DateTime.UtcNow.Date.AddDays(dOffset + 2).ToString("yyyy-MM-dd"),
            guests = 20 // Exceeds room capacity of 2
        };

        var response = await _internalBookingsClient.CreateQuoteAsync(payload);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "Exceeding room capacity must return 422 Unprocessable Entity");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("CapacityExceeded");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_03_InvalidDateRange_ReturnsBadRequest()
    {
        var payload = new
        {
            roomId = 102,
            guestUserId = 2,
            purchaseIntentId = "cpi_test_03",
            checkIn = "2026-12-05",
            checkOut = "2026-12-05", // 0 nights
            guests = 2
        };

        var response = await _internalBookingsClient.CreateQuoteAsync(payload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Zero or inverted date range must return 400 Bad Request");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("CheckIn date must be before CheckOut date.");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_04_RoomAlreadyBooked_ReturnsConflict()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkInDate, checkOutDate) = TestDateHelper.GetStayDates(110);

        // Pre-book room 101 for these dates
        var bookResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Pre-booked Guest",
            guestEmail = "prebooked@example.com",
            checkInDate = checkInDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOutDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Attempt quote for same room and dates
        var payload = new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_test_04",
            checkIn = checkInDate.ToString("yyyy-MM-dd"),
            checkOut = checkOutDate.ToString("yyyy-MM-dd"),
            guests = 2
        };

        var response = await _internalBookingsClient.CreateQuoteAsync(payload);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "Attempting quote on an already-booked room must return 409 Conflict");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_05_NonExistentRoomId_ReturnsNotFound()
    {
        var offset5 = 2300 + Random.Shared.Next(1, 100);
        var payload = new
        {
            roomId = 999999,
            guestUserId = 2,
            purchaseIntentId = "cpi_test_05",
            checkIn = DateTime.UtcNow.Date.AddDays(offset5).ToString("yyyy-MM-dd"),
            checkOut = DateTime.UtcNow.Date.AddDays(offset5 + 2).ToString("yyyy-MM-dd"),
            guests = 2
        };

        var response = await _internalBookingsClient.CreateQuoteAsync(payload);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Quote for non-existent room must return 404 Not Found");
    }
}
