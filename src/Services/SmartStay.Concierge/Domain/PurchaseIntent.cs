namespace SmartStay.Concierge.Domain;

/// <summary>
/// Agregado raíz de Mandato e Intención de Compra Autónoma en Concierge.
/// Gestiona la máquina de estados, idempotencia y ciclo de vida de la reserva y cobro.
/// </summary>
public class PurchaseIntent
{
    public PurchaseIntent()
    {
        Id = string.Empty;
        IdempotencyKey = string.Empty;
        DemoPaymentMethodId = string.Empty;
        District = string.Empty;
        Currency = "USD";
        ContactName = string.Empty;
        ContactEmail = string.Empty;
        ContactPhone = string.Empty;
        Status = PurchaseIntentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public PurchaseIntent(
        string id,
        int guestUserId,
        string idempotencyKey,
        string demoPaymentMethodId,
        DateOnly checkIn,
        DateOnly checkOut,
        int guests,
        string district,
        decimal budgetTotal,
        string currency,
        string contactName,
        string contactEmail,
        string contactPhone,
        bool cancellationPolicyAccepted,
        int mandateTtlMinutes = 15) : this()
    {
        Id = id;
        GuestUserId = guestUserId;
        IdempotencyKey = idempotencyKey;
        DemoPaymentMethodId = demoPaymentMethodId;
        CheckIn = checkIn;
        CheckOut = checkOut;
        Guests = guests;
        District = district;
        BudgetTotal = budgetTotal;
        Currency = currency;
        ContactName = contactName;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        CancellationPolicyAccepted = cancellationPolicyAccepted;
        Status = PurchaseIntentStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        ExpiresAtUtc = DateTime.UtcNow.AddMinutes(mandateTtlMinutes);
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public string Id { get; set; }
    public int GuestUserId { get; set; }
    public PurchaseIntentStatus Status { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public string District { get; set; }
    public int Guests { get; set; }
    public decimal BudgetTotal { get; set; }
    public string Currency { get; set; }
    public string DemoPaymentMethodId { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string IdempotencyKey { get; set; }
    public string ContactName { get; set; }
    public string ContactEmail { get; set; }
    public string ContactPhone { get; set; }
    public bool CancellationPolicyAccepted { get; set; }

    public int? SelectedRoomId { get; set; }
    public string? SelectedRoomTypeName { get; set; }
    public string? SelectedHotelName { get; set; }
    public decimal? StayTotal { get; set; }

    public string? QuoteId { get; set; }
    public DateTime? QuoteExpiresAtUtc { get; set; }

    public int? BookingId { get; set; }
    public int? PaymentId { get; set; }
    public string? ReceiptVoucher { get; set; }
    public string? FailureReason { get; set; }

    public int CandidateAttempts { get; set; } = 0;
    public int RetryCount { get; set; } = 0;
    public DateTime? NextRetryAtUtc { get; set; }

    public string ConcurrencyToken { get; set; }

    public bool IsMandateExpired => DateTime.UtcNow >= ExpiresAtUtc;

    public string StableBookingIdempotencyKey => $"concierge-booking-{Id}";
    public string StablePaymentIdempotencyKey => $"concierge-payment-{Id}";

    public void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

    public void TransitionToReserving(string quoteId, DateTime quoteExpiresAtUtc, int roomId, string? roomTypeName, string? hotelName, decimal stayTotal)
    {
        QuoteId = quoteId;
        QuoteExpiresAtUtc = quoteExpiresAtUtc;
        SelectedRoomId = roomId;
        SelectedRoomTypeName = roomTypeName;
        SelectedHotelName = hotelName;
        StayTotal = stayTotal;
        Status = PurchaseIntentStatus.Reserving;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToAwaitingConfirmation(int bookingId)
    {
        BookingId = bookingId;
        Status = PurchaseIntentStatus.AwaitingConfirmation;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToConfirmed(int paymentId, string receiptVoucher)
    {
        PaymentId = paymentId;
        ReceiptVoucher = receiptVoucher;
        Status = PurchaseIntentStatus.Confirmed;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToCancelling()
    {
        Status = PurchaseIntentStatus.Cancelling;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToCancelled()
    {
        Status = PurchaseIntentStatus.Cancelled;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToFailed(string reason)
    {
        Status = PurchaseIntentStatus.Failed;
        FailureReason = reason;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }

    public void TransitionToExpired(string reason = "Autonomous purchase mandate expired (15m limit exceeded).")
    {
        Status = PurchaseIntentStatus.Expired;
        FailureReason = reason;
        RetryCount = 0;
        NextRetryAtUtc = null;
        Touch();
    }
}
