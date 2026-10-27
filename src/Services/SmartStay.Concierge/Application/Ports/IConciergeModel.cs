namespace SmartStay.Concierge.Application.Ports;

/// <summary>
/// Puerto para el modelo de lenguaje (DeepSeek u otro proveedor LLM).
/// Genera respuestas explicativas o clasificaciones JSON estructuradas.
/// </summary>
public interface IConciergeModel
{
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
    Task<string> CompleteChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
