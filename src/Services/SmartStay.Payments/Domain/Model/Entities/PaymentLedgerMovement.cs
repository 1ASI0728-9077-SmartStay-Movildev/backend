namespace SmartStay.Payments.Domain.Model.Entities;

public class PaymentLedgerMovement
{
    public PaymentLedgerMovement()
    {
        DemoPaymentMethodId = string.Empty;
        OperationType = string.Empty;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public PaymentLedgerMovement(
        string demoPaymentMethodId,
        int paymentId,
        string operationType,
        decimal amount,
        decimal balanceAfter)
    {
        DemoPaymentMethodId = demoPaymentMethodId;
        PaymentId = paymentId;
        OperationType = operationType;
        Amount = amount;
        BalanceAfter = balanceAfter;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public int Id { get; private set; }
    public string DemoPaymentMethodId { get; private set; }
    public int PaymentId { get; private set; }
    public string OperationType { get; private set; }
    public decimal Amount { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
