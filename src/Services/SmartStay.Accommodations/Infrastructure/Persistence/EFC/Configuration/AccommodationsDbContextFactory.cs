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
            ?? throw new InvalidOperationException("Configure ConnectionStrings__AccommodationsConnection.");

        var optionsBuilder = new DbContextOptionsBuilder<AccommodationsDbContext>();
        var serverVersion = ServerVersion.AutoDetect(connectionString);
        optionsBuilder.UseMySql(connectionString, serverVersion,
            mysql => mysql.MigrationsHistoryTable("__EFMigrationsHistory_accommodations"));

        return new AccommodationsDbContext(optionsBuilder.Options);
    }
}
