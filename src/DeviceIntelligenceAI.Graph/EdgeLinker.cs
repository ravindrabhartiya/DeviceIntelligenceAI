using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Graph;

/// <summary>
/// Infers causal and correlational relationships between graph entities.
/// Uses temporal proximity, module matching, and heuristic rules.
/// </summary>
public sealed class EdgeLinker
{
    private readonly GraphStore _store;

    // Temporal proximity window for causal inference
    private static readonly TimeSpan CausalWindow = TimeSpan.FromHours(24);

    public EdgeLinker(GraphStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Run all linking heuristics across the graph.
    /// Returns the number of new edges created.
    /// </summary>
    public int LinkAll()
    {
        int count = 0;
        count += LinkUpdateFailuresToDriverChanges();
        count += LinkFailuresToPrecedingUpdates();
        count += LinkPerformanceDegradationToChanges();
        return count;
    }

    /// <summary>
    /// If a driver changed and a failure occurred within the causal window,
    /// create a caused_by edge from the failure to the driver change.
    /// </summary>
    private int LinkUpdateFailuresToDriverChanges()
    {
        var failures = _store.GetEntitiesByType(EntityTypes.Failure);
        var drivers = _store.GetEntitiesByType(EntityTypes.Driver);
        int created = 0;

        foreach (var failure in failures)
        {
            var failureTime = failure.FirstSeen;
            var failureModule = failure.Properties.GetValueOrDefault("source", "").ToLowerInvariant();

            foreach (var driver in drivers)
            {
                var driverName = driver.Properties.GetValueOrDefault("name", "").ToLowerInvariant();
                if (string.IsNullOrEmpty(driverName) || string.IsNullOrEmpty(failureModule)) continue;

                // Module match: failure source contains driver name or vice versa
                bool moduleMatch = failureModule.Contains(driverName) || driverName.Contains(failureModule);
                if (!moduleMatch) continue;

                // Temporal proximity: driver seen near failure time
                var timeDiff = (failureTime - driver.LastSeen).Duration();
                if (timeDiff > CausalWindow) continue;

                var edgeId = $"edge:{failure.Id}->caused_by->{driver.Id}";
                var confidence = moduleMatch ? 0.8 : 0.5;
                confidence *= Math.Max(0.3, 1.0 - timeDiff.TotalHours / CausalWindow.TotalHours);

                _store.UpsertEdge(new GraphEdge
                {
                    Id = edgeId,
                    SourceId = failure.Id,
                    TargetId = driver.Id,
                    Type = EdgeTypes.CausedBy,
                    CreatedAt = failureTime,
                    Confidence = confidence,
                    Properties = new Dictionary<string, string>
                    {
                        ["reason"] = "module_match + temporal_proximity",
                        ["timeDiffHours"] = timeDiff.TotalHours.ToString("F1")
                    }
                });
                created++;
            }
        }
        return created;
    }

    /// <summary>
    /// If a failure occurred within the causal window after an update,
    /// create a caused_by edge from the failure to the update.
    /// </summary>
    private int LinkFailuresToPrecedingUpdates()
    {
        var failures = _store.GetEntitiesByType(EntityTypes.Failure);
        var updates = _store.GetEntitiesByType(EntityTypes.Update);
        int created = 0;

        foreach (var failure in failures)
        {
            var failureTime = failure.FirstSeen;

            foreach (var update in updates)
            {
                var updateState = update.Properties.GetValueOrDefault("state", "");
                if (updateState == "failed") continue; // failed updates didn't change anything

                var updateTime = update.LastSeen;
                var timeDiff = failureTime - updateTime;

                // Update must precede the failure
                if (timeDiff < TimeSpan.Zero || timeDiff > CausalWindow) continue;

                var confidence = 0.5 * Math.Max(0.3, 1.0 - timeDiff.TotalHours / CausalWindow.TotalHours);

                var edgeId = $"edge:{failure.Id}->caused_by->{update.Id}";
                _store.UpsertEdge(new GraphEdge
                {
                    Id = edgeId,
                    SourceId = failure.Id,
                    TargetId = update.Id,
                    Type = EdgeTypes.CausedBy,
                    CreatedAt = failureTime,
                    Confidence = confidence,
                    Properties = new Dictionary<string, string>
                    {
                        ["reason"] = "temporal_proximity",
                        ["timeDiffHours"] = timeDiff.TotalHours.ToString("F1")
                    }
                });
                created++;
            }
        }
        return created;
    }

    /// <summary>
    /// If performance degraded after an update or driver change,
    /// link the performance sample to the change.
    /// </summary>
    private int LinkPerformanceDegradationToChanges()
    {
        var perfSamples = _store.GetEntitiesByType(EntityTypes.PerformanceSample);
        if (perfSamples.Count < 2) return 0;

        var updates = _store.GetEntitiesByType(EntityTypes.Update);
        int created = 0;

        // Compare consecutive samples for degradation
        for (int i = 0; i < perfSamples.Count - 1; i++)
        {
            var current = perfSamples[i];
            var previous = perfSamples[i + 1];

            if (!double.TryParse(current.Properties.GetValueOrDefault("cpuPercent", ""), out var currentCpu)) continue;
            if (!double.TryParse(previous.Properties.GetValueOrDefault("cpuPercent", ""), out var prevCpu)) continue;

            // Significant degradation: >30% CPU increase
            if (currentCpu - prevCpu < 30) continue;

            // Find updates between the two samples
            foreach (var update in updates)
            {
                if (update.LastSeen > previous.FirstSeen && update.LastSeen < current.FirstSeen)
                {
                    var edgeId = $"edge:{update.Id}->degraded->{current.Id}";
                    _store.UpsertEdge(new GraphEdge
                    {
                        Id = edgeId,
                        SourceId = update.Id,
                        TargetId = current.Id,
                        Type = EdgeTypes.Degraded,
                        CreatedAt = current.FirstSeen,
                        Confidence = 0.6,
                        Properties = new Dictionary<string, string>
                        {
                            ["cpuIncrease"] = (currentCpu - prevCpu).ToString("F1"),
                            ["reason"] = "performance_degradation_after_update"
                        }
                    });
                    created++;
                }
            }
        }
        return created;
    }
}
