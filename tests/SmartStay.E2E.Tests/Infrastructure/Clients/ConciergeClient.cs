using System.Net.Http.Headers;
using System.Net.Http.Json;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Clients;

/// <summary>
/// Typed client for Concierge purchase intents and conversational endpoints.
/// Can target Gateway or direct Concierge instance.
/// </summary>
public class ConciergeClient
{
    private readonly HttpClient _httpClient;

    public ConciergeClient(HttpClient? httpClient = null, bool useDirectUrl = true)
    {
        var baseUrl = useDirectUrl ? E2ETestConfig.DirectConciergeUrl : E2ETestConfig.GatewayUrl;
        _httpClient = httpClient ?? new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds)
        };
    }

    public HttpClient RawClient => _httpClient;

    public async Task<HttpResponseMessage> SendMessageAsync(object request, string? token = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/concierge/messages")
        {
            Content = JsonContent.Create(request)
        };
        ApplyHeaders(req, token);
        return await _httpClient.SendAsync(req);
    }

    public async Task<HttpResponseMessage> CreatePurchaseIntentAsync(object request, string token, string? idempotencyKey = null)
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

    public async Task<HttpResponseMessage> GetPurchaseIntentAsync(string intentId, string token)
    {
        return await SendWithRetryOn429Async(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/concierge/purchase-intents/{intentId}");
            ApplyHeaders(req, token);
            return req;
        });
    }

    public async Task<HttpResponseMessage> CancelPurchaseIntentAsync(string intentId, string token)
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
            if (resp.StatusCode != (System.Net.HttpStatusCode)429 || attempt == maxAttempts)
            {
                return resp;
            }
            resp.Dispose();
            await Task.Delay(1000 * attempt);
        }
        throw new InvalidOperationException("Unreachable");
    }

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
