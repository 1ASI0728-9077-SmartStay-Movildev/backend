using BackendAwSmartstay.API.Accommodations.Domain.Model.Aggregates;
using BackendAwSmartstay.API.Accommodations.Domain.Model.Entities;
using BackendAwSmartstay.API.Accommodations.Infrastructure.Persistence.EFC.Configuration.Extensions;
using EntityFrameworkCore.CreatedUpdatedDate.Extensions;
using Microsoft.EntityFrameworkCore;
using SmartStay.Accommodations.Infrastructure.Persistence.Extensions;

namespace SmartStay.Accommodations.Infrastructure.Persistence.EFC.Configuration;

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
        modelBuilder.ApplyAccommodationsConfiguration();
        modelBuilder.UseSnakeCaseNamingConvention();
    }
}
