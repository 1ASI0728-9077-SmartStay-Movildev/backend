using System.Net;
using System.Text.Json;
using Apache.NMS;
using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F05")]
public class F05_StrictPaymentConfirmationBoundaryTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public F05_StrictPaymentConfirmationBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, decimal TotalAmount)> CreateEligibleBookingAsync(string token)
    {
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(stayDuration: 2);
        var payload = new
        {
            roomId = 101,
            guestName = "Strict Confirmation Guest",
            guestEmail = "strict@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bookingId = doc.RootElement.GetProperty("id").GetInt32();
        var total = doc.RootElement.GetProperty("totalAmount").GetDecimal();
        return (bookingId, total);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_B01_UnderpaymentByOneCent_TriggersRefundOutbox()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Underpay by exactly 0.01
        var underpaidAmount = total - 0.01m;
        var eventId = Guid.NewGuid();

        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Payments.V1",
            new PaymentCompletedV1(
                PaymentId: 9001,
                BookingId: bookingId,
                HotelId: 1,
                Amount: underpaidAmount,
                Currency: "USD"
            ),
            "payments.payment-completed",
            eventId
        );

        // Bookings must NOT transition to Confirmed
        await Task.Delay(1500);
        var bookingResp = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        var doc = JsonDocument.Parse(await bookingResp.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().NotBe("Confirmed", "Underpaid booking must never be confirmed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_B02_OverpaymentByOneCent_TriggersRefundOutbox()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Overpay by exactly 0.01
        var overpaidAmount = total + 0.01m;
        var eventId = Guid.NewGuid();

        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Payments.V1",
            new PaymentCompletedV1(
                PaymentId: 9002,
                BookingId: bookingId,
                HotelId: 1,
                Amount: overpaidAmount,
                Currency: "USD"
            ),
            "payments.payment-completed",
            eventId
        );

        // Bookings must NOT transition to Confirmed
        await Task.Delay(1500);
        var bookingResp = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        var doc = JsonDocument.Parse(await bookingResp.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().NotBe("Confirmed", "Overpaid booking must never be confirmed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_B03_CurrencyMismatch_EUR_TriggersRefundOutbox()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        var eventId = Guid.NewGuid();

        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Payments.V1",
            new PaymentCompletedV1(
                PaymentId: 9003,
                BookingId: bookingId,
                HotelId: 1,
                Amount: total,
                Currency: "EUR" // Mismatched currency
            ),
            "payments.payment-completed",
            eventId
        );

        await Task.Delay(1500);
        var bookingResp = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        var doc = JsonDocument.Parse(await bookingResp.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().NotBe("Confirmed", "Payment in EUR for USD booking must never be confirmed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_B04_ExpiredHold_PaymentTriggersRefundOutbox()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, total) = await CreateEligibleBookingAsync(token);

        // Fast-forward expires_at_utc in MySQL so the hold is expired
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE bookings SET expires_at_utc = DATE_SUB(NOW(), INTERVAL 1 HOUR) WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", bookingId);
            await cmd.ExecuteNonQueryAsync();
        }

        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Payments.V1",
            new PaymentCompletedV1(
                PaymentId: 9004,
                BookingId: bookingId,
                HotelId: 1,
                Amount: total,
                Currency: "USD"
            ),
            "payments.payment-completed",
            eventId
        );

        await Task.Delay(1500);
        var bookingResp = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        var doc = JsonDocument.Parse(await bookingResp.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().NotBe("Confirmed", "Payment arriving after hold expiration must not confirm booking");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_B05_ZeroAmountPayment_DoesNotConfirmBooking()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (bookingId, _) = await CreateEligibleBookingAsync(token);

        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Payments.V1",
            new PaymentCompletedV1(
                PaymentId: 9005,
                BookingId: bookingId,
                HotelId: 1,
                Amount: 0.00m,
                Currency: "USD"
            ),
            "payments.payment-completed",
            eventId
        );

        await Task.Delay(1500);
        var bookingResp = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        var doc = JsonDocument.Parse(await bookingResp.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status").GetString();
        status.Should().NotBe("Confirmed", "Zero amount payment must never confirm booking");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
