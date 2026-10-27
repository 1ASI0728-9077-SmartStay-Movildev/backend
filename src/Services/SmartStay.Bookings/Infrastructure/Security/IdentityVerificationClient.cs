using System.Net.Http.Json;
using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Infrastructure.Security;

public interface IIdentityVerificationClient
{
    Task<ValidateIdentityResponse> ValidateTokenAsync(string token, CancellationToken ct = default);
}

public class IdentityVerificationClient(
    HttpClient httpClient,
    ILogger<IdentityVerificationClient> logger) : IIdentityVerificationClient
{
    public async Task<ValidateIdentityResponse> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        try
        {
            var request = new ValidateIdentityRequest(token);
            var response = await httpClient.PostAsJsonAsync("internal/v1/identity/validate", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Identity service returned status code {StatusCode}.", response.StatusCode);
                return new ValidateIdentityResponse(false, null, null, null, null, null, null, "Identity service error");
            }

            var result = await response.Content.ReadFromJsonAsync<ValidateIdentityResponse>(cancellationToken: ct);
            return result ?? new ValidateIdentityResponse(false, null, null, null, null, null, null, "Empty response from identity service");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to communicate with internal IAM service.");
            return new ValidateIdentityResponse(false, null, null, null, null, null, null, "IAM service unreachable");
        }
    }
}
