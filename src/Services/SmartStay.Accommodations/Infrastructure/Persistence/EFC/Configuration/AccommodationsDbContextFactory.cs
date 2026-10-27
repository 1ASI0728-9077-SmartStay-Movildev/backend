using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
/// Factory en tiempo de diseño para habilitar la generación de migraciones de EF Core sin requerir levantar la API.
/// </summary>
public class AccommodationsDbContextFactory : IDesignTimeDbContextFactory<AccommodationsDbContext>
{
    public AccommodationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__AccommodationsConnection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "server=localhost;port=3306;user=root;password=rootpassword;database=smartstay_accommodations;";

        var optionsBuilder = new DbContextOptionsBuilder<AccommodationsDbContext>();
        var serverVersion = new MySqlServerVersion(new Version(8, 0, 36));
        optionsBuilder.UseMySql(connectionString, serverVersion);

        return new AccommodationsDbContext(optionsBuilder.Options);
    }
}
