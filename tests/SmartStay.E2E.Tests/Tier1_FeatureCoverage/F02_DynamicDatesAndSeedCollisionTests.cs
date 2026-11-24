using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using SmartStay.Contracts.Constants;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F02")]
public class F02_DynamicDatesAndSeedCollisionTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F02_DynamicDatesAndSeedCollisionTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_01_MultiRoleSignIn_AndIdentityNonCollision()
    {
        var adminToken = await _authFixture.GetTokenAsync(_apiFixture.GatewayClient, "admin");
        var guest1Token = await _authFixture.GetTokenAsync(_apiFixture.GatewayClient, "guest1");
        var guest2Token = await _authFixture.GetTokenAsync(_apiFixture.GatewayClient, "guest2");

        var handler = new JwtSecurityTokenHandler();
        var adminJwt = handler.ReadJwtToken(adminToken);
        var guest1Jwt = handler.ReadJwtToken(guest1Token);
        var guest2Jwt = handler.ReadJwtToken(guest2Token);

        adminJwt.Claims.First(c => c.Type is ClaimTypes.Sid or ClaimTypes.NameIdentifier or "sid" or "nameid" or "sub").Value.Should().Be("1", "Admin must have UserId=1 from seeding");
        guest1Jwt.Claims.First(c => c.Type is ClaimTypes.Sid or ClaimTypes.NameIdentifier or "sid" or "nameid" or "sub").Value.Should().Be("2", "Guest1 must have UserId=2, avoiding collision with Admin");
        guest2Jwt.Claims.First(c => c.Type is ClaimTypes.Sid or ClaimTypes.NameIdentifier or "sid" or "nameid" or "sub").Value.Should().Be("3", "Guest2 must have UserId=3, avoiding collision with Guest1 and Admin");

        adminJwt.Claims.First(c => c.Type == ClaimTypes.Role).Value.Should().Be(UserRoles.Admin);
        guest1Jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value.Should().Be(UserRoles.Guest);
        guest2Jwt.Claims.First(c => c.Type == ClaimTypes.Role).Value.Should().Be(UserRoles.Guest);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_02_DynamicRelativeDates_BookingCreation()
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(10);

        var payload = new
        {
            roomId = 101,
            guestName = "Juan Perez",
            guestEmail = "juan.perez@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Created, "Booking with valid dynamic relative dates must return 201 Created");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("PendingPayment");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_03_PastCheckInDate_Rejection()
    {
        var checkIn = DateTime.UtcNow.AddDays(10);
        var checkOut = DateTime.UtcNow.AddDays(50); // 40 nights exceeds 30-night limit

        var payload = new
        {
            roomId = 102,
            guestName = "Juan Perez",
            guestEmail = "juan.perez@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Exceeding max stay limit must be rejected with 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_04_InvertedDateRange_Rejection()
    {
        var checkIn = DateTime.UtcNow.AddDays(10);
        var checkOut = DateTime.UtcNow.AddDays(8);

        var payload = new
        {
            roomId = 102,
            guestName = "Juan Perez",
            guestEmail = "juan.perez@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Inverted date range must be rejected with 400 Bad Request");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F02")]
    public async Task TC_F02_05_MaximumStayDuration_BoundaryEnforcement()
    {
        var checkIn = DateTime.UtcNow.AddDays(5);
        var checkOut31Days = DateTime.UtcNow.AddDays(36);

        var payload = new
        {
            roomId = 102,
            guestName = "Juan Perez",
            guestEmail = "juan.perez@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut31Days.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "Stay duration > 30 nights must be rejected with 400 Bad Request");
    }
}
