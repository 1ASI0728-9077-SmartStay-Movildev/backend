using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Clients;

/// <summary>
/// Direct HTTP client for Payments internal endpoints (/internal/v1/payments/...).
/// </summary>
public class InternalPaymentsClient
{
    private readonly HttpClient _httpClient;

    public InternalPaymentsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(E2ETestConfig.DirectPaymentsUrl),
            Timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds)
        };
    }

    public HttpClient RawClient => _httpClient;

    public async Task<HttpResponseMessage> ValidateDemoPaymentMethodAsync(string methodId, int guestUserId)
    {
        return await _httpClient.GetAsync($"/internal/v1/payments/demo-methods/{Uri.EscapeDataString(methodId)}/validate?guestUserId={guestUserId}");
    }

    public async Task<HttpResponseMessage> GetPaymentByBookingIdAsync(int bookingId)
    {
        return await _httpClient.GetAsync($"/internal/v1/payments/booking/{bookingId}");
    }
}
