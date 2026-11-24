using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Clients;

public interface IPaymentsValidationClient
{
    Task<ValidateDemoPaymentMethodResponse> ValidateDemoPaymentMethodAsync(string demoPaymentMethodId, int guestUserId, CancellationToken ct = default);
}
