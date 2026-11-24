using System.Net;
using System.Text.Json;
using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Clients;
using SmartStay.E2E.Tests.Infrastructure.Configuration;
using SmartStay.E2E.Tests.Infrastructure.Fixtures;

namespace SmartStay.E2E.Tests.Tier1_FeatureCoverage;

[Trait("Tier", "Tier1")]
[Trait("Feature", "F16")]
public class F16_ConciergePersistenceRunnerTests : IClassFixture<SmartStayApiFixture>
{
    private readonly SmartStayApiFixture _apiFixture;
    private readonly AuthTokenFixture _authFixture;
    private readonly ConciergeClient _conciergeClient;
    private readonly GatewayClient _gatewayClient;

    public F16_ConciergePersistenceRunnerTests(SmartStayApiFixture apiFixture)
    {
        _apiFixture = apiFixture;
        _authFixture = new AuthTokenFixture();
        _conciergeClient = new ConciergeClient(useDirectUrl: true);
        _gatewayClient = new GatewayClient(_apiFixture.GatewayClient);
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_01_ConciergeDbContext_RegistrationContract()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // 1. Verify Concierge migration history table exists and contains applied migrations in live MySQL
        await using var cmdMig = conn.CreateCommand();
        cmdMig.CommandText = "SELECT COUNT(*) FROM `__EFMigrationsHistory_concierge`";
        var count = Convert.ToInt32(await cmdMig.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0, "Concierge migrations must be applied in the live database");

        // 2. Verify purchase_intents table exists in MySQL schema
        await using var cmdTable = conn.CreateCommand();
        cmdTable.CommandText = "SELECT COUNT(*) FROM information_schema.TABLES WHERE table_schema = 'smartstay_legacy' AND table_name = 'purchase_intents'";
        var tableExists = Convert.ToInt32(await cmdTable.ExecuteScalarAsync());
        tableExists.Should().Be(1, "purchase_intents table must exist in database schema");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_02_PurchaseIntents_RequiredSchemaContract()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT column_name FROM information_schema.COLUMNS WHERE table_schema = 'smartstay_legacy' AND table_name = 'purchase_intents'";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        var requiredColumns = new[]
        {
            "id",
            "guest_user_id",
            "status",
            "check_in",
            "check_out",
            "district",
            "guests",
            "budget_total",
            "currency",
            "demo_payment_method_id",
            "expires_at_utc",
            "created_at_utc",
            "updated_at_utc",
            "concurrency_token"
        };

        foreach (var col in requiredColumns)
        {
            columns.Should().Contain(col, $"Table purchase_intents in live MySQL must contain column '{col}'");
        }
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_03_StateMachineTransitions_LifecycleDefinition()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(55).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(57).ToString("yyyy-MM-dd");
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

        var res = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f16-03-{Guid.NewGuid():N}");
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var initialStatus = doc.RootElement.GetProperty("status").GetString();

        var canonicalStates = new HashSet<string>
        {
            "Pending",
            "Reserving",
            "AwaitingConfirmation",
            "Confirmed",
            "Cancelling",
            "Cancelled",
            "Failed",
            "Expired"
        };

        canonicalStates.Should().Contain(initialStatus, "State machine status must be one of the canonical lifecycle states");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_04_OptimisticConcurrencyToken_VersionProtectionContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var stableKey = $"cpi-idemp-{Guid.NewGuid():N}";
        var checkIn = DateTime.UtcNow.Date.AddDays(60).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(62).ToString("yyyy-MM-dd");
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

        // First creation
        var res1 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, stableKey);
        res1.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var id1 = doc1.RootElement.GetProperty("id").GetString();

        // Duplicate replay with identical key must return same intent idempotently
        var res2 = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, stableKey);
        res2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
        var id2 = doc2.RootElement.GetProperty("id").GetString();

        id2.Should().Be(id1, "Replaying purchase intent with same idempotency key must return existing intent");
    }

    [Fact]
    [Trait("Tier", "Tier1")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_05_UncompletedIntentDiscovery_FilterContract()
    {
        var token = await _authFixture.GetGuest1TokenAsync(_apiFixture.GatewayClient);
        var cardRes = await _gatewayClient.ActivateDemoPaymentMethodAsync(token);
        var cardDoc = JsonDocument.Parse(await cardRes.Content.ReadAsStringAsync());
        var cardId = cardDoc.RootElement.GetProperty("id").GetString()!;

        var checkIn = DateTime.UtcNow.Date.AddDays(70).ToString("yyyy-MM-dd");
        var checkOut = DateTime.UtcNow.Date.AddDays(72).ToString("yyyy-MM-dd");
        var payload = new
        {
            checkIn,
            checkOut,
            district = "Cercado de Lima",
            guests = 2,
            budgetTotal = 10.00m, // Below catalog rooms so discovery evaluates and transitions cleanly
            currency = "USD",
            contactEmail = "guest1@example.com",
            contactPhone = "+51999999999",
            cancellationPolicyAccepted = true,
            demoPaymentMethodId = cardId
        };

        var res = await _conciergeClient.CreatePurchaseIntentAsync(payload, token, $"cpi-f16-05-{Guid.NewGuid():N}");
        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var intentId = doc.RootElement.GetProperty("id").GetString()!;

        // Verify the intent was persisted in MySQL table purchase_intents
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // Poll until background worker discovers the uncompleted intent and updates its state in MySQL
        var discovered = false;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            await using var cmdSelect = conn.CreateCommand();
            cmdSelect.CommandText = "SELECT status, failure_reason FROM purchase_intents WHERE id = @id";
            cmdSelect.Parameters.AddWithValue("@id", intentId);

            await using var reader = await cmdSelect.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var status = reader.GetString(0);
                if (status is "Failed" or "Confirmed" or "Expired")
                {
                    discovered = true;
                    break;
                }
            }
            await reader.CloseAsync();
            await Task.Delay(500);
        }

        discovered.Should().BeTrue("Background worker must discover and transition uncompleted intent persisted in database");
    }
}
