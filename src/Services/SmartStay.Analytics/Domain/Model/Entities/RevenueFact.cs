namespace SmartStay.Analytics.Domain.Model.Entities;

public class RevenueFact
{
    public RevenueFact() { }

    public RevenueFact(int paymentId, int bookingId, int hotelId, decimal amount, string currency, DateTime occurredAtUtc)
    {
        PaymentId = paymentId;
        BookingId = bookingId;
        HotelId = hotelId;
        Amount = amount;
        Currency = currency;
        OccurredAtUtc = occurredAtUtc;
        IsRefunded = false;
        RefundAmount = 0;
    }

    public int PaymentId { get; private set; }
    public int BookingId { get; private set; }
    public int HotelId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public bool IsRefunded { get; private set; }
    public decimal RefundAmount { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public void MarkRefunded(decimal refundAmount)
    {
        IsRefunded = true;
        RefundAmount = refundAmount;
    }
}
