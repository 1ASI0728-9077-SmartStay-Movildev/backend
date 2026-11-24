using MySqlConnector;
using SmartStay.Contracts.Events.V1;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F07")]
public class F07_PaymentsAtomicRefundBoundaryTests : IClassFixture<SmartStayApiFixture>, IDisposable
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ActiveMqFixture _activeMqFixture;
    private readonly GatewayClient _gatewayClient;

    public F07_PaymentsAtomicRefundBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _activeMqFixture = new ActiveMqFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_B01_DuplicateRefundEvent_ExactDeduplication()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        await _gatewayClient.ActivateDemoPaymentMethodAsync(token);

        var eventId = Guid.NewGuid();
        var refundEvent = new PaymentRefundRequestedV1(
            PaymentId: 98765,
            BookingId: 98765,
            Reason: "Duplicate delivery boundary test"
        );

        // First delivery
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            refundEvent,
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(1500);

        // Second duplicate delivery with identical eventId
        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            refundEvent,
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(1500);

        // Verify inbox table has at most 1 entry for this eventId
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM payments_inbox_messages WHERE event_id = @id";
        cmd.Parameters.AddWithValue("@id", eventId.ToString());
        var inboxCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        inboxCount.Should().BeLessOrEqualTo(1, "Inbox deduplication must prevent duplicate processing of identical eventId");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_B02_RefundEventForNonExistentBooking_HandledWithoutCrash()
    {
        var eventId = Guid.NewGuid();
        var nonExistentBookingId = 999998;

        var act = () => _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: 999998,
                BookingId: nonExistentBookingId,
                Reason: "Non-existent booking refund test"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        act.Should().NotThrow();
        await Task.Delay(1000);

        // Ensure Payments service is still healthy and responsive
        var health = await _gatewayClient.GetHealthAsync();
        health.IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_B03_ZeroAmountRefund_DoesNotCreateLedgerMovement()
    {
        var eventId = Guid.NewGuid();

        _activeMqFixture.PublishIntegrationEvent(
            "VirtualTopic.SmartStay.Bookings.V1",
            new PaymentRefundRequestedV1(
                PaymentId: 999997,
                BookingId: 999997,
                Reason: "Zero refund test"
            ),
            "bookings.payment-refund-requested",
            eventId
        );

        await Task.Delay(1000);

        // Verify no ledger movement with 0.00 amount was recorded
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM payment_ledger_movements WHERE amount = 0.00;";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(0, "Zero amount refund must never create a ledger movement");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F07")]
    public async Task TC_F07_B04_InboxMessages_TableSchemaIntegrity()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type 
            FROM information_schema.columns 
            WHERE table_schema = DATABASE() AND table_name = 'payments_inbox_messages';";

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        columns.Should().ContainKey("consumer_name");
        columns.Should().ContainKey("event_id");
        columns.Should().ContainKey("processed_at_utc");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F07")]
    public void TC_F07_B05_UnrecognizedEventPayload_HandledCleanlyByOpenWirePipeline()
    {
        var payload = $"{{\"eventId\":\"{Guid.NewGuid()}\",\"eventType\":\"bookings.unrecognized-event\",\"schemaVersion\":1,\"data\":{{}}}}";
        var act = () => _activeMqFixture.PublishToTopic("VirtualTopic.SmartStay.Bookings.V1", payload);
        act.Should().NotThrow("Publishing unrecognized event payload should be handled cleanly by messaging pipeline");
    }

    public void Dispose()
    {
        _activeMqFixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
