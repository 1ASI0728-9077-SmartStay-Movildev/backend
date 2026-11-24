using System.ComponentModel.DataAnnotations;

namespace SmartStay.Payments.Interfaces.REST.Resources;

public record ProcessPaymentResource(
    [Required] int BookingId,
    [Required] decimal Amount,
    [Required] string PaymentMethod,
    string? CardNumber = null,
    string? CardHolderName = null,
    string? ExpirationDate = null,
    string? Cvv = null,
    string? DemoPaymentMethodId = null
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.Equals(PaymentMethod, "DemoCard", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(DemoPaymentMethodId))
            {
                yield return new ValidationResult(
                    "DemoPaymentMethodId is required when PaymentMethod is DemoCard.",
                    new[] { nameof(DemoPaymentMethodId) });
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(CardNumber))
            {
                yield return new ValidationResult(
                    "CardNumber is required for credit card payments.",
                    new[] { nameof(CardNumber) });
            }
        }
    }
}

public record PaymentResource(
    int Id,
    int BookingId,
    string TransactionId,
    decimal Amount,
    string Status,
    string CardNumberMasked,
    string PaymentDate,
    string? DemoPaymentMethodId = null,
    string? ReceiptVoucher = null
);

public record DemoPaymentMethodResource(
    string Id,
    int GuestUserId,
    string Label,
    string Status,
    decimal Balance,
    string Currency
);
