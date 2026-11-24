using System.Net.Http.Json;
using SmartStay.Contracts.Http;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Clients;

/// <summary>
/// Direct HTTP client for Bookings internal endpoints (/internal/v1/bookings/...).
/// </summary>
public class InternalBookingsClient
{
    private readonly HttpClient _httpClient;

    public InternalBookingsClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(E2ETestConfig.DirectBookingsUrl),
            Timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds)
        };
    }

    public HttpClient RawClient => _httpClient;

    public async Task<HttpResponseMessage> CreateQuoteAsync(object request)
    {
        return await _httpClient.PostAsJsonAsync("/internal/v1/bookings/quotes", request);
    }

    public async Task<HttpResponseMessage> GetEligibilityAsync(int bookingId)
    {
        return await _httpClient.GetAsync($"/internal/v1/bookings/{bookingId}/eligibility");
    }

    public async Task<HttpResponseMessage> CheckAvailabilityAsync(AvailabilityRequest request)
    {
        return await _httpClient.PostAsJsonAsync("/internal/v1/availability/check", request);
    }
}
