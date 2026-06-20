namespace DeviceIntelligenceAI.Ingestion.SemanticIndex;

/// <summary>
/// Abstraction over semantic indexing and retrieval.
/// Implementations: WindowsSemanticIndex (AppContentIndexer) and LocalSemanticIndex (keyword fallback).
/// </summary>
public interface ISemanticIndex : IDisposable
{
    /// <summary>
    /// Index a fact with its content and metadata.
    /// </summary>
    Task IndexFactAsync(string factId, string factText, DateTimeOffset observedAt, CancellationToken ct = default);

    /// <summary>
    /// Index multiple facts in batch.
    /// </summary>
    Task IndexFactsBatchAsync(IEnumerable<SemanticFact> facts, CancellationToken ct = default);

    /// <summary>
    /// Query the index with a natural language question.
    /// Returns the most relevant facts ranked by similarity.
    /// </summary>
    Task<IReadOnlyList<SemanticSearchResult>> QueryAsync(string query, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// Query with a time range filter.
    /// </summary>
    Task<IReadOnlyList<SemanticSearchResult>> QueryInTimeRangeAsync(string query, DateTimeOffset from, DateTimeOffset to, int maxResults = 10, CancellationToken ct = default);

    /// <summary>
    /// Remove a fact from the index.
    /// </summary>
    Task RemoveFactAsync(string factId, CancellationToken ct = default);

    /// <summary>
    /// Get the number of indexed facts.
    /// </summary>
    Task<int> GetIndexedCountAsync(CancellationToken ct = default);
}

/// <summary>
/// A fact to be indexed semantically.
/// </summary>
public sealed record SemanticFact(string Id, string Text, DateTimeOffset ObservedAt, string? EntityId = null);

/// <summary>
/// A result from semantic search.
/// </summary>
public sealed class SemanticSearchResult
{
    public required string FactId { get; init; }
    public required string FactText { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public double Score { get; init; }
    public int MatchOffset { get; init; }
    public int MatchLength { get; init; }
}
