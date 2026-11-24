namespace SmartStay.Contracts.Http;

public sealed record ValidateDemoPaymentMethodResponse(
    bool IsValid,
    string? Status = null,
    decimal? Balance = null,
    string? Currency = null,
    string? Reason = null
);
