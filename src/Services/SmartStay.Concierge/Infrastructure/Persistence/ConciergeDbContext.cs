using Microsoft.EntityFrameworkCore;
using SmartStay.Concierge.Domain;
using SmartStay.Concierge.Infrastructure.Persistence.Extensions;

namespace SmartStay.Concierge.Infrastructure.Persistence;

public class ConciergeDbContext(DbContextOptions<ConciergeDbContext> options) : DbContext(options)
{
    public DbSet<PurchaseIntent> PurchaseIntents => Set<PurchaseIntent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<PurchaseIntent>(entity =>
        {
            entity.ToTable("purchase_intents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(e => e.GuestUserId).HasColumnName("guest_user_id").IsRequired();
            entity.HasIndex(e => e.GuestUserId, "ix_purchase_intents_guest_user_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(e => e.Status, "ix_purchase_intents_status");
            entity.Property(e => e.CheckIn).HasColumnName("check_in").HasColumnType("date").IsRequired();
            entity.Property(e => e.CheckOut).HasColumnName("check_out").HasColumnType("date").IsRequired();
            entity.Property(e => e.District).HasColumnName("district").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Guests).HasColumnName("guests").IsRequired();
            entity.Property(e => e.BudgetTotal).HasColumnName("budget_total").HasColumnType("decimal(18,2)").IsRequired();
            entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(10).HasDefaultValue("USD").IsRequired();
            entity.Property(e => e.DemoPaymentMethodId).HasColumnName("demo_payment_method_id").HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
            entity.Property(e => e.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            entity.Property(e => e.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

            entity.Property(e => e.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.GuestUserId, e.IdempotencyKey }, "ix_purchase_intents_guest_idempotency");

            entity.Property(e => e.ContactName).HasColumnName("contact_name").HasMaxLength(100);
            entity.Property(e => e.ContactEmail).HasColumnName("contact_email").HasMaxLength(200);
            entity.Property(e => e.ContactPhone).HasColumnName("contact_phone").HasMaxLength(50);
            entity.Property(e => e.CancellationPolicyAccepted).HasColumnName("cancellation_policy_accepted").HasDefaultValue(true);

            entity.Property(e => e.SelectedRoomId).HasColumnName("selected_room_id");
            entity.Property(e => e.SelectedRoomTypeName).HasColumnName("selected_room_type_name").HasMaxLength(100);
            entity.Property(e => e.SelectedHotelName).HasColumnName("selected_hotel_name").HasMaxLength(150);
            entity.Property(e => e.StayTotal).HasColumnName("stay_total").HasColumnType("decimal(18,2)");

            entity.Property(e => e.QuoteId).HasColumnName("quote_id").HasMaxLength(64);
            entity.Property(e => e.QuoteExpiresAtUtc).HasColumnName("quote_expires_at_utc");

            entity.Property(e => e.BookingId).HasColumnName("booking_id");
            entity.Property(e => e.PaymentId).HasColumnName("payment_id");
            entity.Property(e => e.ReceiptVoucher).HasColumnName("receipt_voucher").HasMaxLength(100);
            entity.Property(e => e.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);

            entity.Property(e => e.CandidateAttempts).HasColumnName("candidate_attempts").HasDefaultValue(0);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            entity.Property(e => e.NextRetryAtUtc).HasColumnName("next_retry_at_utc");

            entity.Property(e => e.ConcurrencyToken).HasColumnName("concurrency_token").HasMaxLength(64).IsConcurrencyToken().IsRequired();
        });

        modelBuilder.UseSnakeCaseNamingConvention();
    }
}
