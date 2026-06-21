using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;

namespace DeviceIntelligenceAI.Ingestion.Tests;

public class SemanticIndexerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GraphStore _store;
    private readonly LocalSemanticIndex _semanticIndex;
    private readonly SemanticIndexer _indexer;

    public SemanticIndexerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-semantic-{Guid.NewGuid():N}.db");
        _store = new GraphStore(_dbPath);
        _semanticIndex = new LocalSemanticIndex();
        _indexer = new SemanticIndexer(_store, _semanticIndex);
    }

    [Fact]
    public async Task IndexPendingFacts_IndexesAndMarksComplete()
    {
        // Build a device twin into the graph (which generates facts)
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwinJson);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        // Verify facts are pending
        var unindexed = _store.GetUnindexedFacts();
        Assert.True(unindexed.Count > 0);

        // Index them
        var indexed = await _indexer.IndexPendingFactsAsync();
        Assert.Equal(unindexed.Count, indexed);

        // Verify none remain unindexed
        var remaining = _store.GetUnindexedFacts();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task Search_ReturnsRelevantGraphFacts()
    {
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwinJson);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        await _indexer.IndexAllPendingAsync();

        var results = await _indexer.SearchAsync("update failed");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.FactText.Contains("KB5039212") || r.FactText.Contains("failed"));
    }

    [Fact]
    public async Task Search_FindsDriverInfo()
    {
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwinJson);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        await _indexer.IndexAllPendingAsync();

        var results = await _indexer.SearchAsync("Intel graphics driver");
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.FactText.Contains("Intel UHD"));
    }

    [Fact]
    public async Task GetStats_ReturnsAccurateCounts()
    {
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwinJson);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        var statsBefore = await _indexer.GetStatsAsync();
        Assert.True(statsBefore.PendingFacts > 0);
        Assert.Equal(0, statsBefore.IndexedFacts);

        await _indexer.IndexAllPendingAsync();

        var statsAfter = await _indexer.GetStatsAsync();
        Assert.Equal(0, statsAfter.PendingFacts);
        Assert.True(statsAfter.IndexedFacts > 0);
        Assert.Equal(statsBefore.TotalFacts, statsAfter.IndexedFacts);
    }

    [Fact]
    public async Task RehydrateAsync_MakesPersistedFactsSearchableOnFreshIndex()
    {
        // Simulate a restart: facts are persisted in the graph and already marked indexed,
        // but the in-memory semantic index is brand new (empty).
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwinJson);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);
        await _indexer.IndexAllPendingAsync();
        _store.MarkFactsIndexed(_store.GetAllFacts().Select(f => f.Id)); // ensure all flagged indexed

        // New process: fresh empty index + indexer over the same persisted graph.
        using var freshIndex = new LocalSemanticIndex();
        var freshIndexer = new SemanticIndexer(_store, freshIndex);

        // IndexAllPendingAsync alone would index nothing (everything is already indexed=1).
        var pending = await freshIndexer.IndexAllPendingAsync();
        Assert.Equal(0, pending);
        Assert.Empty(await freshIndexer.SearchAsync("update failed"));

        // Rehydration rebuilds the in-memory index from persisted facts.
        var rehydrated = await freshIndexer.RehydrateAsync();
        Assert.True(rehydrated > 0);

        var results = await freshIndexer.SearchAsync("update failed");
        Assert.NotEmpty(results);
    }

    private const string SampleTwinJson = """
    {
        "inventory": {
            "computerName": "DEVPC",
            "architecture": "x64",
            "manufacturer": "Dell",
            "model": "XPS 15",
            "osBuild": "26100.2"
        },
        "os": {
            "buildNumber": "26100.2",
            "version": "24H2",
            "edition": "Enterprise"
        },
        "updates": {
            "installedUpdates": [
                {
                    "kbId": "KB5034441",
                    "title": "Recovery Environment Update",
                    "state": "installed",
                    "installedOn": "2024-06-10"
                },
                {
                    "kbId": "KB5039212",
                    "title": "June Cumulative Update",
                    "state": "failed",
                    "installedOn": null
                }
            ],
            "recentFailures": []
        },
        "drivers": [
            {
                "friendlyName": "Intel UHD Graphics 770",
                "version": "31.0.101.5186",
                "infName": "iigd_dch.inf",
                "provider": "Intel Corporation",
                "deviceClass": "Display"
            },
            {
                "friendlyName": "Realtek High Definition Audio",
                "version": "6.0.9600.17",
                "infName": "hdaudio.inf",
                "provider": "Realtek",
                "deviceClass": "AudioEndpoint"
            }
        ],
        "reliability": {
            "applicationCrashes": [],
            "systemErrors": [],
            "bugchecks": [],
            "unexpectedShutdowns": []
        },
        "performance": {
            "cpuUsagePercent": "22",
            "memoryAvailableGb": "12.4",
            "diskUtilizationPercent": "30"
        },
        "security": {
            "firewallEnabled": "true",
            "antivirusStatus": "active",
            "secureBootEnabled": "true",
            "bitlockerStatus": "enabled",
            "tpmVersion": "2.0"
        }
    }
    """;

    public void Dispose()
    {
        _semanticIndex.Dispose();
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
