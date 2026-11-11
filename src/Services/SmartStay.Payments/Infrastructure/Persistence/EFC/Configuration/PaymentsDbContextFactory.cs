using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

public class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentsConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException("Configure ConnectionStrings__PaymentsConnection.");

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        optionsBuilder.UseMySql(connectionString, serverVersion,
            mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_payments"));

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
