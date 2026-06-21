using DeviceIntelligenceAI.Graph;

namespace DeviceIntelligenceAI.Ingestion.SemanticIndex;

/// <summary>
/// Connects the graph store to the semantic index.
/// Reads unindexed facts from the graph, indexes them semantically,
/// and marks them as indexed in the graph store.
/// </summary>
public sealed class SemanticIndexer
{
    private readonly GraphStore _graphStore;
    private readonly ISemanticIndex _semanticIndex;

    public SemanticIndexer(GraphStore graphStore, ISemanticIndex semanticIndex)
    {
        _graphStore = graphStore;
        _semanticIndex = semanticIndex;
    }

    /// <summary>
    /// Index all unindexed facts from the graph store into the semantic index.
    /// Returns the number of facts indexed.
    /// </summary>
    public async Task<int> IndexPendingFactsAsync(int batchSize = 100, CancellationToken ct = default)
    {
        var unindexed = _graphStore.GetUnindexedFacts(batchSize);
        if (unindexed.Count == 0) return 0;

        var facts = unindexed.Select(f => new SemanticFact(f.Id, f.FactText, f.ObservedAt, f.EntityId)).ToList();
        await _semanticIndex.IndexFactsBatchAsync(facts, ct);

        _graphStore.MarkFactsIndexed(unindexed.Select(f => f.Id));
        return unindexed.Count;
    }

    /// <summary>
    /// Continuously index pending facts until none remain.
    /// </summary>
    public async Task<int> IndexAllPendingAsync(int batchSize = 100, CancellationToken ct = default)
    {
        int total = 0;
        int batch;
        do
        {
            batch = await IndexPendingFactsAsync(batchSize, ct);
            total += batch;
        } while (batch > 0 && !ct.IsCancellationRequested);

        return total;
    }

    /// <summary>
    /// Rebuild the in-memory semantic index from all persisted graph facts, independent of
    /// the per-fact "indexed" flag. The semantic index is not durable across process
    /// restarts, so this must run on startup; otherwise retrieval returns no facts even
    /// though the graph is fully populated. Indexing is idempotent for both backends.
    /// Returns the number of facts rehydrated.
    /// </summary>
    public async Task<int> RehydrateAsync(CancellationToken ct = default)
    {
        var allFacts = _graphStore.GetAllFacts();
        if (allFacts.Count == 0) return 0;

        var facts = allFacts.Select(f => new SemanticFact(f.Id, f.FactText, f.ObservedAt, f.EntityId)).ToList();
        await _semanticIndex.IndexFactsBatchAsync(facts, ct);
        return facts.Count;
    }

    /// <summary>
    /// Query the semantic index with a natural language question.
    /// </summary>
    public Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        return _semanticIndex.QueryAsync(query, maxResults, ct);
    }

    /// <summary>
    /// Query with temporal constraints.
    /// </summary>
    public Task<IReadOnlyList<SemanticSearchResult>> SearchInTimeRangeAsync(string query, DateTimeOffset from, DateTimeOffset to, int maxResults = 10, CancellationToken ct = default)
    {
        return _semanticIndex.QueryInTimeRangeAsync(query, from, to, maxResults, ct);
    }

    /// <summary>
    /// Get statistics about the semantic index.
    /// </summary>
    public async Task<SemanticIndexStats> GetStatsAsync(CancellationToken ct = default)
    {
        var graphStats = _graphStore.GetStats();
        var indexedCount = await _semanticIndex.GetIndexedCountAsync(ct);
        var unindexed = _graphStore.GetUnindexedFacts(1);

        return new SemanticIndexStats
        {
            TotalFacts = graphStats.FactCount,
            IndexedFacts = indexedCount,
            PendingFacts = graphStats.FactCount - indexedCount,
            TotalEntities = graphStats.EntityCount,
            TotalEdges = graphStats.EdgeCount
        };
    }
}

public sealed class SemanticIndexStats
{
    public int TotalFacts { get; init; }
    public int IndexedFacts { get; init; }
    public int PendingFacts { get; init; }
    public int TotalEntities { get; init; }
    public int TotalEdges { get; init; }
}
