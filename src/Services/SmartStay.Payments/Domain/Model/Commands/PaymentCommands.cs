namespace SmartStay.Payments.Domain.Model.Commands;

public record ProcessPaymentCommand(
    int BookingId,
    decimal Amount,
    string PaymentMethod,
    string CardNumber,
    string CardHolderName,
    string ExpirationDate,
    string Cvv,
    string IdempotencyKey,
    int ActorId
);

public record RefundPaymentCommand(
    int PaymentId,
    string Reason
);
