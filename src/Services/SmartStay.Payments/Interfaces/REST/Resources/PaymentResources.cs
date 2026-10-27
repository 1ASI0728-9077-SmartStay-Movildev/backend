using System.ComponentModel.DataAnnotations;

namespace SmartStay.Payments.Interfaces.REST.Resources;

public record ProcessPaymentResource(
    [Required] int BookingId,
    [Required] decimal Amount,
    [Required] string PaymentMethod,
    [Required] [CreditCard] string CardNumber,
    [Required] string CardHolderName,
    [Required] string ExpirationDate,
    [Required] string Cvv
);

public record PaymentResource(
    int Id,
    int BookingId,
    string TransactionId,
    decimal Amount,
    string Status,
    string CardNumberMasked,
    string PaymentDate
);
