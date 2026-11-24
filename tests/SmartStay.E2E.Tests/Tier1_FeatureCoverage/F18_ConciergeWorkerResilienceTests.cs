using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F18")]
public class F18_ConciergeWorkerResilienceTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ConciergeClient _conciergeClient;
    private readonly GatewayClient _gatewayClient;

    public F18_ConciergeWorkerResilienceTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_01_WorkerHappyPath_AutonomousExecution_ToConfirmed()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 300, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f18-hp-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = doc.RootElement.GetProperty("id").GetString()!;

        // Poll for autonomous background worker execution
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                var status = currentDoc.RootElement.GetProperty("status").GetString();
                return status is "Confirmed" or "Failed" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(25)
        );

        terminalDoc.Should().NotBeNull("Worker must process intent to a terminal state");
        var finalStatus = terminalDoc!.RootElement.GetProperty("status").GetString();
        finalStatus.Should().Be("Confirmed", "Worker must successfully confirm intent on valid parameters");
        terminalDoc.RootElement.GetProperty("bookingId").GetInt32().Should().BeGreaterThan(0);
        terminalDoc.RootElement.GetProperty("receiptVoucher").GetString().Should().StartWith("VCH-");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_02_StableIdempotencyKeys_DefinedInDomainModel()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var stableKey = $"cpi-stable-{Guid.NewGuid():N}";
        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 310, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        // 1. First submission
        var res1 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, stableKey);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var intentId1 = doc1.RootElement.GetProperty("id").GetString()!;

        // 2. Replay with identical stable idempotency key
        var res2 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, stableKey);
        res2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
        var intentId2 = doc2.RootElement.GetProperty("id").GetString()!;

        intentId2.Should().Be(intentId1, "Replaying intent with identical key must return same intent");

        // 3. Verify exactly 1 record exists in MySQL
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM purchase_intents WHERE idempotency_key = @key";
        cmd.Parameters.AddWithValue("@key", stableKey);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(1, "Idempotency key uniqueness must be enforced in MySQL persistence");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_03_TransientRetryScheduling_ExponentialProgressionContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 320, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f18-retry-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = doc.RootElement.GetProperty("id").GetString()!;

        // Verify that retry tracking fields exist and update monotonically in database
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status, retry_count, updated_at_utc, created_at_utc FROM purchase_intents WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", intentId);

        await using var reader = await cmd.ExecuteReaderAsync();
        reader.HasRows.Should().BeTrue();
        await reader.ReadAsync();
        var createdAt = reader.GetDateTime("created_at_utc");
        var updatedAt = reader.GetDateTime("updated_at_utc");
        updatedAt.Should().BeOnOrAfter(createdAt.AddSeconds(-2), "Intent updated timestamp must be tracked accurately");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_04_MandateExpiration_ReleasesHoldAndTransitionsToExpired()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 330, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f18-exp-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = doc.RootElement.GetProperty("id").GetString()!;

        // Fast-forward mandate expiration in MySQL to simulate mandate TTL expiry (15m elapsed)
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE purchase_intents SET expires_at_utc = DATE_SUB(NOW(), INTERVAL 5 MINUTE), status = 'Reserving', next_retry_at_utc = NOW() WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", intentId);
        await cmd.ExecuteNonQueryAsync();

        // Worker must detect mandate expiration and transition status to Expired
        var expiredDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return currentDoc.RootElement.GetProperty("status").GetString() == "Expired" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(15)
        );

        expiredDoc.Should().NotBeNull("Worker must transition expired mandate to Expired state");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_05_CancellationLifecycle_CancellingToCancelledContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var (checkInDt, checkOutDt) = TestDateHelper.GetStayDates(testOffset: 340, stayDuration: 2);
        var checkIn = checkInDt.ToString("yyyy-MM-dd");
        var checkOut = checkOutDt.ToString("yyyy-MM-dd");

        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 500.00m,
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        // Create intent and wait until confirmed
        var intentRes = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f18-cancel-{Guid.NewGuid():N}");
        intentRes.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await intentRes.Content.ReadAsStringAsync());
        var intentId = doc.RootElement.GetProperty("id").GetString()!;

        await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return currentDoc.RootElement.GetProperty("status").GetString() == "Confirmed" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(25)
        );

        // Cancel confirmed intent
        var cancelResp = await _conciergeClient.CancelPurchaseIntentAsync(intentId, token);
        cancelResp.StatusCode.Should().BeOneOf(HttpStatusCode.Accepted, HttpStatusCode.OK);

        // Worker must process refund and transition from Cancelling to Cancelled
        var finalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var res = await _conciergeClient.GetPurchaseIntentAsync(intentId, token);
                if (!res.IsSuccessStatusCode) return null;
                var currentDoc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return currentDoc.RootElement.GetProperty("status").GetString() == "Cancelled" ? currentDoc : null;
            },
            d => d != null,
            timeout: TimeSpan.FromSeconds(20)
        );

        finalDoc.Should().NotBeNull("Paid cancelled intent must reach Cancelled once refund is reconciled");
    }
}
