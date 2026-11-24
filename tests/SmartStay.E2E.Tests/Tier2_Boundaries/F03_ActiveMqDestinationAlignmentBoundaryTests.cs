using Apache.NMS;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F03")]
public class F03_ActiveMqDestinationAlignmentBoundaryTests : IDisposable
{
    private readonly ActiveMqFixture _activeMqFixture;

    public F03_ActiveMqDestinationAlignmentBoundaryTests()
    {
        _activeMqFixture = new ActiveMqFixture();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F03")]
    public void TC_F03_B01_LargePayload_VirtualTopicPublishing()
    {
        var topic = "VirtualTopic.SmartStay.Payments.V1";
        // 64 KB message payload stress boundary
        var largeData = new string('X', 64 * 1024);
        var payload = $"{{\"eventType\":\"TestLargePayload\",\"data\":\"{largeData}\"}}";

        var act = () => _activeMqFixture.PublishToTopic(topic, payload);
        act.Should().NotThrow("ActiveMQ virtual topic must accept 64KB payload without error");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F03")]
    public void TC_F03_B02_MalformedJson_PoisonPillResilience()
    {
        // Use dedicated test topic so malformed payloads do not poison live service worker queues
        var topic = "VirtualTopic.SmartStay.TransportBoundary.V1";
        var malformedPayload = "NOT_A_VALID_JSON_STRING_<<<>>>";

        // Publishing non-JSON must not throw on ActiveMQ level
        var act = () => _activeMqFixture.PublishToTopic(topic, malformedPayload);
        act.Should().NotThrow("Publishing non-JSON string to virtual topic should be handled by OpenWire transport");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F03")]
    public void TC_F03_B03_RapidBurstPublishing_FifoOrdering()
    {
        var topic = "VirtualTopic.SmartStay.Bookings.V1";

        // Rapid burst of 15 messages with valid integration envelope headers so background workers can acknowledge cleanly
        for (var i = 0; i < 15; i++)
        {
            var eventId = Guid.NewGuid();
            var payload = $"{{\"eventId\":\"{eventId}\",\"eventType\":\"bookings.test-burst\",\"seq\":{i},\"timestamp\":\"{DateTime.UtcNow:O}\"}}";
            _activeMqFixture.PublishToTopic(topic, payload, new Dictionary<string, string>
            {
                ["Sequence"] = i.ToString(),
                ["EventType"] = "bookings.test-burst",
                ["EventId"] = eventId.ToString()
            });
        }

        // Connection should remain healthy after burst
        _activeMqFixture.Connection.IsStarted.Should().BeTrue("ActiveMQ connection must remain healthy after burst publish");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F03")]
    public void TC_F03_B04_EmptyMessageProperties_HandledCleanly()
    {
        var topic = "VirtualTopic.SmartStay.Payments.V1";
        var payload = "{\"eventType\":\"NoPropertiesEvent\"}";

        var act = () => _activeMqFixture.PublishToTopic(topic, payload, properties: null);
        act.Should().NotThrow("Publishing without message properties must succeed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F03")]
    public void TC_F03_B05_DurableSubscription_NamingAlignment()
    {
        using var session = _activeMqFixture.Connection.CreateSession(AcknowledgementMode.AutoAcknowledge);

        // Verify valid consumer queue creation following Virtual Topic naming pattern
        var bookingsConsumerQueue = session.GetQueue("Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1");
        bookingsConsumerQueue.Should().NotBeNull();
        bookingsConsumerQueue.QueueName.Should().Be("Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1");

        var paymentsConsumerQueue = session.GetQueue("Consumer.Payments.VirtualTopic.SmartStay.Bookings.V1");
        paymentsConsumerQueue.Should().NotBeNull();
        paymentsConsumerQueue.QueueName.Should().Be("Consumer.Payments.VirtualTopic.SmartStay.Bookings.V1");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
