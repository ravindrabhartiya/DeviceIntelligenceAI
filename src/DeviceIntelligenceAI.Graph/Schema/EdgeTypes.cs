namespace DeviceIntelligenceAI.Graph.Schema;

/// <summary>
/// Types of relationships (edges) in the device knowledge graph.
/// </summary>
public static class EdgeTypes
{
    public const string Installed = "installed";
    public const string FailedOn = "failed_on";
    public const string Drives = "drives";
    public const string CausedBy = "caused_by";
    public const string Preceded = "preceded";
    public const string Degraded = "degraded";
    public const string BlockedBy = "blocked_by";
    public const string DependsOn = "depends_on";
    public const string Changed = "changed";
    public const string PartOf = "part_of";
    public const string OccurredOn = "occurred_on";
    public const string Indicates = "indicates";
}
