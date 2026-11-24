using System.Net;
using System.Text.Json;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F05")]
public class F05_StrictPaymentConfirmationTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F05_StrictPaymentConfirmationTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_01_FullMatchPaymentConfirmation_ValidatesHoldAndPrice()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = (await cardResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // 1. Create booking with valid dates
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 500, stayDuration: 2);
        var payload = new
        {
            roomId = 101,
            guestName = "Strict Payment Guest",
            guestEmail = "strict@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var response = await _gatewayClient.CreateBookingAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("PendingPayment");
        var bookingId = json.GetProperty("id").GetInt32();
        var totalAmount = json.GetProperty("totalAmount").GetDecimal();

        // 2. Pay for the booking matching exact price and currency
        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token, "pay-f05-01-" + Guid.NewGuid());
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // 3. Confirm booking transitions to Confirmed via payment confirmation
        var confirmed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var bRes = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
                if (!bRes.IsSuccessStatusCode) return false;
                var doc = JsonDocument.Parse(await bRes.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Confirmed";
            },
            c => c,
            timeout: TimeSpan.FromSeconds(15)
        );

        confirmed.Should().BeTrue("Booking must transition from PendingPayment to Confirmed after payment");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_02_AmountMismatch_TriggersRefundRequestedEvent()
    {
        var eventId = Guid.NewGuid();
        var mismatchEvent = new PaymentCompletedV1(
            PaymentId: 78,
            BookingId: 102,
            HotelId: 1,
            Amount: 150.00m,
            Currency: "USD"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: mismatchEvent,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        var compensation = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              (msg.Text.Contains("\"paymentId\":78") || msg.Text.Contains("78")),
            timeout: TimeSpan.FromSeconds(8)
        );

        compensation.Should().NotBeNull("Amount mismatch must trigger a PaymentRefundRequestedV1 compensation event");
        compensation!.Text.Should().Contain("78");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_03_CurrencyMismatch_TriggersRefundRequestedEvent()
    {
        var eventId = Guid.NewGuid();
        var currencyMismatch = new PaymentCompletedV1(
            PaymentId: 79,
            BookingId: 103,
            HotelId: 1,
            Amount: 300.00m,
            Currency: "EUR"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: currencyMismatch,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        var compensation = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              (msg.Text.Contains("\"paymentId\":79") || msg.Text.Contains("79")),
            timeout: TimeSpan.FromSeconds(8)
        );

        compensation.Should().NotBeNull("Currency mismatch must trigger a PaymentRefundRequestedV1 compensation event");
        compensation!.Text.Should().Contain("79");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_04_NonExistentBooking_PaymentTriggersRefund()
    {
        var eventId = Guid.NewGuid();
        var paymentForNonExistent = new PaymentCompletedV1(
            PaymentId: 80,
            BookingId: 999999,
            HotelId: 1,
            Amount: 300.00m,
            Currency: "USD"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: paymentForNonExistent,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        var compensation = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              (msg.Text.Contains("\"paymentId\":80") || msg.Text.Contains("80")),
            timeout: TimeSpan.FromSeconds(8)
        );

        compensation.Should().NotBeNull("Payment targeting non-existent or invalid booking must emit a refund request");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_05_CancelledBooking_PaymentTriggersRefund()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        // 1. Create and immediately cancel booking to release night locks
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(20);
        var createResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Cancelled Payment Test",
            guestEmail = "cancelled-pay@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookingId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, token);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Publish PaymentCompleted for cancelled booking
        var eventId = Guid.NewGuid();
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: new PaymentCompletedV1(81, bookingId, 1, 300.00m, "USD"),
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        var compensation = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              (msg.Text.Contains($"\"bookingId\":{bookingId}") || msg.Text.Contains(bookingId.ToString())),
            timeout: TimeSpan.FromSeconds(8)
        );

        compensation.Should().NotBeNull("Payment for a cancelled booking with released locks must emit a refund request");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F05")]
    public async Task TC_F05_06_DuplicatePaymentCompleted_IdempotencyContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: 510, stayDuration: 2);
        var createResp = await _gatewayClient.CreateBookingAsync(new
        {
            roomId = 101,
            guestName = "Idempotent Payment",
            guestEmail = "idem-pay@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }, token);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        // 1. Publish PaymentCompletedV1 integration event
        var eventId = Guid.NewGuid();
        var paymentEvt = new PaymentCompletedV1(
            PaymentId: 7890,
            BookingId: bookingId,
            HotelId: 1,
            Amount: totalAmount,
            Currency: "USD"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: paymentEvt,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        // Await confirmation
        var confirmed = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var bRes = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
                if (!bRes.IsSuccessStatusCode) return false;
                var doc = JsonDocument.Parse(await bRes.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("status").GetString() == "Confirmed";
            },
            c => c,
            timeout: TimeSpan.FromSeconds(15)
        );
        confirmed.Should().BeTrue("Booking must transition to Confirmed on first PaymentCompleted event");

        // 2. Publish the exact same PaymentCompletedV1 event again
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: paymentEvt,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        await Task.Delay(1000);

        // Status must remain Confirmed (idempotent, no regression, no refund)
        var finalRes = await _gatewayClient.GetBookingByIdAsync(bookingId, token);
        finalRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var finalDoc = JsonDocument.Parse(await finalRes.Content.ReadAsStringAsync());
        finalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Duplicate PaymentCompleted event must be handled idempotently");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
