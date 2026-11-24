namespace SmartStay.Payments.Domain.Model.Aggregates;

public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Refunded = 3
}

public class Payment
{
    public Payment()
    {
        TransactionId = Guid.NewGuid().ToString();
        PaymentMethod = string.Empty;
        CardHolderName = string.Empty;
        CardNumberMasked = string.Empty;
        Status = PaymentStatus.Pending;
        PaymentDate = DateTime.UtcNow;
    }

    public Payment(
        int bookingId,
        decimal amount,
        string paymentMethod,
        string cardHolderName,
        string cardNumber,
        string? demoPaymentMethodId = null) : this()
    {
        BookingId = bookingId;
        Amount = amount;
        PaymentMethod = paymentMethod;
        CardHolderName = cardHolderName;
        DemoPaymentMethodId = demoPaymentMethodId;
        CardNumberMasked = !string.IsNullOrEmpty(cardNumber) && cardNumber.Length > 4
            ? "**** **** **** " + cardNumber[^4..]
            : (!string.IsNullOrEmpty(demoPaymentMethodId) ? "DemoCard" : "****");
    }

    public int Id { get; private set; }
    public int BookingId { get; private set; }
    public string TransactionId { get; private set; }
    public string? DemoPaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMethod { get; private set; }
    public string CardHolderName { get; private set; }
    public string CardNumberMasked { get; private set; }
    public DateTime PaymentDate { get; private set; }
    public PaymentStatus Status { get; private set; }

    public void Complete()
    {
        Status = PaymentStatus.Completed;
    }

    public void Fail()
    {
        Status = PaymentStatus.Failed;
    }

    public void Refund()
    {
        Status = PaymentStatus.Refunded;
    }
}
