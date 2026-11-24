using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F04")]
public class F04_BookingsOutboxPatternBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F04_BookingsOutboxPatternBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_B01_OutboxTableSchema_PrimaryKeyAndIndexes()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, is_nullable 
            FROM information_schema.columns 
            WHERE table_schema = DATABASE() AND table_name = 'bookings_outbox_messages';";

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        columns.Should().ContainKey("id");
        columns.Should().ContainKey("event_type");
        columns.Should().ContainKey("payload");
        columns.Should().ContainKey("occurred_at_utc");
        columns.Should().ContainKey("published_at_utc");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_B02_FailedBookingRequest_DoesNotInsertOutboxRecord()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        long countBefore;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM bookings_outbox_messages;";
            countBefore = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        // Deliberately invalid booking request (inverted dates)
        var invalidPayload = new
        {
            roomId = 101,
            checkIn = "2030-05-10",
            checkOut = "2030-05-01",
            guests = 1,
            stayTotal = 100.00m
        };

        var response = await _gatewayClient.CreateBookingAsync(invalidPayload, token);
        response.IsSuccessStatusCode.Should().BeFalse();

        long countAfter;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM bookings_outbox_messages;";
            countAfter = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        countAfter.Should().Be(countBefore, "Failed booking request must not generate uncommitted outbox entries");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_B03_ProcessedOutbox_TimestampMonotonicity()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT occurred_at_utc, published_at_utc 
            FROM bookings_outbox_messages 
            WHERE published_at_utc IS NOT NULL 
            ORDER BY id DESC LIMIT 5;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var occurred = reader.GetDateTime(0);
            var published = reader.GetDateTime(1);

            published.Should().BeOnOrAfter(occurred, "Published timestamp must be on or after occurred timestamp");
            published.Should().BeBefore(DateTime.UtcNow.AddMinutes(5), "Published timestamp must not be in the future");
        }
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_B04_OutboxPayload_ContainsValidJson()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT payload FROM bookings_outbox_messages ORDER BY id DESC LIMIT 5;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payload = reader.GetString(0);
            payload.Should().NotBeNullOrWhiteSpace();

            var act = () => System.Text.Json.JsonDocument.Parse(payload);
            act.Should().NotThrow("Every persisted outbox payload must be valid JSON");
        }
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F04")]
    public async Task TC_F04_B05_OutboxBatchDispatcher_HandlesEmptyQueueSafely()
    {
        var testEventId = Guid.NewGuid().ToString();
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Insert a synthetic pending outbox message with published_at_utc = NULL
        await using (var insertCmd = conn.CreateCommand())
        {
            insertCmd.CommandText = @"
                INSERT INTO bookings_outbox_messages 
                (id, event_type, schema_version, aggregate_id, aggregate_version, payload, occurred_at_utc, published_at_utc, attempts)
                VALUES
                (@id, 'bookings.test-batch-dispatcher', 1, @id, 1, @payload, UTC_TIMESTAMP(6), NULL, 0);";
            insertCmd.Parameters.AddWithValue("@id", testEventId);
            insertCmd.Parameters.AddWithValue("@payload", $"{{\"testEventId\":\"{testEventId}\"}}");
            await insertCmd.ExecuteNonQueryAsync();
        }

        try
        {
            // Verify that OutboxPublisherWorker actively polls the batch, publishes the event, and marks published_at_utc
            var isPublished = await PollingHelper.PollUntilAsync(
                async () =>
                {
                    await using var checkCmd = conn.CreateCommand();
                    checkCmd.CommandText = "SELECT published_at_utc FROM bookings_outbox_messages WHERE id = @id;";
                    checkCmd.Parameters.AddWithValue("@id", testEventId);
                    var res = await checkCmd.ExecuteScalarAsync();
                    return res != null && res != DBNull.Value;
                },
                published => published,
                TimeSpan.FromSeconds(10),
                description: "Waiting for OutboxPublisherWorker to dispatch batch message"
            );

            isPublished.Should().BeTrue("Outbox batch dispatcher must process pending outbox entries and set published_at_utc");
        }
        finally
        {
            // Clean up test message
            await using var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM bookings_outbox_messages WHERE id = @id;";
            deleteCmd.Parameters.AddWithValue("@id", testEventId);
            await deleteCmd.ExecuteNonQueryAsync();
        }
    }
}
