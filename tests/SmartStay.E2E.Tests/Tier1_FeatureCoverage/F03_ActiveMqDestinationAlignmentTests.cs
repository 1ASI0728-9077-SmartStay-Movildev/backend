using Apache.NMS;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F03")]
public class F03_ActiveMqDestinationAlignmentTests : IDisposable
{
    private readonly ActiveMqFixture _activeMqFixture;

    public F03_ActiveMqDestinationAlignmentTests()
    {
        _activeMqFixture = new ActiveMqFixture();
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F03")]
    public async Task TC_F03_01_VirtualTopicRouting_ToPaymentsQueue()
    {
        var eventId = Guid.NewGuid();
        var refundRequested = new PaymentRefundRequestedV1(
            PaymentId: 50,
            BookingId: 45,
            Reason: "Guest cancellation"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundRequested,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        var receivedMessage = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "bookings.payment-refund-requested" &&
                              msg.Properties.GetString("EventId") == eventId.ToString(),
            timeout: TimeSpan.FromSeconds(5),
            messageSelector: $"EventId = '{eventId}'"
        );

        receivedMessage.Should().NotBeNull("Virtual topic VirtualTopic.SmartStay.Bookings.V1 must route message to Consumer queues");
        receivedMessage!.Text.Should().Contain(eventId.ToString());
        receivedMessage.Text.Should().Contain("\"bookingId\":45");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F03")]
    public async Task TC_F03_02_VirtualTopicRouting_ToBookingsQueue()
    {
        var eventId = Guid.NewGuid();
        var paymentCompleted = new PaymentCompletedV1(
            PaymentId: 77,
            BookingId: 101,
            HotelId: 1,
            Amount: 300.00m,
            Currency: "USD"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: paymentCompleted,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        var receivedMessage = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1",
            predicate: msg => msg.Properties.GetString("EventType") == "payments.payment-completed" &&
                              msg.Properties.GetString("EventId") == eventId.ToString(),
            timeout: TimeSpan.FromSeconds(5),
            messageSelector: $"EventId = '{eventId}'"
        );

        receivedMessage.Should().NotBeNull("Virtual topic VirtualTopic.SmartStay.Payments.V1 must route message to Consumer queue");
        receivedMessage!.Text.Should().Contain(eventId.ToString());
        receivedMessage.Text.Should().Contain("\"amount\":300.00");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F03")]
    public async Task TC_F03_03_DurableQueue_MessageRetentionDuringConsumerDisconnect()
    {
        var eventId = Guid.NewGuid();
        var paymentCompleted = new PaymentCompletedV1(
            PaymentId: 88,
            BookingId: 102,
            HotelId: 1,
            Amount: 450.00m,
            Currency: "USD"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: paymentCompleted,
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        await Task.Delay(500);

        var message = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1",
            predicate: msg => msg.Properties.GetString("EventId") == eventId.ToString(),
            timeout: TimeSpan.FromSeconds(5),
            messageSelector: $"EventId = '{eventId}'"
        );

        message.Should().NotBeNull("Physical queue Consumer.TestRunner must retain messages published to virtual topic");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F03")]
    public async Task TC_F03_04_StandardizedJmsHeaders_AndEnvelopeCorrelation()
    {
        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: new PaymentCompletedV1(1, 1, 1, 100m, "USD"),
            eventType: "payments.payment-completed",
            eventId: eventId,
            correlationId: correlationId
        );

        var message = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1",
            predicate: msg => msg.Properties.GetString("EventId") == eventId.ToString(),
            timeout: TimeSpan.FromSeconds(5),
            messageSelector: $"EventId = '{eventId}'"
        );

        message.Should().NotBeNull("Message must be delivered with all standardized integration headers");
        message!.Properties.GetString("EventType").Should().Be("payments.payment-completed");
        message.Properties.GetString("EventId").Should().Be(eventId.ToString());
        message.Text.Should().Contain(correlationId);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F03")]
    public async Task TC_F03_05_NonAcknowledgedMessage_RedeliveryMode()
    {
        await Task.CompletedTask;
        var queueName = "Consumer.TestRunner.VirtualTopic.SmartStay.Payments.V1";
        var eventId = Guid.NewGuid();

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Payments.V1",
            data: new PaymentCompletedV1(2, 2, 1, 200m, "USD"),
            eventType: "payments.payment-completed",
            eventId: eventId
        );

        // Session 1: Receive but do NOT acknowledge
        using (var session1 = _activeMqFixture.Connection.CreateSession(AcknowledgementMode.ClientAcknowledge))
        {
            var queue1 = session1.GetQueue(queueName);
            using var consumer1 = session1.CreateConsumer(queue1);
            var msg1 = consumer1.Receive(TimeSpan.FromSeconds(3)) as ITextMessage;
            msg1.Should().NotBeNull("First consumer must receive the published message");
            // Closed without acknowledge
        }

        // Session 2: Must redeliver unacknowledged message
        using (var session2 = _activeMqFixture.Connection.CreateSession(AcknowledgementMode.ClientAcknowledge))
        {
            var queue2 = session2.GetQueue(queueName);
            using var consumer2 = session2.CreateConsumer(queue2);
            var msg2 = consumer2.Receive(TimeSpan.FromSeconds(3)) as ITextMessage;
            msg2.Should().NotBeNull("Unacknowledged message must be redelivered to next consumer session");
            msg2!.Acknowledge(); // Clean up
        }
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
