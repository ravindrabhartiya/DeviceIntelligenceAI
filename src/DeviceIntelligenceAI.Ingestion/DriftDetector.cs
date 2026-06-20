using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Models;

namespace DeviceIntelligenceAI.Ingestion;

/// <summary>
/// Detects meaningful changes between graph states.
/// Triggers proactive reasoning when significant drift is detected.
/// </summary>
public sealed class DriftDetector
{
    private readonly GraphStore _store;

    public DriftDetector(GraphStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Compare current graph state against a previous time and identify drift.
    /// </summary>
    public DriftReport DetectDrift(DateTimeOffset since)
    {
        var report = new DriftReport { Since = since, DetectedAt = DateTimeOffset.Now };

        var recentEntities = _store.GetEntitiesInTimeRange(since, DateTimeOffset.Now);

        foreach (var entity in recentEntities)
        {
            // New entities that appeared after 'since'
            if (entity.FirstSeen > since)
            {
                report.NewEntities.Add(entity);
            }
        }

        // Check for new failures
        var newFailures = report.NewEntities
            .Where(e => e.Type == Graph.Schema.EntityTypes.Failure)
            .ToList();

        // Check for new updates (especially failed ones)
        var newUpdates = report.NewEntities
            .Where(e => e.Type == Graph.Schema.EntityTypes.Update)
            .ToList();

        var failedUpdates = newUpdates
            .Where(u => u.Properties.GetValueOrDefault("state", "") == "failed")
            .ToList();

        // Severity assessment
        if (newFailures.Any(f => f.Properties.GetValueOrDefault("type", "") == "bsod"))
        {
            report.Severity = DriftSeverity.Critical;
            report.Summary = $"BSOD detected since {since:g}";
        }
        else if (failedUpdates.Count > 0)
        {
            report.Severity = DriftSeverity.Warning;
            report.Summary = $"{failedUpdates.Count} update(s) failed since {since:g}";
        }
        else if (newFailures.Count > 3)
        {
            report.Severity = DriftSeverity.Warning;
            report.Summary = $"{newFailures.Count} failures detected since {since:g}";
        }
        else if (report.NewEntities.Count > 0)
        {
            report.Severity = DriftSeverity.Informational;
            report.Summary = $"{report.NewEntities.Count} new entities observed since {since:g}";
        }
        else
        {
            report.Severity = DriftSeverity.None;
            report.Summary = "No significant changes detected";
        }

        return report;
    }
}

public sealed class DriftReport
{
    public DateTimeOffset Since { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public DriftSeverity Severity { get; set; } = DriftSeverity.None;
    public string Summary { get; set; } = "";
    public List<GraphEntity> NewEntities { get; } = new();
}

public enum DriftSeverity
{
    None,
    Informational,
    Warning,
    Critical
}
