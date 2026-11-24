using System.Net.Http.Headers;
using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Concierge.Infrastructure.Security;

namespace SmartStay.Concierge.Infrastructure.Clients;

public class PaymentsClient : IPaymentsClient
{
    private readonly HttpClient _httpClient;
    private readonly ServiceTokenGenerator _tokenGenerator;
    private readonly ILogger<PaymentsClient> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public PaymentsClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ServiceTokenGenerator tokenGenerator,
        ILogger<PaymentsClient> logger)
    {
        _httpClient = httpClient;
        _tokenGenerator = tokenGenerator;
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
                    _logger.LogWarning("Retrying Payments client call #{RetryCount} after {Delay}ms due to: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<PaymentLookupResult?> GetPaymentByBookingIdAsync(int bookingId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await _httpClient.GetAsync($"internal/v1/payments/booking/{bookingId}", ct);
                if (response.IsSuccessStatusCode)
                {
                    var payment = await response.Content.ReadFromJsonAsync<PaymentResourceDto>(cancellationToken: ct);
                    if (payment != null)
                    {
                        return new PaymentLookupResult(payment.Id, payment.BookingId, payment.Status, payment.Amount, payment.ReceiptVoucher);
                    }
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning("Lookup payment for booking #{BookingId} returned {StatusCode}.", bookingId, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying payment for booking #{BookingId}.", bookingId);
                return null;
            }
        });
    }

    public async Task<PaymentExecutionResult> ProcessDemoPaymentAsync(
        int bookingId,
        decimal amount,
        string currency,
        string demoPaymentMethodId,
        int guestUserId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/payments");
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            var token = _tokenGenerator.GenerateGuestToken(guestUserId);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                bookingId,
                amount,
                currency,
                paymentMethod = "DemoCard",
                demoPaymentMethodId
            };
            req.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
            {
                var res = await response.Content.ReadFromJsonAsync<PaymentResourceDto>(cancellationToken: ct);
                if (res != null)
                {
                    return new PaymentExecutionResult(true, res.Id, res.ReceiptVoucher, false, null);
                }
            }

            var errText = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;
            var isTransient = statusCode is >= 500 or 408;

            _logger.LogWarning("Process demo payment for booking #{BookingId} returned status {StatusCode}: {Error}",
                bookingId, response.StatusCode, errText);

            return new PaymentExecutionResult(false, null, null, isTransient, $"HTTP {statusCode}: {errText}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transient network error processing payment for booking #{BookingId}.", bookingId);
            return new PaymentExecutionResult(false, null, null, true, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing payment for booking #{BookingId}.", bookingId);
            return new PaymentExecutionResult(false, null, null, false, ex.Message);
        }
    }

    private sealed record PaymentResourceDto(
        int Id,
        int BookingId,
        string TransactionId,
        decimal Amount,
        string Status,
        string CardNumberMasked,
        DateTime PaymentDate,
        string? DemoPaymentMethodId,
        string? ReceiptVoucher
    );
}
