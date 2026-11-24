namespace SmartStay.Payments.Domain.Model.Aggregates;

public class DemoPaymentMethod
{
    public DemoPaymentMethod()
    {
        Id = $"dpm_{Guid.NewGuid():N}";
        Label = "SmartStay Demo";
        Status = "Active";
        Balance = 1000000.00m;
        Currency = "USD";
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public DemoPaymentMethod(int guestUserId, decimal initialCredit = 1000000.00m, string currency = "USD") : this()
    {
        GuestUserId = guestUserId;
        Balance = initialCredit;
        Currency = currency;
    }

    public string Id { get; private set; }
    public int GuestUserId { get; private set; }
    public string Label { get; private set; }
    public string Status { get; private set; }
    public decimal Balance { get; private set; }
    public string Currency { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public void Debit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Debit amount must be positive.");
        if (Balance < amount)
            throw new InvalidOperationException($"Insufficient balance: available {Balance}, required {amount}.");

        Balance -= amount;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Credit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");

        Balance += amount;
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status = "Inactive";
        ConcurrencyToken = Guid.NewGuid();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
