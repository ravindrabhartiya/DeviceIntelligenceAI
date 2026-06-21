using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Graph.Tests;

public class GraphStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GraphStore _store;

    public GraphStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-graph-{Guid.NewGuid():N}.db");
        _store = new GraphStore(_dbPath);
    }

    [Fact]
    public void UpsertEntity_CreatesAndUpdates()
    {
        var entity = new GraphEntity
        {
            Id = "device:testpc",
            Type = EntityTypes.Device,
            Label = "TestPC",
            FirstSeen = DateTimeOffset.UtcNow.AddDays(-1),
            LastSeen = DateTimeOffset.UtcNow,
            Properties = new() { ["hostname"] = "TESTPC" }
        };

        _store.UpsertEntity(entity);
        var retrieved = _store.GetEntity("device:testpc");

        Assert.NotNull(retrieved);
        Assert.Equal("TestPC", retrieved.Label);
        Assert.Equal("TESTPC", retrieved.Properties["hostname"]);
    }

    [Fact]
    public void UpsertEntity_UpdatesLastSeen()
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new GraphEntity
        {
            Id = "device:testpc",
            Type = EntityTypes.Device,
            Label = "TestPC",
            FirstSeen = now.AddDays(-2),
            LastSeen = now.AddDays(-1)
        };
        _store.UpsertEntity(entity);

        var updated = new GraphEntity
        {
            Id = "device:testpc",
            Type = EntityTypes.Device,
            Label = "TestPC-Updated",
            FirstSeen = now.AddDays(-2),
            LastSeen = now
        };
        _store.UpsertEntity(updated);

        var retrieved = _store.GetEntity("device:testpc");
        Assert.Equal("TestPC-Updated", retrieved!.Label);
    }

    [Fact]
    public void GetEntitiesByType_FiltersCorrectly()
    {
        _store.UpsertEntity(new GraphEntity { Id = "d1", Type = EntityTypes.Driver, Label = "Driver1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.UpsertEntity(new GraphEntity { Id = "d2", Type = EntityTypes.Driver, Label = "Driver2", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.UpsertEntity(new GraphEntity { Id = "u1", Type = EntityTypes.Update, Label = "Update1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });

        var drivers = _store.GetEntitiesByType(EntityTypes.Driver);
        Assert.Equal(2, drivers.Count);
        Assert.All(drivers, d => Assert.Equal(EntityTypes.Driver, d.Type));
    }

    [Fact]
    public void Edges_CreateAndRetrieve()
    {
        _store.UpsertEntity(new GraphEntity { Id = "update:KB1", Type = EntityTypes.Update, Label = "KB1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.UpsertEntity(new GraphEntity { Id = "device:pc", Type = EntityTypes.Device, Label = "PC", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });

        _store.UpsertEdge(new GraphEdge
        {
            Id = "e1",
            SourceId = "update:KB1",
            TargetId = "device:pc",
            Type = EdgeTypes.Installed,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var outgoing = _store.GetEdgesFrom("update:KB1");
        Assert.Single(outgoing);
        Assert.Equal(EdgeTypes.Installed, outgoing[0].Type);

        var incoming = _store.GetEdgesTo("device:pc");
        Assert.Single(incoming);
    }

    [Fact]
    public void GetNeighborhood_ReturnsConnectedSubgraph()
    {
        var now = DateTimeOffset.UtcNow;
        _store.UpsertEntity(new GraphEntity { Id = "A", Type = EntityTypes.Device, Label = "A", FirstSeen = now, LastSeen = now });
        _store.UpsertEntity(new GraphEntity { Id = "B", Type = EntityTypes.Update, Label = "B", FirstSeen = now, LastSeen = now });
        _store.UpsertEntity(new GraphEntity { Id = "C", Type = EntityTypes.Failure, Label = "C", FirstSeen = now, LastSeen = now });

        _store.UpsertEdge(new GraphEdge { Id = "AB", SourceId = "A", TargetId = "B", Type = EdgeTypes.Installed, CreatedAt = now });
        _store.UpsertEdge(new GraphEdge { Id = "BC", SourceId = "B", TargetId = "C", Type = EdgeTypes.CausedBy, CreatedAt = now });

        var (entities, edges) = _store.GetNeighborhood("A", depth: 2);
        Assert.Equal(3, entities.Count);
        Assert.Equal(2, edges.Count);
    }

    [Fact]
    public void Facts_InsertAndRetrieveUnindexed()
    {
        _store.UpsertEntity(new GraphEntity { Id = "e1", Type = EntityTypes.Update, Label = "U1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.InsertFact("f1", "e1", "Update KB123 was installed", DateTimeOffset.UtcNow);
        _store.InsertFact("f2", "e1", "Update KB456 failed", DateTimeOffset.UtcNow);

        var unindexed = _store.GetUnindexedFacts();
        Assert.Equal(2, unindexed.Count);

        _store.MarkFactsIndexed(new[] { "f1" });
        unindexed = _store.GetUnindexedFacts();
        Assert.Single(unindexed);
    }

    [Fact]
    public void Stats_ReturnsCorrectCounts()
    {
        _store.UpsertEntity(new GraphEntity { Id = "e1", Type = EntityTypes.Device, Label = "D", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.UpsertEntity(new GraphEntity { Id = "e2", Type = EntityTypes.Update, Label = "U", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.UpsertEdge(new GraphEdge { Id = "edge1", SourceId = "e1", TargetId = "e2", Type = EdgeTypes.Installed, CreatedAt = DateTimeOffset.UtcNow });
        _store.InsertFact("fact1", "e1", "Test fact", DateTimeOffset.UtcNow);

        var (entities, edges, facts) = _store.GetStats();
        Assert.Equal(2, entities);
        Assert.Equal(1, edges);
        Assert.Equal(1, facts);
    }

    [Fact]
    public void PruneOlderThan_RemovesOldData()
    {
        var old = DateTimeOffset.UtcNow.AddDays(-60);
        var recent = DateTimeOffset.UtcNow;

        _store.UpsertEntity(new GraphEntity { Id = "old1", Type = EntityTypes.Update, Label = "Old", FirstSeen = old, LastSeen = old });
        _store.UpsertEntity(new GraphEntity { Id = "new1", Type = EntityTypes.Update, Label = "New", FirstSeen = recent, LastSeen = recent });

        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        _store.PruneOlderThan(cutoff);

        Assert.Null(_store.GetEntity("old1"));
        Assert.NotNull(_store.GetEntity("new1"));
    }

    [Fact]
    public void GetAllFacts_ReturnsFactsRegardlessOfIndexedFlag()
    {
        _store.UpsertEntity(new GraphEntity { Id = "e1", Type = EntityTypes.Update, Label = "U1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });
        _store.InsertFact("f1", "e1", "Update KB123 was installed", DateTimeOffset.UtcNow);
        _store.InsertFact("f2", "e1", "Update KB456 failed", DateTimeOffset.UtcNow);

        // Mark one indexed; GetAllFacts must still return both (unlike GetUnindexedFacts).
        _store.MarkFactsIndexed(new[] { "f1" });

        var all = _store.GetAllFacts();
        Assert.Equal(2, all.Count);
        Assert.Single(_store.GetUnindexedFacts());
    }

    [Fact]
    public void InsertFact_UpsertsObservedAtOnDuplicateId()
    {
        _store.UpsertEntity(new GraphEntity { Id = "e1", Type = EntityTypes.Update, Label = "U1", FirstSeen = DateTimeOffset.UtcNow, LastSeen = DateTimeOffset.UtcNow });

        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var second = DateTimeOffset.UtcNow;
        _store.InsertFact("fdup", "e1", "Same fact text", first);
        _store.InsertFact("fdup", "e1", "Same fact text", second);

        // Re-inserting the same id must not create a second row...
        var all = _store.GetAllFacts();
        Assert.Single(all);
        // ...and must refresh observed_at to the latest observation.
        Assert.Equal(second.ToUnixTimeSeconds(), all[0].ObservedAt.ToUnixTimeSeconds());
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
