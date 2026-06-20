using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Graph;

/// <summary>
/// High-level query interface for the knowledge graph.
/// Provides typed traversal and temporal query capabilities.
/// </summary>
public sealed class GraphQuery
{
    private readonly GraphStore _store;

    public GraphQuery(GraphStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Get all causal chains leading to a given entity (recursive backward traversal).
    /// </summary>
    public IReadOnlyList<CausalChainLink> GetCausalChain(string entityId, int maxDepth = 5)
    {
        var chain = new List<CausalChainLink>();
        var visited = new HashSet<string>();
        TraverseCauses(entityId, chain, visited, 0, maxDepth);
        return chain;
    }

    private void TraverseCauses(string entityId, List<CausalChainLink> chain, HashSet<string> visited, int depth, int maxDepth)
    {
        if (depth >= maxDepth || !visited.Add(entityId)) return;

        var edges = _store.GetEdgesFrom(entityId)
            .Where(e => e.Type == EdgeTypes.CausedBy)
            .OrderByDescending(e => e.Confidence);

        foreach (var edge in edges)
        {
            var cause = _store.GetEntity(edge.TargetId);
            if (cause == null) continue;

            chain.Add(new CausalChainLink
            {
                Effect = _store.GetEntity(entityId)!,
                Cause = cause,
                Edge = edge,
                Depth = depth
            });

            TraverseCauses(edge.TargetId, chain, visited, depth + 1, maxDepth);
        }
    }

    /// <summary>
    /// Find all entities that changed within a time window.
    /// </summary>
    public IReadOnlyList<GraphEntity> GetChangesInWindow(DateTimeOffset from, DateTimeOffset to)
    {
        return _store.GetEntitiesInTimeRange(from, to);
    }

    /// <summary>
    /// Get the most recent failures and their potential causes.
    /// </summary>
    public IReadOnlyList<FailureWithCauses> GetRecentFailuresWithCauses(int limit = 10)
    {
        var failures = _store.GetEntitiesByType(EntityTypes.Failure)
            .Take(limit)
            .ToList();

        return failures.Select(f =>
        {
            var causedByEdges = _store.GetEdgesFrom(f.Id)
                .Where(e => e.Type == EdgeTypes.CausedBy)
                .ToList();

            var causes = causedByEdges
                .Select(e => _store.GetEntity(e.TargetId))
                .Where(e => e != null)
                .Cast<GraphEntity>()
                .ToList();

            return new FailureWithCauses
            {
                Failure = f,
                PotentialCauses = causes,
                CausalEdges = causedByEdges
            };
        }).ToList();
    }

    /// <summary>
    /// Get all updates and their outcomes (installed vs failed + any linked failures).
    /// </summary>
    public IReadOnlyList<UpdateOutcome> GetUpdateHistory()
    {
        var updates = _store.GetEntitiesByType(EntityTypes.Update);

        return updates.Select(u =>
        {
            var linkedFailures = _store.GetEdgesTo(u.Id)
                .Where(e => e.Type == EdgeTypes.CausedBy)
                .Select(e => _store.GetEntity(e.SourceId))
                .Where(e => e != null)
                .Cast<GraphEntity>()
                .ToList();

            return new UpdateOutcome
            {
                Update = u,
                State = u.Properties.GetValueOrDefault("state", "unknown"),
                LinkedFailures = linkedFailures
            };
        }).ToList();
    }
}

public sealed class CausalChainLink
{
    public required GraphEntity Effect { get; init; }
    public required GraphEntity Cause { get; init; }
    public required GraphEdge Edge { get; init; }
    public int Depth { get; init; }
}

public sealed class FailureWithCauses
{
    public required GraphEntity Failure { get; init; }
    public required IReadOnlyList<GraphEntity> PotentialCauses { get; init; }
    public required IReadOnlyList<GraphEdge> CausalEdges { get; init; }
}

public sealed class UpdateOutcome
{
    public required GraphEntity Update { get; init; }
    public required string State { get; init; }
    public required IReadOnlyList<GraphEntity> LinkedFailures { get; init; }
}
