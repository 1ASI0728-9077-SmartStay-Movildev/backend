using System.Net;
using System.Text.Json;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F06")]
public class F06_BookingsCancellationRefundOutboxTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F06_BookingsCancellationRefundOutboxTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_01_CancelConfirmedBooking_EnqueuesRefundOutboxAtomically()
    {
        var guestToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(guestToken);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // 1. Create fresh booking
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 550, stayDuration: 2);
        var createPayload = new
        {
            roomId = 101,
            guestName = "Cancel Confirmed Test",
            guestEmail = "cancel-confirmed@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        var createResp = await _gatewayClient.CreateBookingAsync(createPayload, guestToken);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookDoc = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookDoc.GetProperty("id").GetInt32();
        var totalAmount = bookDoc.GetProperty("totalAmount").GetDecimal();

        // 2. Pay for the booking to confirm it
        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, guestToken, "pay-f06-01-" + Guid.NewGuid());
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Await confirmation
        var confirmed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var bRes = await _gatewayClient.GetBookingByIdAsync(bookingId, guestToken);
                if (!bRes.IsSuccessStatusCode) return false;
                var doc = JsonDocument.Parse(await bRes.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Confirmed";
            },
            c => c,
            timeout: TimeSpan.FromSeconds(15)
        );
        confirmed.Should().BeTrue("Booking must be Confirmed before cancellation refund can be emitted");

        // 3. Cancel the Confirmed booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, guestToken);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK, "Cancelling booking must return 200 OK");

        // 4. Assert PaymentRefundRequestedV1 integration event is emitted to Bookings virtual topic
        var refundMsg = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              (msg.Text.Contains($"\"bookingId\":{bookingId}") || msg.Text.Contains(bookingId.ToString())),
            timeout: TimeSpan.FromSeconds(10),
            messageSelector: "EventType = 'bookings.payment-refund-requested'"
        );

        refundMsg.Should().NotBeNull("Cancelling a Confirmed paid booking must enqueue and publish PaymentRefundRequestedV1");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_02_CancelUnpaidBooking_DoesNotEmitRefundContract()
    {
        var guestToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 600 + Random.Shared.Next(10, 5000), stayDuration: 2);
        var createPayload = new
        {
            roomId = 101,
            guestName = "Unpaid Cancel Test",
            guestEmail = "unpaid-cancel@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        var createResp = await _gatewayClient.CreateBookingAsync(createPayload, guestToken);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Cancel unpaid booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, guestToken);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert NO refund event is dispatched for an unpaid booking
        var refundMsg = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Text.Contains($"\"bookingId\":{bookingId}"),
            timeout: TimeSpan.FromSeconds(2)
        );
        refundMsg.Should().BeNull("Cancelling an unpaid booking must never enqueue a refund outbox event");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_03_CrossUserBookingCancellation_Rejection()
    {
        var guest1Token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var guest2Token = await _authFixture.GetGuest2TokenAsync(_apiFixture.GatewayClient);

        // Guest1 creates booking
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 650 + Random.Shared.Next(10, 5000), stayDuration: 2);
        var createPayload = new
        {
            roomId = 101,
            guestName = "Guest1 Booking",
            guestEmail = "guest1-isolated@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        var createResp = await _gatewayClient.CreateBookingAsync(createPayload, guest1Token);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // Guest2 attempts to cancel Guest1's booking -> Must be 403 Forbidden
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, guest2Token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "Cross-user booking cancellation must be rejected with 403 Forbidden");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_04_IdempotentCancellation_OnAlreadyCancelledBooking()
    {
        var guestToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 700 + Random.Shared.Next(10, 5000), stayDuration: 2);
        var createResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Repeat Cancel",
            guestEmail = "repeat-cancel@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, guestToken);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        // First cancellation -> 200 OK
        var resp1 = await _gatewayClient.CancelBookingAsync(bookingId, guestToken);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second cancellation -> 200 OK (idempotent)
        var resp2 = await _gatewayClient.CancelBookingAsync(bookingId, guestToken);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK, "Cancelling an already cancelled booking must be idempotent and return 200 OK");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F06")]
    public async Task TC_F06_05_NonExistentBooking_CancellationRejection()
    {
        var guestToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var response = await _gatewayClient.CancelBookingAsync(999999, guestToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Cancelling a non-existent booking must return 404 Not Found");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
