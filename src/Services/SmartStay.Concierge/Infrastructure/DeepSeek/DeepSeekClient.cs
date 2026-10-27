using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartStay.Concierge.Application.Ports;

namespace SmartStay.Concierge.Infrastructure.DeepSeek;

public class DeepSeekClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<DeepSeekClient> logger) : IConciergeModel
{
    private readonly string _apiKey = configuration["DeepSeek:ApiKey"] ?? Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? string.Empty;
    private readonly string _model = configuration["DeepSeek:Model"] ?? "deepseek-chat";

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            logger.LogWarning("DeepSeek API key is not configured. Returning deterministic fallback.");
            return string.Empty;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var payload = new
            {
                model = _model,
                thinking = new { type = "disabled" },
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                response_format = new { type = "json_object" },
                max_tokens = 400,
                stream = false
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("DeepSeek API request failed with status code {StatusCode}.", response.StatusCode);
                return string.Empty;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            var parsed = JsonNode.Parse(responseBody);
            var content = parsed?["choices"]?[0]?["message"]?["content"]?.ToString();
            return content ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error communicating with DeepSeek API. Using fallback response.");
            return string.Empty;
        }
    }

    public async Task<string> CompleteChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            logger.LogWarning("DeepSeek API key is not configured. Returning fallback chat.");
            return string.Empty;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var payload = new
            {
                model = _model,
                thinking = new { type = "disabled" },
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                max_tokens = 400,
                stream = false
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("DeepSeek API chat failed with status code {StatusCode}.", response.StatusCode);
                return string.Empty;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);
            var parsed = JsonNode.Parse(responseBody);
            var content = parsed?["choices"]?[0]?["message"]?["content"]?.ToString();
            return content ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error calling DeepSeek chat. Using fallback.");
            return string.Empty;
        }
    }
}
