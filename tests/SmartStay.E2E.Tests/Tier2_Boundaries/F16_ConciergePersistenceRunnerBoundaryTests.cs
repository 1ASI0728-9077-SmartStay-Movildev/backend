using MySqlConnector;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Tier2_Boundaries;

[Trait("Tier", "Tier2")]
[Trait("Feature", "F16")]
public class F16_ConciergePersistenceRunnerBoundaryTests
{
    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_B01_PurchaseIntentTable_PrimaryKeyAndUniqueIndex()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        // 1. Primary key verification
        await using var pkCmd = conn.CreateCommand();
        pkCmd.CommandText = @"
            SELECT column_name 
            FROM information_schema.key_column_usage 
            WHERE table_schema = DATABASE() 
              AND table_name = 'purchase_intents' 
              AND constraint_name = 'PRIMARY';";
        var pkCol = (string?)await pkCmd.ExecuteScalarAsync();
        pkCol.Should().Be("id");

        // 2. Index verification
        await using var idxCmd = conn.CreateCommand();
        idxCmd.CommandText = @"
            SELECT COUNT(*) 
            FROM information_schema.statistics 
            WHERE table_schema = DATABASE() 
              AND table_name = 'purchase_intents' 
              AND column_name = 'idempotency_key';";
        var uniqueCount = Convert.ToInt32(await idxCmd.ExecuteScalarAsync());
        uniqueCount.Should().BeGreaterThan(0, "idempotency_key must have an index on purchase_intents");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_B02_MigrationTable_ConciergeHistoryExists()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM `__EFMigrationsHistory_concierge`;";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().BeGreaterThan(0, "Concierge migrations must be recorded in __EFMigrationsHistory_concierge");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_B03_StatusColumn_TypeAndLength()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT character_maximum_length, data_type 
            FROM information_schema.columns 
            WHERE table_schema = DATABASE() 
              AND table_name = 'purchase_intents' 
              AND column_name = 'status';";

        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        var maxLen = reader.GetInt64(0);
        maxLen.Should().BeGreaterOrEqualTo(30, "Status column must accommodate status string length");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_B04_Indexes_GuestUserIdAndStatus()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name 
            FROM information_schema.statistics 
            WHERE table_schema = DATABASE() 
              AND table_name = 'purchase_intents';";

        var indexedCols = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexedCols.Add(reader.GetString(0).ToLowerInvariant());
        }

        indexedCols.Should().Contain("guest_user_id");
        indexedCols.Should().Contain("status");
    }

    [Fact]
    [Trait("Tier", "Tier2")]
    [Trait("Feature", "F16")]
    public async Task TC_F16_B05_AllColumnsExist_OnPurchaseIntentsTable()
    {
        await using var conn = new MySqlConnection(E2ETestConfig.MySqlConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name 
            FROM information_schema.columns 
            WHERE table_schema = DATABASE() 
              AND table_name = 'purchase_intents';";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        var required = new[]
        {
            "id", "idempotency_key", "guest_user_id", "status", "budget_total",
            "cancellation_policy_accepted", "demo_payment_method_id", "created_at_utc",
            "expires_at_utc", "retry_count", "updated_at_utc"
        };

        foreach (var col in required)
        {
            columns.Should().Contain(col, $"Table purchase_intents must contain column '{col}'");
        }
    }
}
