using System.Text.Json.Serialization;

namespace DeviceIntelligenceAI.Graph.Models;

/// <summary>
/// A node in the knowledge graph representing a device entity.
/// </summary>
public sealed class GraphEntity
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required string Label { get; init; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>
/// A directed edge in the knowledge graph representing a relationship.
/// </summary>
public sealed class GraphEdge
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset CreatedAt { get; set; }
    public double Confidence { get; set; } = 1.0;
    public Dictionary<string, string> Properties { get; init; } = new();
}

/// <summary>
/// A snapshot of the graph state at a point in time.
/// </summary>
public sealed class GraphSnapshot
{
    public required string Id { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int EntityCount { get; init; }
    public int EdgeCount { get; init; }
    public string? SourceSnapshotId { get; init; }
}
