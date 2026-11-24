namespace SmartStay.Concierge.Interfaces.REST.Resources;

public sealed record CreatePurchaseIntentResource(
    string CheckIn,
    string CheckOut,
    string District,
    int Guests,
    decimal BudgetTotal,
    string Currency,
    string? ContactName,
    string ContactEmail,
    string ContactPhone,
    bool CancellationPolicyAccepted,
    string DemoPaymentMethodId
);

public sealed record PurchaseIntentTrackingResource(
    string Id,
    int GuestUserId,
    string Status,
    string CheckIn,
    string CheckOut,
    string District,
    int Guests,
    decimal BudgetTotal,
    string Currency,
    string DemoPaymentMethodId,
    bool CancellationPolicyAccepted,
    int? RoomId,
    string? RoomTypeName,
    string? HotelName,
    decimal? StayTotal,
    string? QuoteId,
    int? BookingId,
    int? PaymentId,
    string? ReceiptVoucher,
    string? FailureReason,
    decimal? TotalAmountPaid,
    decimal? UpdatedCardBalance,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc
);
