using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

public class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PaymentsConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_payments;";

        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        optionsBuilder.UseMySql(connectionString, serverVersion);

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
