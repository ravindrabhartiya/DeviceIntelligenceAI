namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Language model implementation using Windows Copilot Runtime Phi Silica.
/// Requires Copilot+ PC with Windows App SDK 1.8+.
/// 
/// Wraps: Microsoft.Windows.AI.Generative.LanguageModel
/// </summary>
public sealed class WindowsLanguageModel : ILanguageModel
{
    private dynamic? _model;
    private bool _initialized;

    private WindowsLanguageModel(dynamic model)
    {
        _model = model;
        _initialized = true;
    }

    /// <summary>
    /// Create a WindowsLanguageModel using Phi Silica.
    /// Throws PlatformNotSupportedException if not on a Copilot+ PC.
    /// </summary>
    public static async Task<WindowsLanguageModel> CreateAsync(CancellationToken ct = default)
    {
        var lmType = Type.GetType("Microsoft.Windows.AI.Generative.LanguageModel, Microsoft.Windows.AI.Generative");
        if (lmType == null)
        {
            throw new PlatformNotSupportedException(
                "Windows Copilot Runtime LanguageModel API not available. " +
                "Requires Windows App SDK 1.8+ on a Copilot+ PC.");
        }

        // Call LanguageModel.CreateAsync()
        var createMethod = lmType.GetMethod("CreateAsync", Type.EmptyTypes)
            ?? throw new PlatformNotSupportedException("LanguageModel.CreateAsync not found.");

        dynamic operation = createMethod.Invoke(null, null)!;
        dynamic model = await operation;

        return new WindowsLanguageModel(model);
    }

    /// <summary>
    /// Check if Phi Silica APIs are available on this device.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            var lmType = Type.GetType("Microsoft.Windows.AI.Generative.LanguageModel, Microsoft.Windows.AI.Generative");
            return lmType != null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        EnsureInitialized();
        dynamic response = await _model!.GenerateResponseAsync(prompt);
        return (string)response.Text;
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        EnsureInitialized();
        // Phi Silica supports system + user context via a combined prompt
        var combinedPrompt = $"<|system|>\n{systemPrompt}\n<|end|>\n<|user|>\n{userPrompt}\n<|end|>\n<|assistant|>\n";
        dynamic response = await _model!.GenerateResponseAsync(combinedPrompt);
        return (string)response.Text;
    }

    public Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_initialized && _model != null);
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Phi Silica streaming via the dynamic WCR API is not wired up here; yield the
        // full response as a single chunk so callers still get a uniform streaming contract.
        // The underlying WinRT call doesn't observe the token, so race it against the token
        // to guarantee the caller's timeout/cancellation is honored at the await boundary.
        EnsureInitialized();
        var genTask = GenerateAsync(prompt, ct);
        await Task.WhenAny(genTask, Task.Delay(Timeout.Infinite, ct));
        ct.ThrowIfCancellationRequested();
        yield return await genTask;
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _model == null)
            throw new InvalidOperationException("Language model not initialized. Call CreateAsync first.");
    }

    public void Dispose()
    {
        if (_model is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _model = null;
        _initialized = false;
    }
}
