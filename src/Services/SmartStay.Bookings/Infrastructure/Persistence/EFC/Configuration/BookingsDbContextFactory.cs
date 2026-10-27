using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;

public class BookingsDbContextFactory : IDesignTimeDbContextFactory<BookingsDbContext>
{
    public BookingsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BookingsConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_bookings;";

        var optionsBuilder = new DbContextOptionsBuilder<BookingsDbContext>();
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        optionsBuilder.UseMySql(connectionString, serverVersion);

        return new BookingsDbContext(optionsBuilder.Options);
    }
}
