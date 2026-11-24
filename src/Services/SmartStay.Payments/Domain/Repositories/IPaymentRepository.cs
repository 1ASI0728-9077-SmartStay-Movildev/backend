using Microsoft.EntityFrameworkCore.Storage;
using SmartStay.Payments.Domain.Model.Aggregates;
using SmartStay.Payments.Domain.Model.Entities;

namespace SmartStay.Payments.Domain.Repositories;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment);
    Task<Payment?> FindByIdAsync(int id);
    Task<Payment?> FindByBookingIdAsync(int bookingId);
    Task<bool> HasCompletedPaymentForBookingAsync(int bookingId);
    void Update(Payment payment);

    Task AddDemoPaymentMethodAsync(DemoPaymentMethod method);
    Task<DemoPaymentMethod?> FindDemoPaymentMethodByGuestUserIdAsync(int guestUserId);
    Task<DemoPaymentMethod?> FindDemoPaymentMethodByIdAsync(string id);
    Task<DemoPaymentMethod?> FindDemoPaymentMethodByIdForUpdateAsync(string id, CancellationToken ct = default);
    void UpdateDemoPaymentMethod(DemoPaymentMethod method);

    Task AddLedgerMovementAsync(PaymentLedgerMovement movement);
    Task<PaymentLedgerMovement?> FindLedgerMovementAsync(int paymentId, string operationType);
    Task<List<PaymentLedgerMovement>> FindLedgerMovementsByDemoMethodAsync(string demoPaymentMethodId);

    Task AddOutboxMessageAsync(OutboxMessage outboxMessage);
    Task<List<OutboxMessage>> GetPendingOutboxMessagesAsync(int batchSize, CancellationToken ct = default);

    Task AddIdempotencyRecordAsync(PaymentIdempotencyRecord record);
    Task<PaymentIdempotencyRecord?> FindIdempotencyRecordAsync(int actorId, string idempotencyKey);

    Task AddInboxMessageAsync(InboxMessage inboxMessage);
    Task<bool> HasProcessedInboxEventAsync(string consumerName, Guid eventId, CancellationToken ct = default);
}
