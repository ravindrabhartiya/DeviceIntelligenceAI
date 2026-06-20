namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Factory that creates the best available language model.
/// Fallback chain: Phi Silica (WCR) → Ollama (local SLM) → Mock (canned responses).
/// 
/// Configure model via environment variable:
///   DEVICE_AI_MODEL=phi3:mini (default)
///   DEVICE_AI_MODEL=gemma2:2b
///   DEVICE_AI_MODEL=llama3.2:1b
/// </summary>
public static class LanguageModelFactory
{
    /// <summary>
    /// Create the best available language model for the current environment.
    /// </summary>
    /// <returns>The created model and a description of which backend was selected.</returns>
    public static async Task<(ILanguageModel Model, string Backend)> CreateAsync()
    {
        // 1. Try Phi Silica (Windows AI Runtime on Copilot+ PCs)
        if (WindowsLanguageModel.IsAvailable())
        {
            try
            {
                var model = await WindowsLanguageModel.CreateAsync();
                return (model, "Phi Silica (Windows AI)");
            }
            catch { /* fall through */ }
        }

        // 2. Try Ollama (local HTTP server with open-source SLMs)
        if (OllamaLanguageModel.IsAvailable())
        {
            var modelName = GetConfiguredModel();
            var ollama = new OllamaLanguageModel(modelName);
            if (await ollama.IsReadyAsync())
            {
                return (ollama, $"Ollama ({modelName})");
            }
            ollama.Dispose();
        }

        // 3. Fall back to mock (canned template responses)
        return (new MockLanguageModel(), "Mock (no LLM available)");
    }

    /// <summary>
    /// Synchronous version for app startup scenarios.
    /// </summary>
    public static (ILanguageModel Model, string Backend) Create()
    {
        // 1. Try Phi Silica
        if (WindowsLanguageModel.IsAvailable())
        {
            try
            {
                var model = WindowsLanguageModel.CreateAsync().GetAwaiter().GetResult();
                return (model, "Phi Silica (Windows AI)");
            }
            catch { /* fall through */ }
        }

        // 2. Try Ollama
        if (OllamaLanguageModel.IsAvailable())
        {
            var modelName = GetConfiguredModel();
            var ollama = new OllamaLanguageModel(modelName);
            var ready = ollama.IsReadyAsync().GetAwaiter().GetResult();
            if (ready)
            {
                return (ollama, $"Ollama ({modelName})");
            }
            ollama.Dispose();
        }

        // 3. Mock
        return (new MockLanguageModel(), "Mock (no LLM available)");
    }

    private static string GetConfiguredModel()
    {
        return Environment.GetEnvironmentVariable("DEVICE_AI_MODEL") ?? "phi3:mini";
    }
}
