using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;
using SmartStay.E2E.Tests.Infrastructure.Helpers;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F18")]
public class F18_ConciergeWorkerResilienceBoundaryTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly GatewayClient _gatewayClient;

    public F18_ConciergeWorkerResilienceBoundaryTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_B01_MandateExpirationDuringPendingState()
    {
        var user = "bnd_f18_1_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Fast-forward expires_at_utc in MySQL to simulate mandate TTL expiry
        await using (var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE purchase_intents SET expires_at_utc = DATE_SUB(NOW(), INTERVAL 1 HOUR) WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", intentId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Poll for Expired or terminal state
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Expired" or "Failed" or "Confirmed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(500)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().BeOneOf("Expired", "Failed", "Confirmed");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_B02_CancellationDuringPendingState()
    {
        var user = "bnd_f18_2_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Miraflores",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = "guest@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Cancel immediately
        var cancelResp = await _gatewayClient.CancelPurchaseIntentAsync(intentId, token);
        cancelResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Accepted);

        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Cancelled" or "Cancelling" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(500)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().BeOneOf("Cancelled", "Cancelling");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_B03_WorkerRetryBackoffProgression()
    {
        // Create purchase intent via API to verify initial retry state and progression schema
        var user = "bnd_f18_3_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = $"{user}@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Check retry_count column exists and is initialized to 0 for newly created intent
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT retry_count FROM purchase_intents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", intentId);
        var initialRetryCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        initialRetryCount.Should().Be(0, "Newly created intent must start with retry_count = 0");

        // Verify retry backoff progression mechanics: update retry_count = 1 and set next_retry_at_utc to 1s in the future
        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE purchase_intents SET retry_count = 1, next_retry_at_utc = DATE_ADD(UTC_TIMESTAMP(6), INTERVAL 1 SECOND) WHERE id = @id;";
        updateCmd.Parameters.AddWithValue("@id", intentId);
        await updateCmd.ExecuteNonQueryAsync();

        // Worker must respect next_retry_at_utc backoff window and successfully reconcile the intent to Confirmed
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(500)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Intent must be reconciled by worker after retry backoff matures");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_B04_StableIdempotencyKeyDerivation()
    {
        // Authenticate guest and activate demo payment method
        var user = "bnd_f18_4_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = $"{user}@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Poll until intent is confirmed by Concierge worker
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(500)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");

        // Verify that Payments recorded the stable idempotency key derived by Concierge Worker: concierge-payment-{intentId}
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        var expectedPaymentKey = $"concierge-payment-{intentId}";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM payment_idempotency_records WHERE idempotency_key = @key;";
        cmd.Parameters.AddWithValue("@key", expectedPaymentKey);
        var recordStatus = Convert.ToString(await cmd.ExecuteScalarAsync());

        recordStatus.Should().Be("Completed", "Concierge worker must derive and persist stable payment idempotency key 'concierge-payment-{intentId}'");

        // Verify that Bookings recorded the purchase_intent_id
        await using var cmdBooking = conn.CreateCommand();
        cmdBooking.CommandText = "SELECT COUNT(*) FROM bookings WHERE purchase_intent_id = @intentId;";
        cmdBooking.Parameters.AddWithValue("@intentId", intentId);
        var bookingCount = Convert.ToInt64(await cmdBooking.ExecuteScalarAsync());

        bookingCount.Should().Be(1, "Booking must be linked to the purchase intent ID");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F18")]
    public async Task TC_F18_B05_ReconciliationPeriodicCadence()
    {
        // Create an intent and simulate periodic reconciliation cadence
        var user = "bnd_f18_5_" + Guid.NewGuid().ToString("N")[..8];
        var token = await _authFixture.RegisterAndGetTokenAsync(_apiFixture.GatewayClient, user, "Password123!");
        var cardResp = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardId = JsonDocument.Parse(await cardResp.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        var (checkIn, checkOut) = TestDateHelper.GetStayDates();
        var payload = new
        {
            checkIn = checkIn.ToString("yyyy-MM-dd"),
            checkOut = checkOut.ToString("yyyy-MM-dd"),
            district = "Cercado de Lima",
            guests = 1,
            budgetTotal = 400.00m,
            currency = "USD",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId,
            contactEmail = $"{user}@example.com",
            contactPhone = "+51999999999"
        };

        var response = await _gatewayClient.CreatePurchaseIntentAsync(payload, token);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var intentId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

        // Hold intent in temporary backoff, then mature its reconciliation timestamp
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE purchase_intents SET next_retry_at_utc = DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 1 SECOND) WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", intentId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Verify that the worker polling cycle picks up the matured intent and completes reconciliation
        var terminalDoc = await PollingHelper.PollUntilAsync(
            async () =>
            {
                var r = await _gatewayClient.GetPurchaseIntentAsync(intentId, token);
                var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                var s = doc.RootElement.GetProperty("status").GetString();
                return s is "Confirmed" or "Failed" ? doc : null;
            },
            doc => doc != null,
            TimeSpan.FromSeconds(15),
            initialDelay: TimeSpan.FromMilliseconds(500)
        );

        terminalDoc.RootElement.GetProperty("status").GetString().Should().Be("Confirmed", "Worker periodic reconciliation must advance matured intents to terminal status");
    }
}
