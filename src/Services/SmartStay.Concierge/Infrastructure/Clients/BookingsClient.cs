using System.Net.Http.Headers;
using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Concierge.Infrastructure.Security;
using SmartStay.Contracts.Http;

namespace SmartStay.Concierge.Infrastructure.Clients;

public class BookingsClient : IBookingsClient
{
    private readonly HttpClient _httpClient;
    private readonly ServiceTokenGenerator _tokenGenerator;
    private readonly ILogger<BookingsClient> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public BookingsClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ServiceTokenGenerator tokenGenerator,
        ILogger<BookingsClient> logger)
    {
        _httpClient = httpClient;
        _tokenGenerator = tokenGenerator;
        _logger = logger;

        var bookingsUrl = configuration["Services:BookingsUrl"] ?? "http://bookings:8080";
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(bookingsUrl.TrimEnd('/') + "/");
        }

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(2, retryAttempt => TimeSpan.FromMilliseconds(100 * retryAttempt),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning("Retrying Bookings client call #{RetryCount} after {Delay}ms due to: {Message}",
                        retryCount, timeSpan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<QuoteRequestResult> RequestQuoteAsync(CreateBookingQuoteRequest request, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("internal/v1/bookings/quotes", request, ct);
                if (response.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    var quote = await response.Content.ReadFromJsonAsync<BookingQuoteResponse>(cancellationToken: ct);
                    return new QuoteRequestResult(true, quote, null);
                }

                var errText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Quote request for Room #{RoomId} returned status {StatusCode}: {Error}",
                    request.RoomId, response.StatusCode, errText);

                return new QuoteRequestResult(false, null, $"HTTP {(int)response.StatusCode}: {errText}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting quote for Room #{RoomId}", request.RoomId);
                return new QuoteRequestResult(false, null, ex.Message);
            }
        });
    }

    public async Task<BookingByIntentResponse?> GetBookingByIntentIdAsync(string intentId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await _httpClient.GetAsync($"internal/v1/bookings/by-intent/{Uri.EscapeDataString(intentId)}", ct);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<BookingByIntentResponse>(cancellationToken: ct);
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning("Lookup booking by intent '{IntentId}' returned {StatusCode}.", intentId, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existing booking for intent '{IntentId}'.", intentId);
                return null;
            }
        });
    }

    public async Task<BookingReservationResult> CreateBookingAsync(
        int roomId,
        string guestName,
        string guestEmail,
        DateTime checkIn,
        DateTime checkOut,
        string quoteId,
        string purchaseIntentId,
        int guestUserId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/bookings");
            req.Headers.Add("Idempotency-Key", idempotencyKey);
            var token = _tokenGenerator.GenerateGuestToken(guestUserId, guestName);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                roomId,
                guestName,
                guestEmail,
                checkInDate = checkIn,
                checkOutDate = checkOut,
                quoteId,
                purchaseIntentId
            };
            req.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<BookingCreateResponseDto>(cancellationToken: ct);
                return new BookingReservationResult(true, body?.Id, false, null);
            }

            var errText = await response.Content.ReadAsStringAsync(ct);
            var statusCode = (int)response.StatusCode;

            if (statusCode == 409)
            {
                _logger.LogWarning("Reservation conflict (409) for Room #{RoomId}: {Error}", roomId, errText);
                return new BookingReservationResult(false, null, false, "Room is already booked for the requested dates.");
            }

            var isTransient = statusCode is >= 500 or 408;
            _logger.LogWarning("Create booking for Room #{RoomId} returned status {StatusCode}: {Error}",
                roomId, response.StatusCode, errText);

            return new BookingReservationResult(false, null, isTransient, $"HTTP {statusCode}: {errText}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Transient network error creating booking for Room #{RoomId}.", roomId);
            return new BookingReservationResult(false, null, true, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating booking for Room #{RoomId}.", roomId);
            return new BookingReservationResult(false, null, false, ex.Message);
        }
    }

    public async Task<bool> CancelBookingAsync(int bookingId, string reason, int guestUserId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"api/v1/bookings/{bookingId}/cancel");
                var token = _tokenGenerator.GenerateGuestToken(guestUserId);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(req, ct);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Cancelled booking #{BookingId} successfully (Reason: {Reason}).", bookingId, reason);
                    return true;
                }

                _logger.LogWarning("Cancel booking #{BookingId} returned status {StatusCode}.", bookingId, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling cancel for booking #{BookingId}.", bookingId);
                return false;
            }
        });
    }

    private sealed record BookingCreateResponseDto(int Id);
}
