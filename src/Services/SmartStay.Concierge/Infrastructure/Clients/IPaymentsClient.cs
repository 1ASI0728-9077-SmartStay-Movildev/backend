namespace SmartStay.Concierge.Infrastructure.Clients;

public sealed record PaymentLookupResult(int Id, int BookingId, string Status, decimal Amount, string? ReceiptVoucher);

public sealed record PaymentExecutionResult(bool Success, int? PaymentId, string? ReceiptVoucher, bool IsTransientError, string? ErrorMessage);

public interface IPaymentsClient
{
    Task<PaymentLookupResult?> GetPaymentByBookingIdAsync(int bookingId, CancellationToken ct = default);
    Task<PaymentExecutionResult> ProcessDemoPaymentAsync(
        int bookingId,
        decimal amount,
        string currency,
        string demoPaymentMethodId,
        int guestUserId,
        string idempotencyKey,
        CancellationToken ct = default);
}
