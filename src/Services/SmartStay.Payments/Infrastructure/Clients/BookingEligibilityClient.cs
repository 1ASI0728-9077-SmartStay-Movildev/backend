using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Contracts.Http;

namespace SmartStay.Payments.Infrastructure.Clients;

public class BookingEligibilityClient : IBookingEligibilityClient
{
    private readonly HttpClient _httpClient;
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly ILogger<BookingEligibilityClient> _logger;

    public BookingEligibilityClient(HttpClient httpClient, ILogger<BookingEligibilityClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retrying Bookings service call #{RetryCount} after {Delay}ms due to: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<BookingEligibilityResponse?> CheckEligibilityAsync(int bookingId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var response = await _httpClient.GetAsync($"/internal/v1/bookings/{bookingId}/eligibility", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bookings service returned status {StatusCode} for booking #{BookingId}.", response.StatusCode, bookingId);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<BookingEligibilityResponse>(cancellationToken: ct);
        });
    }
}
