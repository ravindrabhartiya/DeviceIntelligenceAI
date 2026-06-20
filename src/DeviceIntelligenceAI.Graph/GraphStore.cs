using System.Text.Json;
using DeviceIntelligenceAI.Graph.Models;
using Microsoft.Data.Sqlite;

namespace DeviceIntelligenceAI.Graph;

/// <summary>
/// SQLite-backed persistent store for the device knowledge graph.
/// </summary>
public sealed class GraphStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public GraphStore(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        var schema = GetEmbeddedSchema();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = schema;
        cmd.ExecuteNonQuery();

        // Enable WAL mode for better concurrent read performance
        using var walCmd = _connection.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        walCmd.ExecuteNonQuery();
    }

    private static string GetEmbeddedSchema()
    {
        var assembly = typeof(GraphStore).Assembly;
        var resourceName = "DeviceIntelligenceAI.Graph.Schema.GraphSchema.sql";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    #region Entity Operations

    public void UpsertEntity(GraphEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO entities (id, type, label, first_seen, last_seen, properties_json)
            VALUES (@id, @type, @label, @firstSeen, @lastSeen, @props)
            ON CONFLICT(id) DO UPDATE SET
                label = @label,
                last_seen = @lastSeen,
                properties_json = @props
            """;
        cmd.Parameters.AddWithValue("@id", entity.Id);
        cmd.Parameters.AddWithValue("@type", entity.Type);
        cmd.Parameters.AddWithValue("@label", entity.Label);
        cmd.Parameters.AddWithValue("@firstSeen", entity.FirstSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@lastSeen", entity.LastSeen.ToString("o"));
        cmd.Parameters.AddWithValue("@props", JsonSerializer.Serialize(entity.Properties, JsonOptions));
        cmd.ExecuteNonQuery();
    }

    public GraphEntity? GetEntity(string id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, label, first_seen, last_seen, properties_json FROM entities WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return ReadEntity(reader);
    }

    public IReadOnlyList<GraphEntity> GetEntitiesByType(string type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, type, label, first_seen, last_seen, properties_json FROM entities WHERE type = @type ORDER BY last_seen DESC";
        cmd.Parameters.AddWithValue("@type", type);

        var results = new List<GraphEntity>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEntity(reader));
        }
        return results;
    }

    public IReadOnlyList<GraphEntity> GetEntitiesInTimeRange(DateTimeOffset from, DateTimeOffset to)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, type, label, first_seen, last_seen, properties_json 
            FROM entities 
            WHERE last_seen >= @from AND first_seen <= @to
            ORDER BY last_seen DESC
            """;
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));

        var results = new List<GraphEntity>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEntity(reader));
        }
        return results;
    }

    #endregion

    #region Edge Operations

    public void UpsertEdge(GraphEdge edge)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO edges (id, source_id, target_id, type, created_at, confidence, properties_json)
            VALUES (@id, @sourceId, @targetId, @type, @createdAt, @confidence, @props)
            ON CONFLICT(id) DO UPDATE SET
                confidence = @confidence,
                properties_json = @props
            """;
        cmd.Parameters.AddWithValue("@id", edge.Id);
        cmd.Parameters.AddWithValue("@sourceId", edge.SourceId);
        cmd.Parameters.AddWithValue("@targetId", edge.TargetId);
        cmd.Parameters.AddWithValue("@type", edge.Type);
        cmd.Parameters.AddWithValue("@createdAt", edge.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@confidence", edge.Confidence);
        cmd.Parameters.AddWithValue("@props", JsonSerializer.Serialize(edge.Properties, JsonOptions));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<GraphEdge> GetEdgesFrom(string entityId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, target_id, type, created_at, confidence, properties_json FROM edges WHERE source_id = @id ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@id", entityId);

        var results = new List<GraphEdge>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEdge(reader));
        }
        return results;
    }

    public IReadOnlyList<GraphEdge> GetEdgesTo(string entityId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, target_id, type, created_at, confidence, properties_json FROM edges WHERE target_id = @id ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@id", entityId);

        var results = new List<GraphEdge>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEdge(reader));
        }
        return results;
    }

    public IReadOnlyList<GraphEdge> GetEdgesByType(string type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, source_id, target_id, type, created_at, confidence, properties_json FROM edges WHERE type = @type ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@type", type);

        var results = new List<GraphEdge>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(ReadEdge(reader));
        }
        return results;
    }

    #endregion

    #region Subgraph Traversal

    /// <summary>
    /// Get the neighborhood of an entity up to a specified depth.
    /// Returns all entities and edges reachable within the depth limit.
    /// </summary>
    public (IReadOnlyList<GraphEntity> Entities, IReadOnlyList<GraphEdge> Edges) GetNeighborhood(string centerId, int depth = 2)
    {
        var visitedEntities = new HashSet<string> { centerId };
        var allEdges = new List<GraphEdge>();
        var frontier = new HashSet<string> { centerId };

        for (int d = 0; d < depth; d++)
        {
            var nextFrontier = new HashSet<string>();
            foreach (var nodeId in frontier)
            {
                var outgoing = GetEdgesFrom(nodeId);
                var incoming = GetEdgesTo(nodeId);

                foreach (var edge in outgoing.Concat(incoming))
                {
                    allEdges.Add(edge);
                    var neighborId = edge.SourceId == nodeId ? edge.TargetId : edge.SourceId;
                    if (visitedEntities.Add(neighborId))
                    {
                        nextFrontier.Add(neighborId);
                    }
                }
            }
            frontier = nextFrontier;
            if (frontier.Count == 0) break;
        }

        var entities = visitedEntities
            .Select(GetEntity)
            .Where(e => e != null)
            .Cast<GraphEntity>()
            .ToList();

        return (entities, allEdges.DistinctBy(e => e.Id).ToList());
    }

    #endregion

    #region Fact Operations

    public void InsertFact(string id, string entityId, string factText, DateTimeOffset observedAt, string? snapshotId = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO facts (id, entity_id, fact_text, observed_at, snapshot_id, indexed)
            VALUES (@id, @entityId, @factText, @observedAt, @snapshotId, 0)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@entityId", entityId);
        cmd.Parameters.AddWithValue("@factText", factText);
        cmd.Parameters.AddWithValue("@observedAt", observedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@snapshotId", snapshotId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(string Id, string EntityId, string FactText, DateTimeOffset ObservedAt)> GetUnindexedFacts(int limit = 100)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, entity_id, fact_text, observed_at FROM facts WHERE indexed = 0 ORDER BY observed_at DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<(string, string, string, DateTimeOffset)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))
            ));
        }
        return results;
    }

    public void MarkFactsIndexed(IEnumerable<string> factIds)
    {
        using var transaction = _connection.BeginTransaction();
        foreach (var id in factIds)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE facts SET indexed = 1 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    #endregion

    #region Snapshot Operations

    public void InsertSnapshot(GraphSnapshot snapshot)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO snapshots (id, timestamp, entity_count, edge_count, source_snapshot_id)
            VALUES (@id, @ts, @entityCount, @edgeCount, @sourceId)
            """;
        cmd.Parameters.AddWithValue("@id", snapshot.Id);
        cmd.Parameters.AddWithValue("@ts", snapshot.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@entityCount", snapshot.EntityCount);
        cmd.Parameters.AddWithValue("@edgeCount", snapshot.EdgeCount);
        cmd.Parameters.AddWithValue("@sourceId", snapshot.SourceSnapshotId ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region Statistics

    public (int EntityCount, int EdgeCount, int FactCount) GetStats()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT 
                (SELECT COUNT(*) FROM entities),
                (SELECT COUNT(*) FROM edges),
                (SELECT COUNT(*) FROM facts)
            """;
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    /// <summary>
    /// Remove entities and edges older than the retention window.
    /// </summary>
    public int PruneOlderThan(DateTimeOffset cutoff)
    {
        using var transaction = _connection.BeginTransaction();

        using var edgeCmd = _connection.CreateCommand();
        edgeCmd.CommandText = "DELETE FROM edges WHERE created_at < @cutoff";
        edgeCmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
        var edgesRemoved = edgeCmd.ExecuteNonQuery();

        using var factCmd = _connection.CreateCommand();
        factCmd.CommandText = "DELETE FROM facts WHERE observed_at < @cutoff";
        factCmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
        factCmd.ExecuteNonQuery();

        using var entityCmd = _connection.CreateCommand();
        entityCmd.CommandText = "DELETE FROM entities WHERE last_seen < @cutoff";
        entityCmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
        var entitiesRemoved = entityCmd.ExecuteNonQuery();

        transaction.Commit();
        return entitiesRemoved + edgesRemoved;
    }

    #endregion

    #region Helpers

    private static GraphEntity ReadEntity(SqliteDataReader reader)
    {
        return new GraphEntity
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Label = reader.GetString(2),
            FirstSeen = DateTimeOffset.Parse(reader.GetString(3)),
            LastSeen = DateTimeOffset.Parse(reader.GetString(4)),
            Properties = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(5)) ?? new()
        };
    }

    private static GraphEdge ReadEdge(SqliteDataReader reader)
    {
        return new GraphEdge
        {
            Id = reader.GetString(0),
            SourceId = reader.GetString(1),
            TargetId = reader.GetString(2),
            Type = reader.GetString(3),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(4)),
            Confidence = reader.GetDouble(5),
            Properties = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6)) ?? new()
        };
    }

    #endregion

    public void Dispose()
    {
        _connection.Dispose();
    }
}
