using System.Net;
using System.Text.Json;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F07")]
public class F07_PaymentsAtomicRefundTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F07_PaymentsAtomicRefundTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    private async Task<(int BookingId, int PaymentId, decimal Amount, string CardId, decimal InitialBalance, string Token)> CreatePaidBookingAsync(int testOffset)
    {
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, $"f07_{Guid.NewGuid():N}"[..18], "Password123!", "Guest");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardJson = await cardResp.Content.ReadFromJsonAsync<JsonElement>();
        var cardId = cardJson.GetProperty("id").GetString()!;
        var initialBalance = cardJson.GetProperty("balance").GetDecimal();

        var (checkIn, checkOut) = TestDateHelper.GetStayDates(testOffset: testOffset + Random.Shared.Next(10, 5000), stayDuration: 2);
        var bookingPayload = new
        {
            roomId = 101,
            guestName = "Atomic Refund Guest",
            guestEmail = $"f07-{Guid.NewGuid():N}@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
        var bookResp = await _gatewayClient.CreateBookingAsync(bookingPayload, token);
        bookResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var bookJson = await bookResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = bookJson.GetProperty("id").GetInt32();
        var totalAmount = bookJson.GetProperty("totalAmount").GetDecimal();

        var payResp = await _gatewayClient.ProcessPaymentAsync(new
        {
            bookingId,
            amount = totalAmount,
            currency = "USD",
            paymentMethod = "DemoCard",
            demoPaymentMethodId = cardId
        }, token, "pay-f07-" + Guid.NewGuid());
        payResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var payJson = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = payJson.GetProperty("id").GetInt32();

        return (bookingId, paymentId, totalAmount, cardId, initialBalance, token);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_01_SingleTransactionAtomicRefund_Execution()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(800);

        // Publish refund request to VirtualTopic.SmartStay.Bookings.V1
        var eventId = Guid.NewGuid();
        var refundReq = new PaymentRefundRequestedV1(
            PaymentId: paymentId,
            BookingId: bookingId,
            Reason: "Guest cancellation"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        var refundCompleted = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "payments.payment-refunded" &&
                              msg.Text.Contains($"\"bookingId\":{bookingId}"),
            timeout: TimeSpan.FromSeconds(10),
            messageSelector: "EventType = 'payments.payment-refunded'"
        );

        refundCompleted.Should().NotBeNull("Payments service must process refund request and publish PaymentRefundedV1 event");
        refundCompleted!.Text.Should().Contain($"\"bookingId\":{bookingId}");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_02_InboxDeduplication_ContractIntegrity()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(810);

        var duplicateEventId = Guid.NewGuid();
        var refundReq = new PaymentRefundRequestedV1(
            PaymentId: paymentId,
            BookingId: bookingId,
            Reason: "Duplicate Inbox Test"
        );

        // Publish first event
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: duplicateEventId
        );

        // Publish second identical event with same EventId
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: duplicateEventId
        );

        // Await refund completion
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(10)
        );

        restored.Should().BeTrue("Refund must restore demo card balance to initialBalance");

        // Verify balance was NOT doubled
        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBalance = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBalance.Should().Be(initialBalance, "Duplicate inbox delivery must not double-credit refund balance");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_03_FallbackPaymentResolution_ByBookingIdContract()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(820);

        var eventId = Guid.NewGuid();
        // PaymentId 0 triggers fallback lookup by BookingId
        var refundReq = new PaymentRefundRequestedV1(
            PaymentId: 0,
            BookingId: bookingId,
            Reason: "Fallback Resolution Test"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        // Await refund completed event on ActiveMQ
        var refundMsg = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "payments.payment-refunded" &&
                              msg.Text.Contains($"\"bookingId\":{bookingId}"),
            timeout: TimeSpan.FromSeconds(10),
            messageSelector: "EventType = 'payments.payment-refunded'"
        );

        refundMsg.Should().NotBeNull("Fallback resolution by BookingId must successfully process refund");

        // Poll card balance restoration
        var restored = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(10)
        );
        restored.Should().BeTrue("Demo card balance must be restored following fallback resolution by bookingId");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_04_GracefulHandling_AlreadyRefundedPaymentContract()
    {
        var (bookingId, paymentId, totalAmount, cardId, initialBalance, token) = await CreatePaidBookingAsync(830);

        // 1. Process first refund
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: new PaymentRefundRequestedV1(paymentId, bookingId, "Initial Refund"),
            eventType: "bookings.payment-refund-requested",
            eventId: Guid.NewGuid()
        );

        await PollingHelper.PollUntilAsync(
            async () =>
            {
                var cardCheck = await _gatewayClient.GetDemoPaymentMethodAsync(token);
                if (!cardCheck.IsSuccessStatusCode) return false;
                var cardDoc = JsonDocument.Parse(await cardCheck.Content.ReadAsStringAsync());
                return cardDoc.RootElement.GetProperty("balance").GetDecimal() == initialBalance;
            },
            r => r,
            timeout: TimeSpan.FromSeconds(10)
        );

        // 2. Publish repeat refund request for already refunded payment
        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: new PaymentRefundRequestedV1(paymentId, bookingId, "Repeat Refund"),
            eventType: "bookings.payment-refund-requested",
            eventId: Guid.NewGuid()
        );

        await Task.Delay(1000);

        // Balance must remain initialBalance and not double-credit
        var finalCard = await _gatewayClient.GetDemoPaymentMethodAsync(token);
        var finalBal = (await finalCard.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("balance").GetDecimal();
        finalBal.Should().Be(initialBalance, "Repeating refund on already refunded payment must not credit balance again");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_05_MissingPaymentCompensation_SafeHandling()
    {
        var eventId = Guid.NewGuid();
        var refundReq = new PaymentRefundRequestedV1(
            PaymentId: 999999,
            BookingId: 999999,
            Reason: "Compensation for non-existent payment"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundReq,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        await Task.Delay(1000);

        // Gateway and Payments service must remain healthy
        var healthResp = await _gatewayClient.GetHealthAsync();
        healthResp.StatusCode.Should().Be(HttpStatusCode.OK, "Payments service must handle unknown payment refund request without crashing");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
