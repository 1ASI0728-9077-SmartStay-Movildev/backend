using Microsoft.EntityFrameworkCore;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Entities;
using SmartStay.Payments.Domain.Repositories;
using SmartStay.Payments.Infrastructure.Persistence.EFC.Configuration;

namespace SmartStay.Payments.Infrastructure.Persistence.EFC.Repositories;

public class PaymentRepository(PaymentsDbContext context) : IPaymentRepository
{
    public async Task AddAsync(Payment payment)
    {
        await context.Payments.AddAsync(payment);
    }

    public async Task<Payment?> FindByIdAsync(int id)
    {
        return await context.Payments.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Payment?> FindByBookingIdAsync(int bookingId)
    {
        return await context.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId);
    }

    public async Task<bool> HasCompletedPaymentForBookingAsync(int bookingId)
    {
        return await context.Payments
            .AnyAsync(p => p.BookingId == bookingId && p.Status == PaymentStatus.Completed);
    }

    public void Update(Payment payment)
    {
        context.Payments.Update(payment);
    }

    public async Task AddDemoPaymentMethodAsync(DemoPaymentMethod method)
    {
        await context.DemoPaymentMethods.AddAsync(method);
    }

    public async Task<DemoPaymentMethod?> FindDemoPaymentMethodByGuestUserIdAsync(int guestUserId)
    {
        return await context.DemoPaymentMethods.FirstOrDefaultAsync(m => m.GuestUserId == guestUserId);
    }

    public async Task<DemoPaymentMethod?> FindDemoPaymentMethodByIdAsync(string id)
    {
        return await context.DemoPaymentMethods.FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<DemoPaymentMethod?> FindDemoPaymentMethodByIdForUpdateAsync(string id, CancellationToken ct = default)
    {
        if (context.Database.IsMySql())
        {
            return await context.DemoPaymentMethods
                .FromSqlInterpolated($"SELECT * FROM demo_payment_methods WHERE id = {id} FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        return await context.DemoPaymentMethods.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public void UpdateDemoPaymentMethod(DemoPaymentMethod method)
    {
        context.DemoPaymentMethods.Update(method);
    }

    public async Task AddLedgerMovementAsync(PaymentLedgerMovement movement)
    {
        await context.PaymentLedgerMovements.AddAsync(movement);
    }

    public async Task<PaymentLedgerMovement?> FindLedgerMovementAsync(int paymentId, string operationType)
    {
        return await context.PaymentLedgerMovements
            .FirstOrDefaultAsync(m => m.PaymentId == paymentId && m.OperationType == operationType);
    }

    public async Task<List<PaymentLedgerMovement>> FindLedgerMovementsByDemoMethodAsync(string demoPaymentMethodId)
    {
        return await context.PaymentLedgerMovements
            .Where(m => m.DemoPaymentMethodId == demoPaymentMethodId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();
    }

    public async Task AddOutboxMessageAsync(OutboxMessage outboxMessage)
    {
        await context.OutboxMessages.AddAsync(outboxMessage);
    }

    public async Task<List<OutboxMessage>> GetPendingOutboxMessagesAsync(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await context.OutboxMessages
            .Where(m => m.PublishedAtUtc == null && (m.NextAttemptAtUtc == null || m.NextAttemptAtUtc <= now))
            .OrderBy(m => m.OccurredAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task AddIdempotencyRecordAsync(PaymentIdempotencyRecord record)
    {
        await context.IdempotencyRecords.AddAsync(record);
    }

    public async Task<PaymentIdempotencyRecord?> FindIdempotencyRecordAsync(int actorId, string idempotencyKey)
    {
        return await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.ActorId == actorId && r.IdempotencyKey == idempotencyKey);
    }

    public async Task AddInboxMessageAsync(InboxMessage inboxMessage)
    {
        await context.InboxMessages.AddAsync(inboxMessage);
    }

    public async Task<bool> HasProcessedInboxEventAsync(string consumerName, Guid eventId, CancellationToken ct = default)
    {
        return await context.InboxMessages
            .AnyAsync(m => m.ConsumerName == consumerName && m.EventId == eventId, ct);
    }
}
