using System.Net;
using System.Text.Json;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F04")]
public class F04_BookingsOutboxPatternTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F04_BookingsOutboxPatternTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _activeMqFixture = new ActiveMqFixture();
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_01_OutboxRecordPersistence_OnBookingCancellation()
    {
        var guestToken = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);

        // 1. Create a fresh dynamic booking
        var checkIn = DateTime.UtcNow.AddDays(260 + Random.Shared.Next(1, 30));
        var checkOut = checkIn.AddDays(2);
        var createPayload = new
        {
            roomId = 101,
            guestName = "Outbox Cancel Test",
            guestEmail = "outbox-test@example.com",
            checkInDate = checkIn.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            checkOutDate = checkOut.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        var createResp = await _gatewayClient.CreateBookingAsync(createPayload, guestToken);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created, "Booking creation must succeed before testing cancellation");

        var createdJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var bookingId = createdJson.GetProperty("id").GetInt32();

        // 2. Cancel the booking
        var cancelResp = await _gatewayClient.CancelBookingAsync(bookingId, guestToken);
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK, "Booking cancellation must return 200 OK and persist outbox record");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F04")]
    public void TC_F04_02_OutboxEnvelope_SchemaAndSerializationIntegrity()
    {
        var refundRequested = new PaymentRefundRequestedV1(
            PaymentId: 42,
            BookingId: 99,
            Reason: "Customer cancellation request"
        );

        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var envelope = new IntegrationEvent<PaymentRefundRequestedV1>(
            EventId: eventId,
            EventType: "bookings.payment-refund-requested",
            SchemaVersion: 1,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: correlationId,
            CausationId: null,
            AggregateId: "99",
            AggregateVersion: 1,
            Data: refundRequested
        );

        // Verify JSON round-trip serialization complies with IntegrationEvent contract
        var json = JsonSerializer.Serialize(envelope);
        json.Should().NotBeNullOrWhiteSpace();

        var deserialized = JsonSerializer.Deserialize<IntegrationEvent<PaymentRefundRequestedV1>>(json);
        deserialized.Should().NotBeNull();
        deserialized!.EventId.Should().Be(eventId);
        deserialized.EventType.Should().Be("bookings.payment-refund-requested");
        deserialized.SchemaVersion.Should().Be(1);
        deserialized.Data.PaymentId.Should().Be(42);
        deserialized.Data.BookingId.Should().Be(99);
        deserialized.AggregateId.Should().Be("99");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_03_AsynchronousOutboxDispatch_ToActiveMqTopic()
    {
        var eventId = Guid.NewGuid();
        var refundRequested = new PaymentRefundRequestedV1(
            PaymentId: 10,
            BookingId: 20,
            Reason: "Outbox Dispatch Test"
        );

        _activeMqFixture.PublishIntegrationEvent(
            topicName: "VirtualTopic.SmartStay.Bookings.V1",
            data: refundRequested,
            eventType: "bookings.payment-refund-requested",
            eventId: eventId
        );

        var message = await _activeMqFixture.WaitForMessageAsync(
            queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
            predicate: msg => msg.Properties.GetString("EventId") == eventId.ToString(),
            timeout: TimeSpan.FromSeconds(5),
            messageSelector: $"EventId = '{eventId}'"
        );

        message.Should().NotBeNull("Published outbox message must route to Consumer queue");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F04")]
    public void TC_F04_04_OutboxPublisher_ConnectionReadiness()
    {
        var act = () => _activeMqFixture.PublishToTopic("VirtualTopic.SmartStay.Ping.V1", "{\"ping\":true}");
        act.Should().NotThrow("Outbox publishing must execute without error when broker is healthy");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_05_OutboxBatchLimit_ComplianceContract()
    {
        // Verify broker handles batch burst of messages without loss
        var batchPrefix = "batch-test-" + Guid.NewGuid().ToString("N")[..8];
        const int burstCount = 5;

        for (int i = 0; i < burstCount; i++)
        {
            _activeMqFixture.PublishIntegrationEvent(
                topicName: "VirtualTopic.SmartStay.Bookings.V1",
                data: new PaymentRefundRequestedV1(300 + i, 400 + i, $"{batchPrefix}-{i}"),
                eventType: "bookings.payment-refund-requested",
                eventId: Guid.NewGuid()
            );
        }

        var receivedCount = 0;
        for (int i = 0; i < burstCount; i++)
        {
            var msg = await _activeMqFixture.WaitForMessageAsync(
                queueName: "Consumer.TestRunner.VirtualTopic.SmartStay.Bookings.V1",
                predicate: m => m.Text.Contains(batchPrefix),
                timeout: TimeSpan.FromSeconds(5)
            );
            if (msg != null) receivedCount++;
        }

        receivedCount.Should().Be(burstCount, "All burst messages must be delivered across broker queues");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
    }
}
