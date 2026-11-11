using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;

public class IamDbContextFactory : IDesignTimeDbContextFactory<IamDbContext>
{
    public IamDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__IamConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings__IamConnection.");

        var optionsBuilder = new DbContextOptionsBuilder<IamDbContext>();
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        optionsBuilder.UseMySql(connectionString, serverVersion,
            mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_iam"));

        return new IamDbContext(optionsBuilder.Options);
    }
}
