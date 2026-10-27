using Microsoft.EntityFrameworkCore;
using SmartStay.IAM.Domain.Model.Aggregates;
using SmartStay.IAM.Domain.Model.Enums;
using SmartStay.IAM.Domain.Model.ValueObjects;

namespace SmartStay.IAM.Infrastructure.Persistence.EFC.Configuration;

public class IamDbContext(DbContextOptions<IamDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<User>().ToTable("users");

        builder.Entity<User>().HasKey(u => u.Id);
        builder.Entity<User>().Property(u => u.Id).IsRequired().ValueGeneratedOnAdd();

        builder.Entity<User>().Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(100)
            .HasConversion(v => v.Value, v => new Username(v));

        builder.Entity<User>().Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);

        builder.Entity<User>().Property(u => u.Role)
            .IsRequired()
            .HasMaxLength(20)
            .HasConversion(v => v.Value, v => new Role(v));

        builder.Entity<User>().Property(u => u.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(UserStatus.Active);

        builder.Entity<User>().HasIndex(u => u.Username, "ix_users_username").IsUnique();
        builder.Entity<User>().HasIndex(u => u.Status, "ix_users_status");

        builder.Entity<User>().Property(u => u.HotelId).HasColumnName("hotel_id").IsRequired(false);
        builder.Entity<User>().Property(u => u.ChainId).HasColumnName("chain_id").IsRequired(false);
        builder.Entity<User>().Property(u => u.TokenVersion).HasColumnName("token_version").IsRequired().HasDefaultValue(0);

        builder.Entity<User>().Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Entity<User>().Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();
    }
}
