namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Abstraction over language model inference.
/// Implementations: WindowsLanguageModel (Phi Silica via WCR) and MockLanguageModel (testing).
/// </summary>
public interface ILanguageModel : IDisposable
{
    /// <summary>
    /// Generate a response for the given prompt.
    /// </summary>
    Task<string> GenerateAsync(string prompt, CancellationToken ct = default);

    /// <summary>
    /// Generate a response with a system context and user prompt.
    /// </summary>
    Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

    /// <summary>
    /// Check if the model is ready for inference.
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken ct = default);
}
