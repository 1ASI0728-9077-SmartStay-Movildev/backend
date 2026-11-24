using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F06")]
public class F06_BookingsCancellationRefundOutboxBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F06_BookingsCancellationRefundOutboxBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_B01_CancellationOfNonExistentBooking_ReturnsNotFound()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var nonExistentBookingId = 999999;

        var response = await _gatewayClient.CancelBookingAsync(nonExistentBookingId, token);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Cancelling a non-existent booking must return 404 Not Found");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_B02_CancellationByUnauthorizedUser_ReturnsForbiddenOrNotFound()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);

        // Guest 1 creates a booking
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            roomId = 101,
            guestName = "Cancel Guest",
            guestEmail = "cancel@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var createResp = await _gatewayClient.CreateBookingAsync(payload, guest1Token);
        var doc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var bookingId = doc.RootElement.GetProperty("id").GetInt32();

        // Guest 2 attempts to cancel Guest 1's booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, guest2Token);
        cancelResp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound },
            "Cancelling another guest's booking must be rejected"
        );
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_B03_RepeatedCancellationOfCancelledBooking_Idempotent()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            roomId = 101,
            guestName = "Cancel Guest 2",
            guestEmail = "cancel2@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var createResp = await _gatewayClient.CreateBookingAsync(payload, token);
        var doc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var bookingId = doc.RootElement.GetProperty("id").GetInt32();

        // First cancellation
        var cancel1 = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancel1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second cancellation (repeated boundary)
        var cancel2 = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancel2.StatusCode.Should().Be(HttpStatusCode.OK, "Repeated cancellation of already cancelled booking must be idempotent (200 OK)");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_B04_CancellationOfPendingPaymentBooking_ReleasesNightLocksWithoutRefund()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            roomId = 101,
            guestName = "Cancel Guest 2",
            guestEmail = "cancel2@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var createResp = await _gatewayClient.CreateBookingAsync(payload, token);
        var doc = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var bookingId = doc.RootElement.GetProperty("id").GetInt32();

        // Cancel unpaid booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify night locks are deleted / released in MySQL
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM room_night_locks WHERE booking_id = @id";
        cmd.Parameters.AddWithValue("@id", bookingId);
        var locksCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        locksCount.Should().Be(0, "Cancelling booking must release all held room night locks");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_B05_CancellationWithoutToken_ReturnsUnauthorized()
    {
        var response = await _gatewayClient.CancelBookingAsync(12345, token: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "Cancelling booking without JWT token must return 401 Unauthorized");
    }
}
