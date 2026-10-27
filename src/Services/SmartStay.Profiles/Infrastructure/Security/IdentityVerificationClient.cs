using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Contracts.Http;

namespace SmartStay.Profiles.Infrastructure.Security;

public interface IIdentityVerificationClient
{
    Task<ValidateIdentityResponse?> ValidateTokenAsync(string token, CancellationToken ct = default);
}

public class IdentityVerificationClient : IIdentityVerificationClient
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger<IdentityVerificationClient> _logger;

    public IdentityVerificationClient(HttpClient httpClient, ILogger<IdentityVerificationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retrying IAM service call #{RetryCount} after {Delay}ms due to: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<ValidateIdentityResponse?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var request = new ValidateIdentityRequest(token);
            var response = await _httpClient.PostAsJsonAsync("/internal/v1/identity/validate", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("IAM validation service returned status code {StatusCode}", response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ValidateIdentityResponse>(cancellationToken: ct);
        });
    }
}
