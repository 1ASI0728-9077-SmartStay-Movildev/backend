using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F14")]
public class F14_BookingsQuotesTtlBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly InternalBookingsClient _internalBookingsClient;

    public F14_BookingsQuotesTtlBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _internalBookingsClient = new InternalBookingsClient(_apiFixture.DirectBookingsClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_B01_QuoteTtl_StrictTwoMinuteBoundary()
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var beforeReq = DateTime.UtcNow;

        var response = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_quote_test_01",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            guests = 1
        });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var expiresAtUtc = doc.RootElement.GetProperty("expiresAtUtc").GetDateTime();

        var ttl = expiresAtUtc - beforeReq;
        ttl.TotalSeconds.Should().BeInRange(110, 130, "Quote TTL must be approximately 2 minutes (120 seconds)");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_B02_QuoteWithInvertedDates_ReturnsBadRequest()
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();

        var response = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_quote_test_01",
            checkIn = checkOut.ToString("yyyy-MM-dd"), // Inverted: checkIn after checkOut
            checkOut = checkIn.ToString("yyyy-MM-dd"),
            guests = 1
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            "Quote with inverted dates (checkIn >= checkOut) must return 400 Bad Request"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_B03_QuoteWithExcessiveStayDuration_ReturnsBadRequest()
    {
        var (checkIn, _) = TestDateHelper.GetStayDates();
        var excessiveCheckOut = checkIn.AddDays(35); // 35 nights > 30 night maximum limit

        var response = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101,
            guestUserId = 2,
            purchaseIntentId = "cpi_quote_test_01",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = excessiveCheckOut.ToString("yyyy-MM-dd"),
            guests = 1
        });

        response.StatusCode.Should().Be(
            HttpStatusCode.BadRequest,
            "Quote with stay duration exceeding 30 nights must return 400 Bad Request"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_B04_QuoteWithGuestsExceedingCapacity_ReturnsBadRequest()
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();

        var response = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 101, // Capacity 2
            guestUserId = 2,
            purchaseIntentId = "cpi_quote_test_01",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            guests = 10 // Extreme excess
        });

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity },
            "Quote exceeding room capacity must be rejected"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F14")]
    public async Task TC_F14_B05_QuoteForNonExistentRoom_ReturnsNotFound()
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();

        var response = await _internalBookingsClient.CreateQuoteAsync(new
        {
            roomId = 999999, // Non-existent room
            guestUserId = 2,
            purchaseIntentId = "cpi_quote_test_01",
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            guests = 1
        });

        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest },
            "Quote for non-existent room must return 404 Not Found"
        );
    }
}
