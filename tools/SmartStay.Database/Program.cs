using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;
using SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

var mode = args.FirstOrDefault() ?? "check";
if (mode is not ("check" or "migrate" or "seed"))
    throw new ArgumentException("Use check, migrate or seed.");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Configure MYSQL_CONNECTION_STRING in .env.");

try
{
    await using var connection = new MySqlConnection(connectionString);
    await connection.OpenAsync();
    Console.WriteLine($"Connected to MySQL {connection.ServerVersion}.");
    await using (var tls = new MySqlCommand("SHOW SESSION STATUS LIKE 'Ssl_cipher'", connection))
    await using (var reader = await tls.ExecuteReaderAsync())
    {
        if (!await reader.ReadAsync() || string.IsNullOrEmpty(reader.GetString(1)))
            throw new InvalidOperationException("The remote database requires a TLS connection.");
        Console.WriteLine("TLS enabled.");
    }

    if (mode != "check")
    {
        await using var migrationLock = new MySqlCommand("SELECT GET_LOCK('smartstay_schema_update', 0)", connection);
        if (Convert.ToInt32(await migrationLock.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Another database initialization is running. Try again after it finishes.");
    }

    var tables = new HashSet<string>(StringComparer.Ordinal);
    await using (var query = new MySqlCommand(
        "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()", connection))
    await using (var reader = await query.ExecuteReaderAsync())
        while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
    Console.WriteLine($"Existing tables: {tables.Count}.");
    if (mode == "check") return;

    // Only these factories configure the histories, for both EF tools and this runner.
    var contexts = new (string Name, DbContext Context)[]
    {
        ("iam", new IamDbContextFactory().CreateDbContext([])),
        ("accommodations", new AccommodationsDbContextFactory().CreateDbContext([])),
        ("bookings", new BookingsDbContextFactory().CreateDbContext([])),
        ("payments", new PaymentsDbContextFactory().CreateDbContext([])),
        ("profiles", new ProfilesDbContextFactory().CreateDbContext([])),
        ("analytics", new AnalyticsDbContextFactory().CreateDbContext([]))
    };
    try
    {
        var allowed = contexts.SelectMany(c => c.Context.Model.GetEntityTypes())
            .Select(e => e.GetTableName()!).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, _) in contexts) allowed.Add("__EFMigrationsHistory_" + name);
        // A failed run may leave the old inbox name before its rename migration.
        allowed.Add("inbox_messages");
        if (tables.Except(allowed).Any() ||
            (tables.Count > 0 && !contexts.Any(c => tables.Contains("__EFMigrationsHistory_" + c.Name))))
            throw new InvalidOperationException("Unexpected existing schema. Initialization stopped; no tables were removed.");

        foreach (var (name, context) in contexts)
        {
            var ownedTables = context.Model.GetEntityTypes().Select(e => e.GetTableName()!);
            if (ownedTables.Any(tables.Contains) && !tables.Contains("__EFMigrationsHistory_" + name))
                throw new InvalidOperationException($"Existing {name} tables have no migration history. Initialization stopped.");
        }

        // Hold one connection for inspection and at most one for migrations.
        foreach (var (name, context) in contexts)
        {
            if (mode == "migrate")
            {
                Console.WriteLine($"Applying {name} migrations...");
                await context.Database.MigrateAsync();
            }
            else if ((await context.Database.GetPendingMigrationsAsync()).Any())
                throw new InvalidOperationException("Run migrations before seeding demo users.");
        }

        var expectedTables = contexts.SelectMany(c => c.Context.Model.GetEntityTypes())
            .Select(e => e.GetTableName()!).ToHashSet(StringComparer.Ordinal);
        foreach (var (name, _) in contexts) expectedTables.Add("__EFMigrationsHistory_" + name);
        var finalTables = new HashSet<string>(StringComparer.Ordinal);
        await using (var query = new MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()", connection))
        await using (var reader = await query.ExecuteReaderAsync())
            while (await reader.ReadAsync()) finalTables.Add(reader.GetString(0));
        if (!expectedTables.IsSubsetOf(finalTables) || finalTables.Contains("inbox_messages"))
            throw new InvalidOperationException("The final schema is incomplete or contains an unassigned inbox table.");
        Console.WriteLine("Verified service tables, six migration histories and three separate inbox tables.");

        if (mode == "seed")
        {
            // Insert missing demo users only; preserve passwords and existing business data.
            const string sql = """
                INSERT INTO users
                  (Id, Username, PasswordHash, Role, Status, hotel_id, chain_id, token_version, created_at, updated_at)
                VALUES (@id, @username, @hash, @role, 'Active', @hotel, @chain, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP())
                ON DUPLICATE KEY UPDATE id = id;
                """;
            var hash = BCrypt.Net.BCrypt.HashPassword("Password123!");
            var users = new (int Id, string Username, string Role, int? Hotel, int? Chain)[]
            {
                (1, "admin", "admin", 1, 1),
                (2, "guest1", "guest", null, null),
                (3, "guest2", "guest", null, null),
                (4, "admin_cusco", "admin", 2, 1),
                (5, "chain_admin", "chain_admin", null, 1)
            };
            foreach (var user in users)
            {
                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@id", user.Id);
                command.Parameters.AddWithValue("@username", user.Username);
                command.Parameters.AddWithValue("@hash", hash);
                command.Parameters.AddWithValue("@role", user.Role);
                command.Parameters.AddWithValue("@hotel", (object?)user.Hotel ?? DBNull.Value);
                command.Parameters.AddWithValue("@chain", (object?)user.Chain ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }
            Console.WriteLine("Demo users inserted if missing. Existing data preserved.");
        }
        Console.WriteLine("Database ready.");
    }
    finally
    {
        foreach (var (_, context) in contexts) await context.DisposeAsync();
    }
}
catch (MySqlException exception)
{
    // Do not print credentials, SQL parameter values or connection strings.
    Console.Error.WriteLine($"Database operation failed (MySQL {exception.Number}). Check host, credentials, TLS, permissions and connection limits.");
    Environment.ExitCode = 1;
}
