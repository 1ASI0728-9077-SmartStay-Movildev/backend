using Microsoft.EntityFrameworkCore;
using SmartStay.Analytics.Domain.Model.Entities;
using SmartStay.Analytics.Infrastructure.Persistence.Extensions;

namespace SmartStay.Analytics.Infrastructure.Persistence.EFC.Configuration;

public class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options) : DbContext(options)
{
    public DbSet<RevenueFact> RevenueFacts => Set<RevenueFact>();
    public DbSet<BookingFact> BookingFacts => Set<BookingFact>();
    public DbSet<CatalogSnapshot> CatalogSnapshots => Set<CatalogSnapshot>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // RevenueFact
        builder.Entity<RevenueFact>().ToTable("analytics_revenue_facts");
        builder.Entity<RevenueFact>().HasKey(f => f.PaymentId);
        builder.Entity<RevenueFact>().Property(f => f.PaymentId).ValueGeneratedNever();
        builder.Entity<RevenueFact>().Property(f => f.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<RevenueFact>().Property(f => f.RefundAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<RevenueFact>().Property(f => f.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("USD");
        builder.Entity<RevenueFact>().Property(f => f.OccurredAtUtc).IsRequired();

        builder.Entity<RevenueFact>().HasIndex(f => f.OccurredAtUtc, "ix_revenue_occurred_at");
        builder.Entity<RevenueFact>().HasIndex(f => f.BookingId, "ix_revenue_booking_id");

        // BookingFact
        builder.Entity<BookingFact>().ToTable("analytics_booking_facts");
        builder.Entity<BookingFact>().HasKey(b => b.BookingId);
        builder.Entity<BookingFact>().Property(b => b.BookingId).ValueGeneratedNever();
        builder.Entity<BookingFact>().Property(b => b.Status).IsRequired().HasMaxLength(50);
        builder.Entity<BookingFact>().Property(b => b.CheckInDate).IsRequired();
        builder.Entity<BookingFact>().Property(b => b.CheckOutDate).IsRequired();
        builder.Entity<BookingFact>().Property(b => b.NightsCount).IsRequired();
        builder.Entity<BookingFact>().Property(b => b.OccurredAtUtc).IsRequired();

        builder.Entity<BookingFact>().HasIndex(b => b.OccurredAtUtc, "ix_bookings_occurred_at");
        builder.Entity<BookingFact>().HasIndex(b => b.Status, "ix_bookings_status");

        // CatalogSnapshot
        builder.Entity<CatalogSnapshot>().ToTable("analytics_catalog_snapshots");
        builder.Entity<CatalogSnapshot>().HasKey(c => c.RoomId);
        builder.Entity<CatalogSnapshot>().Property(c => c.RoomId).ValueGeneratedNever();
        builder.Entity<CatalogSnapshot>().Property(c => c.NightlyPrice).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<CatalogSnapshot>().Property(c => c.UpdatedAtUtc).IsRequired();

        // InboxMessage
        builder.Entity<InboxMessage>().ToTable("inbox_messages");
        builder.Entity<InboxMessage>().HasKey(m => new { m.ConsumerName, m.EventId });
        builder.Entity<InboxMessage>().Property(m => m.ConsumerName).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.EventType).IsRequired().HasMaxLength(150);
        builder.Entity<InboxMessage>().Property(m => m.Status).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.LastError).HasMaxLength(1000);

        builder.UseSnakeCaseNamingConvention();
    }
}
