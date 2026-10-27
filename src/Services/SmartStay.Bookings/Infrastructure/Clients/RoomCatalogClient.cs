using System.Net.Http.Json;
using Polly;
using Polly.Retry;
using SmartStay.Contracts.Http;

namespace SmartStay.Bookings.Infrastructure.Clients;

public class RoomCatalogClient : IRoomCatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RoomCatalogClient> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public RoomCatalogClient(HttpClient httpClient, ILogger<RoomCatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 1,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(200 + Random.Shared.Next(50, 150)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("Transient failure calling Accommodations catalog. Retrying attempt {RetryCount} after {Delay}ms.", retryCount, timespan.TotalMilliseconds);
                });
    }

    public async Task<RoomSnapshot?> FindAsync(int roomId, CancellationToken ct = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                var response = await _httpClient.GetAsync($"internal/v1/rooms/{roomId}", ct);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Accommodations service returned {StatusCode} for room {RoomId}.", response.StatusCode, roomId);
                    return null;
                }

                return await response.Content.ReadFromJsonAsync<RoomSnapshot>(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is not TimeoutException and not HttpRequestException)
            {
                _logger.LogError(ex, "Unexpected error retrieving room {RoomId} from Accommodations catalog.", roomId);
                return null;
            }
        });
    }
}
