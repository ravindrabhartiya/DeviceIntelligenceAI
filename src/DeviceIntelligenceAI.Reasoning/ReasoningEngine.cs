using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Schema;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;

namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Top-level reasoning engine. Orchestrates the RAG pipeline, insight cache,
/// and graph queries to answer questions about the device.
/// This is the entry point for the MCP server tools and App Actions.
/// </summary>
public sealed class ReasoningEngine
{
    private readonly RagPipeline _rag;
    private readonly InsightCache _cache;
    private readonly GraphStore _graphStore;
    private readonly GraphQuery _graphQuery;

    public ReasoningEngine(ISemanticIndex semanticIndex, ILanguageModel languageModel, GraphStore graphStore)
    {
        _rag = new RagPipeline(semanticIndex, languageModel);
        _cache = new InsightCache();
        _graphStore = graphStore;
        _graphQuery = new GraphQuery(graphStore);
    }

    /// <summary>
    /// Answer any natural language question about the device.
    /// Uses cache if available, otherwise runs RAG pipeline.
    /// </summary>
    public async Task<ReasoningResult> QueryAsync(string question, CancellationToken ct = default)
    {
        var cacheKey = $"query:{question.ToLowerInvariant().Trim()}";
        var cached = _cache.Get(cacheKey);
        if (cached != null) return cached.Result;

        var result = await _rag.AnswerAsync(question, ct: ct);
        _cache.Store(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Get a health summary of the device.
    /// </summary>
    public async Task<ReasoningResult> GetHealthSummaryAsync(CancellationToken ct = default)
    {
        var cached = _cache.Get("health-summary");
        if (cached != null) return cached.Result;

        var result = await _rag.AnswerAsync("Summarize the current device health state", new ReasoningOptions
        {
            TemplateName = "SummarizeHealth",
            MaxRetrievedFacts = 20
        }, ct);

        _cache.Store("health-summary", result);
        return result;
    }

    /// <summary>
    /// Explain the most recent update failure.
    /// </summary>
    public async Task<ReasoningResult> ExplainUpdateFailureAsync(string? kbId = null, CancellationToken ct = default)
    {
        var question = kbId != null
            ? $"Why did update {kbId} fail? What was the error and how to fix it?"
            : "Why did the most recent Windows update fail? What was the error and how to fix it?";

        var result = await _rag.AnswerAsync(question, new ReasoningOptions
        {
            TemplateName = "ExplainFailure",
            MaxRetrievedFacts = 15
        }, ct);

        return result;
    }

    /// <summary>
    /// Assess whether it's safe to update.
    /// </summary>
    public async Task<ReasoningResult> PredictUpdateRiskAsync(CancellationToken ct = default)
    {
        var cached = _cache.Get("update-risk");
        if (cached != null) return cached.Result;

        var result = await _rag.AnswerAsync(
            "Is it safe to install Windows updates right now? What are the risks?",
            new ReasoningOptions
            {
                TemplateName = "PredictRisk",
                MaxRetrievedFacts = 20
            }, ct);

        _cache.Store("update-risk", result);
        return result;
    }

    /// <summary>
    /// Narrate what changed on the device over a time period.
    /// </summary>
    public async Task<ReasoningResult> NarrateTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        from ??= DateTimeOffset.UtcNow.AddDays(-7);
        to ??= DateTimeOffset.UtcNow;

        var result = await _rag.AnswerAsync(
            $"What happened on this device between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}?",
            new ReasoningOptions
            {
                TemplateName = "NarrateTimeline",
                MaxRetrievedFacts = 25,
                TimeRangeFrom = from,
                TimeRangeTo = to
            }, ct);

        return result;
    }

    /// <summary>
    /// Generate a servicing pipeline diagram.
    /// </summary>
    public async Task<ReasoningResult> GenerateServicingDiagramAsync(CancellationToken ct = default)
    {
        var result = await _rag.AnswerAsync(
            "Show the current Windows servicing pipeline state as a Mermaid diagram",
            new ReasoningOptions
            {
                TemplateName = "GenerateDiagram",
                MaxRetrievedFacts = 15
            }, ct);

        return result;
    }

    /// <summary>
    /// Get the causal chain for a specific failure, enhanced with NL explanation.
    /// </summary>
    public async Task<ReasoningResult> ExplainCausalChainAsync(string failureEntityId, CancellationToken ct = default)
    {
        var chain = _graphQuery.GetCausalChain(failureEntityId);
        if (chain.Count == 0)
        {
            return new ReasoningResult
            {
                Answer = "No causal chain found for this failure. There isn't enough temporal or module-matching evidence to identify a root cause.",
                Sources = Array.Empty<SemanticSearchResult>(),
                TemplateName = "ExplainFailure",
                RetrievedFactCount = 0
            };
        }

        // Build context from the causal chain
        var context = string.Join("\n", chain.Select((link, i) =>
            $"[{i + 1}] {link.Effect.Label} was possibly caused by {link.Cause.Label} " +
            $"(confidence: {link.Edge.Confidence:P0}, reason: {link.Edge.Properties.GetValueOrDefault("reason", "unknown")})"));

        var result = await _rag.ReasonOverContextAsync("ExplainFailure", context, ct);
        return result;
    }

    /// <summary>
    /// Invalidate the insight cache (e.g., after new data ingestion).
    /// </summary>
    public void InvalidateCache()
    {
        _cache.InvalidateAll();
    }

    /// <summary>
    /// Get all currently cached insights.
    /// </summary>
    public IReadOnlyList<CachedInsight> GetCachedInsights() => _cache.GetAll();
}
