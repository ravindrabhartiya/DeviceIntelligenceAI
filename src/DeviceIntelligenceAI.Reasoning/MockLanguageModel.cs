namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Mock language model for testing and development.
/// Returns template-based responses that simulate reasoning behavior.
/// </summary>
public sealed class MockLanguageModel : ILanguageModel
{
    private readonly Func<string, string>? _responseGenerator;

    public MockLanguageModel(Func<string, string>? responseGenerator = null)
    {
        _responseGenerator = responseGenerator;
    }

    /// <summary>
    /// Records of all prompts sent to this mock (for test assertions).
    /// </summary>
    public List<string> ReceivedPrompts { get; } = new();

    public Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        ReceivedPrompts.Add(prompt);

        if (_responseGenerator != null)
            return Task.FromResult(_responseGenerator(prompt));

        return Task.FromResult(GenerateDefaultResponse(prompt));
    }

    public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var combined = $"[System: {systemPrompt}] [User: {userPrompt}]";
        ReceivedPrompts.Add(combined);

        if (_responseGenerator != null)
            return Task.FromResult(_responseGenerator(combined));

        return Task.FromResult(GenerateDefaultResponse(userPrompt));
    }

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(true);

    public void Dispose() { }

    private static string GenerateDefaultResponse(string prompt)
    {
        var lower = prompt.ToLowerInvariant();

        if (lower.Contains("summarize") || lower.Contains("summary"))
            return "Based on the available evidence, the device is in a generally healthy state with minor concerns noted.";

        if (lower.Contains("fail") || lower.Contains("error"))
            return "The failure appears to be related to the evidence provided. The most likely root cause is indicated by the error code in the context.";

        if (lower.Contains("cause") || lower.Contains("why"))
            return "Based on temporal analysis, the most likely cause is the change that occurred immediately before the issue was observed.";

        if (lower.Contains("safe") || lower.Contains("update") || lower.Contains("ready"))
            return "Based on the current device state, updating appears safe. No blocking issues were identified in the evidence.";

        if (lower.Contains("mermaid") || lower.Contains("diagram"))
            return "```mermaid\nstateDiagram-v2\n    [*] --> Idle\n    Idle --> Scanning\n    Scanning --> Downloading\n    Downloading --> Installing\n    Installing --> [*]\n```";

        return "Based on the provided context, the device state has been analyzed. See the evidence below for details.";
    }
}
