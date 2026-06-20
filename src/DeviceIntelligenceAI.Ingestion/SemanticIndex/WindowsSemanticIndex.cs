using System.Runtime.InteropServices;

namespace DeviceIntelligenceAI.Ingestion.SemanticIndex;

/// <summary>
/// Semantic index implementation using the Windows App SDK AppContentIndexer API.
/// Requires: packaged app (MSIX) with systemaimodels capability, Copilot+ PC.
/// 
/// This wraps Microsoft.Windows.Search.AppContentIndex.AppContentIndexer
/// and handles text indexing + semantic querying via the on-device embedding model.
/// </summary>
public sealed class WindowsSemanticIndex : ISemanticIndex
{
    private readonly dynamic _indexer;
    private readonly Dictionary<string, (string Text, DateTimeOffset ObservedAt)> _factStore = new();
    private readonly string _indexName;

    private WindowsSemanticIndex(dynamic indexer, string indexName)
    {
        _indexer = indexer;
        _indexName = indexName;
    }

    /// <summary>
    /// Create a WindowsSemanticIndex. Throws PlatformNotSupportedException if APIs unavailable.
    /// </summary>
    public static WindowsSemanticIndex Create(string indexName = "device-intelligence-facts")
    {
        // Dynamically load the WinRT API to avoid hard compile-time dependency
        // This allows the project to compile on any machine, but only run on Copilot+ PCs
        var indexerType = Type.GetType("Microsoft.Windows.Search.AppContentIndex.AppContentIndexer, Microsoft.Windows.SDK.NET");
        if (indexerType == null)
        {
            throw new PlatformNotSupportedException(
                "Windows AppContentIndexer API not available. " +
                "Requires Windows App SDK 1.8+ on a Copilot+ PC with systemaimodels capability.");
        }

        var getOrCreateMethod = indexerType.GetMethod("GetOrCreateIndex");
        if (getOrCreateMethod == null)
        {
            throw new PlatformNotSupportedException("AppContentIndexer.GetOrCreateIndex method not found.");
        }

        dynamic result = getOrCreateMethod.Invoke(null, new object[] { indexName })!;
        if (!(bool)result.Succeeded)
        {
            throw new InvalidOperationException($"Failed to create/open index '{indexName}': {result.Status} - {result.ExtendedError}");
        }

        return new WindowsSemanticIndex(result.Indexer, indexName);
    }

    /// <summary>
    /// Check if the Windows Semantic Index APIs are available on this device.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            var indexerType = Type.GetType("Microsoft.Windows.Search.AppContentIndex.AppContentIndexer, Microsoft.Windows.SDK.NET");
            return indexerType != null;
        }
        catch
        {
            return false;
        }
    }

    public Task IndexFactAsync(string factId, string factText, DateTimeOffset observedAt, CancellationToken ct = default)
    {
        _factStore[factId] = (factText, observedAt);

        // Create indexable content: the text includes a timestamp prefix for temporal context
        var indexableText = $"[{observedAt:yyyy-MM-dd HH:mm}] {factText}";

        var contentType = Type.GetType("Microsoft.Windows.Search.AppContentIndex.AppManagedIndexableAppContent, Microsoft.Windows.SDK.NET")!;
        var createMethod = contentType.GetMethod("CreateFromString")!;
        dynamic content = createMethod.Invoke(null, new object[] { factId, indexableText })!;

        _indexer.AddOrUpdate(content);
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
        dynamic queryCursor = _indexer.CreateTextQuery(query);
        dynamic matches = queryCursor.GetNextMatches(maxResults);

        var results = new List<SemanticSearchResult>();
        foreach (dynamic match in matches)
        {
            string contentId = match.ContentId;
            if (_factStore.TryGetValue(contentId, out var stored))
            {
                int offset = 0;
                int length = stored.Text.Length;

                // Try to get offset/length from AppManagedTextQueryMatch
                try
                {
                    offset = (int)match.TextOffset;
                    length = (int)match.TextLength;
                }
                catch { }

                results.Add(new SemanticSearchResult
                {
                    FactId = contentId,
                    FactText = stored.Text,
                    ObservedAt = stored.ObservedAt,
                    Score = 1.0 - (results.Count * 0.1), // Approximate: results are pre-sorted by relevance
                    MatchOffset = offset,
                    MatchLength = length
                });
            }
        }

        return Task.FromResult<IReadOnlyList<SemanticSearchResult>>(results);
    }

    public async Task<IReadOnlyList<SemanticSearchResult>> QueryInTimeRangeAsync(string query, DateTimeOffset from, DateTimeOffset to, int maxResults = 10, CancellationToken ct = default)
    {
        // AppContentIndexer doesn't natively support time filtering,
        // so we query broadly and filter post-hoc
        var allResults = await QueryAsync(query, maxResults * 3, ct);
        return allResults
            .Where(r => r.ObservedAt >= from && r.ObservedAt <= to)
            .Take(maxResults)
            .ToList();
    }

    public Task RemoveFactAsync(string factId, CancellationToken ct = default)
    {
        _factStore.Remove(factId);
        _indexer.Remove(factId);
        return Task.CompletedTask;
    }

    public Task<int> GetIndexedCountAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_factStore.Count);
    }

    public void Dispose()
    {
        if (_indexer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
