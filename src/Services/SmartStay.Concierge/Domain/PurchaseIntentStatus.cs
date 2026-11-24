namespace SmartStay.Concierge.Domain;

public enum PurchaseIntentStatus
{
    Pending = 0,
    Reserving = 1,
    AwaitingConfirmation = 2,
    Confirmed = 3,
    Cancelling = 4,
    Cancelled = 5,
    Failed = 6,
    Expired = 7
}
