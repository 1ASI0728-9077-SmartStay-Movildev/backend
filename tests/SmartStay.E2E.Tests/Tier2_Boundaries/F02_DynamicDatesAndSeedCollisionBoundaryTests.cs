using System.Net;
using System.Net.Http.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F02")]
public class F02_DynamicDatesAndSeedCollisionBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F02_DynamicDatesAndSeedCollisionBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_B01_InvertedStayDates_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(stayDuration: 2);

        // Invert checkIn and checkOut: checkIn is later than checkOut
        var invertedCheckIn = checkOut;
        var invertedCheckOut = checkIn;

        var payload = new
        {
            roomId = 101,
            guestName = "Inverted Date Guest",
            guestEmail = "inverted@example.com",
            checkInDate = invertedCheckIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = invertedCheckOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Creating booking with inverted stay dates (checkIn > checkOut) must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_B02_ZeroLengthStay_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, _) = TestDateHelper.GetStayDates();

        // Zero-length stay: checkIn == checkOut
        var payload = new
        {
            roomId = 101,
            guestName = "Zero Stay Guest",
            guestEmail = "zero@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Creating booking with zero stay nights (checkIn == checkOut) must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_B03_StayExceeding30Nights_ReturnsBadRequest()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, _) = TestDateHelper.GetStayDates(stayDuration: 35);
        var checkOut = checkIn.AddDays(35); // 35 nights exceeds 30-night boundary

        var payload = new
        {
            roomId = 101,
            guestName = "Long Stay Guest",
            guestEmail = "longstay@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Booking exceeding max stay limit of 30 nights must return 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_B04_SingleNightStay_BoundaryAcceptance()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, _) = TestDateHelper.GetStayDates(stayDuration: 1);
        var checkOut = checkIn.AddDays(1);

        var payload = new
        {
            roomId = 101,
            guestName = "Single Night Guest",
            guestEmail = "single@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Created, HttpStatusCode.OK },
            "Single night stay (1 night duration minimum boundary) should be valid and accepted"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_B05_AdminSeedIdCollision_SignUpRejectsAdminDuplicate()
    {
        // Attempting to register another user named "admin" must be rejected
        var response = await _gatewayClient.SignUpRawAsync("admin", "AdminPassword123!");
        response.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Conflict, HttpStatusCode.BadRequest },
            "Sign-up with existing seeded admin username must return 409 Conflict or 400 Bad Request"
        );
    }
}
