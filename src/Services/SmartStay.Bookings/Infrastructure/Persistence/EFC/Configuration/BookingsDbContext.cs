using Microsoft.EntityFrameworkCore;
using SmartStay.Bookings.Domain.Model.Aggregates;
using SmartStay.Bookings.Domain.Model.Entities;
using SmartStay.Bookings.Infrastructure.Persistence.Extensions;

namespace SmartStay.Bookings.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
/// DbContext exclusivo para el bounded context de Bookings.
/// Gestiona la persistencia de reservas y los bloqueos físicos de noches (room_night_locks).
/// </summary>
public sealed class BookingsDbContext(DbContextOptions<BookingsDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<RoomNightLock> RoomNightLocks => Set<RoomNightLock>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Booking Entity
        builder.Entity<Booking>().ToTable("bookings");
        builder.Entity<Booking>().HasKey(b => b.Id);
        builder.Entity<Booking>().Property(b => b.Id).ValueGeneratedOnAdd();
        builder.Entity<Booking>().Property(b => b.RoomId).IsRequired();
        builder.Entity<Booking>().Property(b => b.HotelId).IsRequired();
        builder.Entity<Booking>().Property(b => b.GuestName).IsRequired().HasMaxLength(100);
        builder.Entity<Booking>().Property(b => b.GuestEmail).IsRequired().HasMaxLength(200);
        builder.Entity<Booking>().Property(b => b.GuestUserId).HasDefaultValue(0);
        builder.Entity<Booking>().HasIndex(b => b.GuestUserId, "ix_bookings_guest_user_id");
        builder.Entity<Booking>().Property(b => b.CheckInDate).IsRequired();
        builder.Entity<Booking>().Property(b => b.CheckOutDate).IsRequired();
        builder.Entity<Booking>().Property(b => b.NightlyPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<Booking>().Property(b => b.TotalAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<Booking>().Property(b => b.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("USD");
        builder.Entity<Booking>().Property(b => b.Status).HasConversion<int>().IsRequired();
        builder.Entity<Booking>().Property(b => b.ExpiresAtUtc);
        builder.Entity<Booking>().Property(b => b.CreatedAtUtc).IsRequired();

        // RoomNightLock Entity (Exclusión física de concurrencia)
        builder.Entity<RoomNightLock>().ToTable("room_night_locks");
        builder.Entity<RoomNightLock>().HasKey(l => new { l.RoomId, l.StayDate });
        builder.Entity<RoomNightLock>().Property(l => l.StayDate).HasColumnType("date").IsRequired();

        builder.Entity<RoomNightLock>()
            .HasOne(l => l.Booking)
            .WithMany(b => b.NightLocks)
            .HasForeignKey(l => l.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RoomNightLock>()
            .HasIndex(l => l.BookingId, "ix_room_night_booking");

        // InboxMessage Entity (Consumo idempotente de eventos)
        builder.Entity<InboxMessage>().ToTable("inbox_messages");
        builder.Entity<InboxMessage>().HasKey(m => new { m.ConsumerName, m.EventId });
        builder.Entity<InboxMessage>().Property(m => m.ConsumerName).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.EventType).IsRequired().HasMaxLength(150);
        builder.Entity<InboxMessage>().Property(m => m.Status).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.LastError).HasMaxLength(1000);

        builder.UseSnakeCaseNamingConvention();
    }
}
