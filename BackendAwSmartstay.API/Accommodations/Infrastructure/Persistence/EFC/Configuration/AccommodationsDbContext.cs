using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration.Extensions;
using BackendAwSmartstay.API.Shared.Infrastructure.Persistence.EFC.Configuration.Extensions;
using EntityFrameworkCore.CreatedUpdatedDate.Extensions;
using Microsoft.EntityFrameworkCore;

namespace BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
/// Contexto de persistencia exclusivo para el bounded context de Accommodations.
/// Contiene únicamente entidades pertenecientes a esta capacidad: Hotels, Rooms, RoomTypes, HotelCategories y AmenitiesCatalog.
/// </summary>
public sealed class AccommodationsDbContext(
    DbContextOptions<AccommodationsDbContext> options) : DbContext(options)
{
    public DbSet<Hotel> Hotels => Set<Hotel>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<HotelCategory> HotelCategories => Set<HotelCategory>();
    public DbSet<Amenity> AmenitiesCatalog => Set<Amenity>();

    protected override void OnConfiguring(DbContextOptionsBuilder builder)
    {
        builder.AddCreatedUpdatedInterceptor();
        base.OnConfiguring(builder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Aplica exclusivamente la configuración del dominio Accommodations
        modelBuilder.ApplyAccommodationsConfiguration();

        // Convención snake_case compatible con MySQL
        modelBuilder.UseSnakeCaseNamingConvention();
    }
}
