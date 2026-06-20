using DeviceIntelligenceAI.Ingestion.SemanticIndex;

namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// RAG (Retrieval-Augmented Generation) pipeline.
/// Retrieves relevant facts from the semantic index, constructs a context window,
/// and sends it to the language model with an appropriate prompt template.
/// </summary>
public sealed class RagPipeline
{
    private readonly ISemanticIndex _semanticIndex;
    private readonly ILanguageModel _languageModel;
    private readonly PromptTemplateManager _templates;

    public RagPipeline(ISemanticIndex semanticIndex, ILanguageModel languageModel)
    {
        _semanticIndex = semanticIndex;
        _languageModel = languageModel;
        _templates = new PromptTemplateManager();
    }

    /// <summary>
    /// Answer a question using RAG: retrieve relevant facts, then reason over them.
    /// </summary>
    public async Task<ReasoningResult> AnswerAsync(string question, ReasoningOptions? options = null, CancellationToken ct = default)
    {
        options ??= new ReasoningOptions();

        // Step 1: Retrieve relevant facts
        IReadOnlyList<SemanticSearchResult> facts;
        if (options.TimeRangeFrom.HasValue && options.TimeRangeTo.HasValue)
        {
            facts = await _semanticIndex.QueryInTimeRangeAsync(
                question,
                options.TimeRangeFrom.Value,
                options.TimeRangeTo.Value,
                options.MaxRetrievedFacts,
                ct);
        }
        else
        {
            facts = await _semanticIndex.QueryAsync(question, options.MaxRetrievedFacts, ct);
        }

        if (facts.Count == 0)
        {
            // No facts found, but still send to LLM with a general device context prompt
            var generalPrompt = $"You are a Windows device assistant. The user asked: \"{question}\"\n\n" +
                "No specific facts were retrieved from the knowledge graph for this query. " +
                "Respond helpfully. If the question is about the device, suggest they run a refresh to ingest device data first.";
            var generalAnswer = await _languageModel.GenerateAsync(generalPrompt, ct);

            return new ReasoningResult
            {
                Answer = generalAnswer,
                Sources = Array.Empty<SemanticSearchResult>(),
                TemplateName = options.TemplateName ?? "general",
                RetrievedFactCount = 0
            };
        }

        // Step 2: Build context from retrieved facts
        var context = BuildContext(facts);

        // Step 3: Select and render prompt template
        var templateName = options.TemplateName ?? InferTemplate(question);
        var prompt = _templates.Render(templateName, context);

        // Step 4: Generate response
        var answer = await _languageModel.GenerateAsync(prompt, ct);

        return new ReasoningResult
        {
            Answer = answer,
            Sources = facts,
            TemplateName = templateName,
            RetrievedFactCount = facts.Count,
            ContextTokenEstimate = context.Length / 4 // Rough token estimate
        };
    }

    /// <summary>
    /// Generate a response with a specific template and explicit context (no retrieval).
    /// </summary>
    public async Task<ReasoningResult> ReasonOverContextAsync(string templateName, string context, CancellationToken ct = default)
    {
        var prompt = _templates.Render(templateName, context);
        var answer = await _languageModel.GenerateAsync(prompt, ct);

        return new ReasoningResult
        {
            Answer = answer,
            Sources = Array.Empty<SemanticSearchResult>(),
            TemplateName = templateName,
            RetrievedFactCount = 0,
            ContextTokenEstimate = context.Length / 4
        };
    }

    private static string BuildContext(IReadOnlyList<SemanticSearchResult> facts)
    {
        var lines = facts.Select((f, i) =>
            $"[{i + 1}] ({f.ObservedAt:yyyy-MM-dd HH:mm}) {f.FactText}");
        return string.Join("\n", lines);
    }

    private static string InferTemplate(string question)
    {
        var lower = question.ToLowerInvariant();

        if (lower.Contains("health") || lower.Contains("how is") || lower.Contains("status"))
            return "SummarizeHealth";

        if (lower.Contains("fail") || lower.Contains("error") || lower.Contains("why did"))
            return "ExplainFailure";

        if (lower.Contains("safe") || lower.Contains("update") || lower.Contains("risk") || lower.Contains("ready"))
            return "PredictRisk";

        if (lower.Contains("what changed") || lower.Contains("happened") || lower.Contains("timeline") || lower.Contains("history"))
            return "NarrateTimeline";

        if (lower.Contains("diagram") || lower.Contains("servicing") || lower.Contains("pipeline") || lower.Contains("mermaid"))
            return "GenerateDiagram";

        // Default to health summary
        return "SummarizeHealth";
    }
}

/// <summary>
/// Options for controlling the RAG reasoning pipeline.
/// </summary>
public sealed class ReasoningOptions
{
    public string? TemplateName { get; init; }
    public int MaxRetrievedFacts { get; init; } = 15;
    public DateTimeOffset? TimeRangeFrom { get; init; }
    public DateTimeOffset? TimeRangeTo { get; init; }
}

/// <summary>
/// Result from the reasoning pipeline, including answer, sources, and metadata.
/// </summary>
public sealed class ReasoningResult
{
    public required string Answer { get; init; }
    public required IReadOnlyList<SemanticSearchResult> Sources { get; init; }
    public string? TemplateName { get; init; }
    public int RetrievedFactCount { get; init; }
    public int ContextTokenEstimate { get; init; }
}
