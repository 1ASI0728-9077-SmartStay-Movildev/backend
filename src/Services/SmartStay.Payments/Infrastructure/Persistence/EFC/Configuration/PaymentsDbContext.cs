using Microsoft.EntityFrameworkCore;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Infrastructure.Persistence.Extensions;

namespace SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<DemoPaymentMethod> DemoPaymentMethods => Set<DemoPaymentMethod>();
    public DbSet<PaymentLedgerMovement> PaymentLedgerMovements => Set<PaymentLedgerMovement>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<PaymentIdempotencyRecord> IdempotencyRecords => Set<PaymentIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Payment Entity
        builder.Entity<Payment>().ToTable("payments");
        builder.Entity<Payment>().HasKey(p => p.Id);
        builder.Entity<Payment>().Property(p => p.Id).ValueGeneratedOnAdd();
        builder.Entity<Payment>().Property(p => p.BookingId).IsRequired();
        builder.Entity<Payment>().Property(p => p.TransactionId).IsRequired().HasMaxLength(100);
        builder.Entity<Payment>().Property(p => p.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<Payment>().Property(p => p.PaymentMethod).IsRequired().HasMaxLength(50);
        builder.Entity<Payment>().Property(p => p.CardHolderName).IsRequired().HasMaxLength(150);
        builder.Entity<Payment>().Property(p => p.CardNumberMasked).IsRequired().HasMaxLength(30);
        builder.Entity<Payment>().Property(p => p.PaymentDate).IsRequired();
        builder.Entity<Payment>().Property(p => p.Status).HasConversion<int>().IsRequired();
        builder.Entity<Payment>().Property(p => p.DemoPaymentMethodId).HasMaxLength(64);

        builder.Entity<Payment>().HasIndex(p => p.BookingId, "ux_payments_booking_id").IsUnique();
        builder.Entity<Payment>().HasIndex(p => p.TransactionId, "ix_payments_transaction_id").IsUnique();
        builder.Entity<Payment>().HasIndex(p => p.DemoPaymentMethodId, "ix_payments_demo_payment_method_id");

        // DemoPaymentMethod Entity
        builder.Entity<DemoPaymentMethod>().ToTable("demo_payment_methods", t =>
        {
            t.HasCheckConstraint("chk_demo_payment_methods_balance", "balance >= 0");
        });
        builder.Entity<DemoPaymentMethod>().HasKey(m => m.Id);
        builder.Entity<DemoPaymentMethod>().Property(m => m.Id).HasMaxLength(64).IsRequired();
        builder.Entity<DemoPaymentMethod>().Property(m => m.GuestUserId).IsRequired();
        builder.Entity<DemoPaymentMethod>().Property(m => m.Label).IsRequired().HasMaxLength(100);
        builder.Entity<DemoPaymentMethod>().Property(m => m.Status).IsRequired().HasMaxLength(50);
        builder.Entity<DemoPaymentMethod>().Property(m => m.Balance).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<DemoPaymentMethod>().Property(m => m.Currency).IsRequired().HasMaxLength(10);
        builder.Entity<DemoPaymentMethod>().Property(m => m.ConcurrencyToken).IsConcurrencyToken().IsRequired();
        builder.Entity<DemoPaymentMethod>().Property(m => m.CreatedAtUtc).IsRequired();
        builder.Entity<DemoPaymentMethod>().Property(m => m.UpdatedAtUtc).IsRequired();

        builder.Entity<DemoPaymentMethod>().HasIndex(m => m.GuestUserId, "ux_demo_payment_methods_guest_user_id").IsUnique();

        // PaymentLedgerMovement Entity
        builder.Entity<PaymentLedgerMovement>().ToTable("payment_ledger_movements", t =>
        {
            t.HasCheckConstraint("chk_payment_ledger_movements_amount", "amount > 0");
        });
        builder.Entity<PaymentLedgerMovement>().HasKey(m => m.Id);
        builder.Entity<PaymentLedgerMovement>().Property(m => m.Id).ValueGeneratedOnAdd();
        builder.Entity<PaymentLedgerMovement>().Property(m => m.DemoPaymentMethodId).IsRequired().HasMaxLength(64);
        builder.Entity<PaymentLedgerMovement>().Property(m => m.PaymentId).IsRequired();
        builder.Entity<PaymentLedgerMovement>().Property(m => m.OperationType).IsRequired().HasMaxLength(20);
        builder.Entity<PaymentLedgerMovement>().Property(m => m.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<PaymentLedgerMovement>().Property(m => m.BalanceAfter).HasColumnType("decimal(18,2)").IsRequired();
        builder.Entity<PaymentLedgerMovement>().Property(m => m.CreatedAtUtc).IsRequired();

        builder.Entity<PaymentLedgerMovement>().HasIndex(m => new { m.PaymentId, m.OperationType }, "ux_payment_ledger_movements_payment_operation").IsUnique();
        builder.Entity<PaymentLedgerMovement>().HasIndex(m => m.DemoPaymentMethodId, "ix_payment_ledger_movements_demo_payment_method_id");

        // OutboxMessage Entity
        builder.Entity<OutboxMessage>().ToTable("outbox_messages");
        builder.Entity<OutboxMessage>().HasKey(m => m.Id);
        builder.Entity<OutboxMessage>().Property(m => m.EventType).IsRequired().HasMaxLength(150);
        builder.Entity<OutboxMessage>().Property(m => m.AggregateId).IsRequired().HasMaxLength(100);
        builder.Entity<OutboxMessage>().Property(m => m.Payload).IsRequired().HasColumnType("longtext");
        builder.Entity<OutboxMessage>().Property(m => m.OccurredAtUtc).IsRequired();
        builder.Entity<OutboxMessage>().Property(m => m.PublishedAtUtc);
        builder.Entity<OutboxMessage>().Property(m => m.LeaseOwner).HasMaxLength(100);
        builder.Entity<OutboxMessage>().Property(m => m.LastError).HasMaxLength(2000);

        builder.Entity<OutboxMessage>().HasIndex(m => m.PublishedAtUtc, "ix_outbox_published_at");
        builder.Entity<OutboxMessage>().HasIndex(m => new { m.PublishedAtUtc, m.NextAttemptAtUtc }, "ix_outbox_polling");

        // InboxMessage Entity
        builder.Entity<InboxMessage>().ToTable("payments_inbox_messages");
        builder.Entity<InboxMessage>().HasKey(m => new { m.ConsumerName, m.EventId });
        builder.Entity<InboxMessage>().Property(m => m.ConsumerName).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.EventType).IsRequired().HasMaxLength(150);
        builder.Entity<InboxMessage>().Property(m => m.Status).IsRequired().HasMaxLength(50);
        builder.Entity<InboxMessage>().Property(m => m.LastError).HasMaxLength(1000);

        // PaymentIdempotencyRecord Entity
        builder.Entity<PaymentIdempotencyRecord>().ToTable("payment_idempotency_records");
        builder.Entity<PaymentIdempotencyRecord>().HasKey(r => new { r.ActorId, r.IdempotencyKey });
        builder.Entity<PaymentIdempotencyRecord>().Property(r => r.IdempotencyKey).HasMaxLength(100);
        builder.Entity<PaymentIdempotencyRecord>().Property(r => r.RequestHash).IsRequired().HasMaxLength(128);
        builder.Entity<PaymentIdempotencyRecord>().Property(r => r.Status).IsRequired().HasMaxLength(50);
        builder.Entity<PaymentIdempotencyRecord>().Property(r => r.ResponsePayload).HasColumnType("longtext");
        builder.Entity<PaymentIdempotencyRecord>().Property(r => r.CreatedAtUtc).IsRequired();

        builder.UseSnakeCaseNamingConvention();
    }
}
