using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;

public class ProfilesDbContextFactory : IDesignTimeDbContextFactory<ProfilesDbContext>
{
    public ProfilesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ProfilesConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings__ProfilesConnection.");

        var optionsBuilder = new DbContextOptionsBuilder<ProfilesDbContext>();
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        optionsBuilder.UseMySql(connectionString, serverVersion,
            mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_profiles"));

        return new ProfilesDbContext(optionsBuilder.Options);
    }
}
