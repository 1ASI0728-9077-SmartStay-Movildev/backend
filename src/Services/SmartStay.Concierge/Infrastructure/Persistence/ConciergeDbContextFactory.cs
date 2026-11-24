using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Concierge.Infrastructure.Persistence;

public class ConciergeDbContextFactory : IDesignTimeDbContextFactory<ConciergeDbContext>
{
    public ConciergeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ConciergeConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("MYSQL_CONNECTION_STRING")
            ?? "Server=localhost;Port=3306;Database=smartstay_legacy;User ID=smartstay_user;Password=smartstay_password;SslMode=None;AllowPublicKeyRetrieval=true;";

        var optionsBuilder = new DbContextOptionsBuilder<ConciergeDbContext>();
        ServerVersion serverVersion;
        try
        {
            serverVersion = ServerVersion.AutoDetect(connectionString);
        }
        catch
        {
            serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        }

        optionsBuilder.UseMySql(connectionString, serverVersion,
            mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_concierge"));

        return new ConciergeDbContext(optionsBuilder.Options);
    }
}
