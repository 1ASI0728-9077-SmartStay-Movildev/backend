using System.Net.Http.Json;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Security;

public interface IIdentityVerificationClient
{
    Task<ValidateIdentityResponse?> ValidateAsync(string token);
}

public class IdentityVerificationClient(HttpClient httpClient, ILogger<IdentityVerificationClient> logger)
    : IIdentityVerificationClient
{
    public async Task<ValidateIdentityResponse?> ValidateAsync(string token)
    {
        try
        {
            var request = new ValidateIdentityRequest(token);
            var response = await httpClient.PostAsJsonAsync("/internal/v1/identity/validate", request);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Identity service returned non-success code: {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ValidateIdentityResponse>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to communicate with IAM service for token validation");
            return null;
        }
    }
}
