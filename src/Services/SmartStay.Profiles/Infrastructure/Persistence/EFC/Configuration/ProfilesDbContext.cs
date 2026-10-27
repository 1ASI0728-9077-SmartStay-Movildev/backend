using Microsoft.EntityFrameworkCore;
using SmartStay.Profiles.Domain.Model.Aggregates;
using SmartStay.Profiles.Infrastructure.Persistence.Extensions;

namespace SmartStay.Profiles.Infrastructure.Persistence.EFC.Configuration;

public class ProfilesDbContext(DbContextOptions<ProfilesDbContext> options) : DbContext(options)
{
    public DbSet<Profile> Profiles => Set<Profile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Profile>().ToTable("profiles");
        builder.Entity<Profile>().HasKey(p => p.Id);
        builder.Entity<Profile>().Property(p => p.Id).ValueGeneratedOnAdd();
        builder.Entity<Profile>().Property(p => p.UserId);
        builder.Entity<Profile>().Property(p => p.CreatedAtUtc).IsRequired();
        builder.Entity<Profile>().Property(p => p.UpdatedAtUtc);

        builder.Entity<Profile>().HasIndex(p => p.UserId, "ix_profiles_user_id");

        builder.Entity<Profile>().OwnsOne(p => p.Name, n =>
        {
            n.WithOwner().HasForeignKey("Id");
            n.Property(p => p.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            n.Property(p => p.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        });

        builder.Entity<Profile>().OwnsOne(p => p.Email, e =>
        {
            e.WithOwner().HasForeignKey("Id");
            e.Property(a => a.Address).HasColumnName("email_address").HasMaxLength(200).IsRequired();
        });

        builder.Entity<Profile>().OwnsOne(p => p.Address, a =>
        {
            a.WithOwner().HasForeignKey("Id");
            a.Property(s => s.Street).HasColumnName("address_street").HasMaxLength(200);
            a.Property(s => s.Number).HasColumnName("address_number").HasMaxLength(50);
            a.Property(s => s.City).HasColumnName("address_city").HasMaxLength(100);
            a.Property(s => s.PostalCode).HasColumnName("address_postal_code").HasMaxLength(20);
            a.Property(s => s.Country).HasColumnName("address_country").HasMaxLength(100);
        });

        builder.UseSnakeCaseNamingConvention();
    }
}
