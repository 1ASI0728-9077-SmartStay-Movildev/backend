using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SmartStay.Contracts.Http;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Clients;

/// <summary>
/// High-level typed HTTP client for SmartStay Gateway public routes (/api/v1/...).
/// </summary>
public class GatewayClient
{
    private readonly HttpClient _httpClient;

    public GatewayClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(E2ETestConfig.GatewayUrl),
            Timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds)
        };
    }

    public HttpClient RawClient => _httpClient;

    #region Health

    public async Task<HttpResponseMessage> GetHealthAsync()
    {
        return await _httpClient.GetAsync("/health");
    }

    #endregion

    #region Authentication

    public async Task<HttpResponseMessage> SignInRawAsync(string username, string password)
    {
        return await _httpClient.PostAsJsonAsync("/api/v1/authentication/sign-in", new
        {
            username,
            password
        });
    }

    public async Task<HttpResponseMessage> SignUpRawAsync(string username, string password, string? role = null)
    {
        var body = role != null
            ? (object)new { username, password, role }
            : new { username, password };

        return await _httpClient.PostAsJsonAsync("/api/v1/authentication/sign-up", body);
    }

    #endregion

    #region Rooms & Accommodations

    public async Task<HttpResponseMessage> SearchRoomsAsync(string? district = null, int? guests = null, decimal? minPrice = null, decimal? maxPrice = null)
    {
        var queryParams = new List<string>();
        if (!string.IsNullOrWhiteSpace(district)) queryParams.Add($"district={Uri.EscapeDataString(district)}");
        if (guests.HasValue) queryParams.Add($"guests={guests.Value}");
        if (minPrice.HasValue) queryParams.Add($"minPrice={minPrice.Value}");
        if (maxPrice.HasValue) queryParams.Add($"maxPrice={maxPrice.Value}");

        var query = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : string.Empty;
        return await _httpClient.GetAsync($"/api/v1/rooms/search{query}");
    }

    public async Task<HttpResponseMessage> GetRoomTourAsync(int roomId)
    {
        return await _httpClient.GetAsync($"/api/v1/rooms/{roomId}/tour");
    }

    #endregion

    #region Bookings

    public async Task<HttpResponseMessage> CreateBookingAsync(object request, string? token = null, string? idempotencyKey = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bookings")
        {
            Content = JsonContent.Create(request)
        };
        ApplyHeaders(req, token, idempotencyKey);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> GetBookingByIdAsync(int bookingId, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/bookings/{bookingId}");
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> GetMyBookingsAsync(string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/bookings/mine");
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> CancelBookingAsync(int bookingId, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/bookings/{bookingId}/cancel");
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    #endregion

    #region Payments & Demo Method

    public async Task<HttpResponseMessage> ActivateDemoPaymentMethodAsync(string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/demo-method");
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> GetDemoPaymentMethodAsync(string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/payments/demo-method");
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> ProcessPaymentAsync(object request, string? token = null, string? idempotencyKey = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments")
        {
            Content = JsonContent.Create(request)
        };
        ApplyHeaders(req, token, idempotencyKey);
        return await _httpClient.SendAsync(req);
    }

    #endregion

    #region Concierge

    public async Task<HttpResponseMessage> SendConciergeMessageAsync(object request, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/concierge/messages")
        {
            Content = JsonContent.Create(request)
        };
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> CreatePurchaseIntentAsync(object request, string? token = null, string? idempotencyKey = null)
    {
        return await SendWithRetryOn429Async(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/concierge/purchase-intents")
            {
                Content = JsonContent.Create(request)
            };
            ApplyHeaders(req, token, idempotencyKey);
            return req;
        });
    }

    public async Task<HttpResponseMessage> GetPurchaseIntentAsync(string intentId, string? token = null)
    {
        return await SendWithRetryOn429Async(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/concierge/purchase-intents/{intentId}");
            ApplyHeaders(req, token);
            return req;
        });
    }

    public async Task<HttpResponseMessage> CancelPurchaseIntentAsync(string intentId, string? token = null)
    {
        return await SendWithRetryOn429Async(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/concierge/purchase-intents/{intentId}/cancel");
            ApplyHeaders(req, token);
            return req;
        });
    }

    private async Task<HttpResponseMessage> SendWithRetryOn429Async(Func<HttpRequestMessage> requestFactory, int maxAttempts = 8)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var req = requestFactory();
            var resp = await _httpClient.SendAsync(req);
            if (resp.StatusCode != (HttpStatusCode)429 || attempt == maxAttempts)
            {
                return resp;
            }
            resp.Dispose();
            await Task.Delay(1000 * attempt);
        }
        throw new InvalidOperationException("Unreachable");
    }

    #endregion

    private static void ApplyHeaders(HttpRequestMessage req, string? token, string? idempotencyKey = null)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }
    }
}
