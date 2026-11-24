using System.Net.Http.Headers;
using SmartStay.E2E.Tests.Infrastructure.Configuration;

namespace SmartStay.E2E.Tests.Infrastructure.Fixtures;

/// <summary>
/// Fixture managing HTTP client instances, Gateway connectivity, and healthcheck verification.
/// Can be shared across test classes using xUnit IClassFixture or ICollectionFixture.
/// </summary>
public class SmartStayApiFixture : IAsyncLifetime, IDisposable
{
    private readonly HttpClient _gatewayClient;
    private readonly HttpClient _directBookingsClient;
    private readonly HttpClient _directPaymentsClient;
    private readonly HttpClient _directConciergeClient;

    public HttpClient GatewayClient => _gatewayClient;
    public HttpClient DirectBookingsClient => _directBookingsClient;
    public HttpClient DirectPaymentsClient => _directPaymentsClient;
    public HttpClient DirectConciergeClient => _directConciergeClient;

    public SmartStayApiFixture()
    {
        var timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds);

        _gatewayClient = new HttpClient { BaseAddress = new Uri(E2ETestConfig.GatewayUrl), Timeout = timeout };
        _directBookingsClient = new HttpClient { BaseAddress = new Uri(E2ETestConfig.DirectBookingsUrl), Timeout = timeout };
        _directPaymentsClient = new HttpClient { BaseAddress = new Uri(E2ETestConfig.DirectPaymentsUrl), Timeout = timeout };
        _directConciergeClient = new HttpClient { BaseAddress = new Uri(E2ETestConfig.DirectConciergeUrl), Timeout = timeout };
    }

    public async Task InitializeAsync()
    {
        // Optional pre-check or warm-up can be executed here
        await Task.CompletedTask;
    }

    public async Task<bool> IsGatewayHealthyAsync()
    {
        try
        {
            var response = await _gatewayClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public HttpClient CreateClientWithAuth(string token, string? baseUrl = null)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl ?? E2ETestConfig.GatewayUrl),
            Timeout = TimeSpan.FromSeconds(E2ETestConfig.HttpTimeoutSeconds)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _gatewayClient.Dispose();
        _directBookingsClient.Dispose();
        _directPaymentsClient.Dispose();
        _directConciergeClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
