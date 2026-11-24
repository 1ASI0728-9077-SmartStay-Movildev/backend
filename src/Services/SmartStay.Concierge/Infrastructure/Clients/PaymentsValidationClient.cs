using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Clients;

public class PaymentsValidationClient : IPaymentsValidationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentsValidationClient> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public PaymentsValidationClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PaymentsValidationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var paymentsUrl = configuration["Services:PaymentsUrl"] ?? "http://payments:8080";
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(paymentsUrl.TrimEnd('/') + "/");
        }

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retrying Payments validation call #{RetryCount} after {Delay}ms due to: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<ValidateDemoPaymentMethodResponse> ValidateDemoPaymentMethodAsync(
        string demoPaymentMethodId,
        int guestUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(demoPaymentMethodId))
        {
            return new ValidateDemoPaymentMethodResponse(
                IsValid: false,
                Reason: "Demo payment method ID is required."
            );
        }

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var url = $"internal/v1/payments/demo-methods/{Uri.EscapeDataString(demoPaymentMethodId)}/validate?guestUserId={guestUserId}";
                var response = await _httpClient.GetAsync(url, ct);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>(cancellationToken: ct);
                    return result ?? new ValidateDemoPaymentMethodResponse(IsValid: false, Reason: "Empty response from Payments validation.");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var err = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>(cancellationToken: ct);
                    return err ?? new ValidateDemoPaymentMethodResponse(IsValid: false, Reason: "Demo payment method not found.");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                {
                    var err = await response.Content.ReadFromJsonAsync<ValidateDemoPaymentMethodResponse>(cancellationToken: ct);
                    return err ?? new ValidateDemoPaymentMethodResponse(IsValid: false, Reason: "Demo payment method is invalid, inactive, or not owned by guest.");
                }

                _logger.LogWarning("Payments validation returned status {StatusCode} for card {CardId}.", response.StatusCode, demoPaymentMethodId);
                return new ValidateDemoPaymentMethodResponse(IsValid: false, Reason: $"Payments service returned error status {response.StatusCode}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error communicating with Payments validation endpoint.");
                return new ValidateDemoPaymentMethodResponse(IsValid: false, Reason: "Unable to reach Payments validation service.");
            }
        });
    }
}
