using System.Collections.Concurrent;

namespace DeviceIntelligenceAI.Ingestion.SemanticIndex;

/// <summary>
/// Local keyword-based semantic index fallback for development and testing.
/// Uses TF-IDF-like scoring with keyword overlap when the Windows AppContentIndexer
/// is not available (non-Copilot+ PCs, unpackaged apps, CI environments).
/// </summary>
public sealed class LocalSemanticIndex : ISemanticIndex
{
    private readonly ConcurrentDictionary<string, IndexedFact> _facts = new();

    public Task IndexFactAsync(string factId, string factText, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        var tokens = Tokenize(factText);
        _facts[factId] = new IndexedFact(factId, factText, observedAt, tokens);
        return Task.CompletedTask;
    }

    public async Task IndexFactsBatchAsync(IEnumerable<SemanticFact> facts, CancellationToken ct = default)
    {
        foreach (var fact in facts)
        {
            ct.ThrowIfCancellationRequested();
            await IndexFactAsync(fact.Id, fact.Text, fact.ObservedAt, ct);
        }
    }

    public Task<IReadOnlyList<SemanticSearchResult>> QueryAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return Task.FromResult<IReadOnlyList<SemanticSearchResult>>(Array.Empty<SemanticSearchResult>());

        var scored = _facts.Values
            .Select(f => (Fact: f, Score: ComputeScore(queryTokens, f.Tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SemanticSearchResult
            {
                FactId = x.Fact.Id,
                FactText = x.Fact.Text,
                ObservedAt = x.Fact.ObservedAt,
                Score = x.Score,
                MatchOffset = 0,
                MatchLength = x.Fact.Text.Length
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticSearchResult>>(scored);
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> QueryInTimeRangeAsync(string query, DateTimeOffset from, DateTimeOffset to, int maxResults = 10, CancellationToken ct = default)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
            return Array.Empty<SemanticSearchResult>();

        var scored = _facts.Values
            .Where(f => f.ObservedAt >= from && f.ObservedAt <= to)
            .Select(f => (Fact: f, Score: ComputeScore(queryTokens, f.Tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(maxResults)
            .Select(x => new SemanticSearchResult
            {
                FactId = x.Fact.Id,
                FactText = x.Fact.Text,
                ObservedAt = x.Fact.ObservedAt,
                Score = x.Score,
                MatchOffset = 0,
                MatchLength = x.Fact.Text.Length
            })
            .ToList();

        return scored;
    }

    public Task RemoveFactAsync(string factId, CancellationToken ct = default)
    {
        _facts.TryRemove(factId, out _);
        return Task.CompletedTask;
    }

    public Task<int> GetIndexedCountAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_facts.Count);
    }

    public void Dispose() { }

    #region Scoring

    private static double ComputeScore(HashSet<string> queryTokens, HashSet<string> factTokens)
    {
        if (factTokens.Count == 0) return 0;

        var intersection = queryTokens.Intersect(factTokens).Count();
        if (intersection == 0) return 0;

        // Jaccard-like similarity with query-side bias
        double queryCoverage = (double)intersection / queryTokens.Count;
        double factCoverage = (double)intersection / factTokens.Count;

        // Weighted: favor high query coverage (precision) slightly over recall
        return queryCoverage * 0.7 + factCoverage * 0.3;
    }

    private static HashSet<string> Tokenize(string text)
    {
        // Simple tokenization: lowercase, split on non-alphanumeric, remove stopwords
        var tokens = text.ToLowerInvariant()
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToHashSet();
        return tokens;
    }

    private static readonly char[] Separators = { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_', '=' };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "to", "for", "of", "with", "was", "were", "are", "been",
        "has", "have", "had", "this", "that", "from", "not", "its"
    };

    #endregion

    private sealed record IndexedFact(string Id, string Text, DateTimeOffset ObservedAt, HashSet<string> Tokens);
}
