using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Ingestion;

/// <summary>
/// Serializes graph entities and their relationships into natural language facts
/// suitable for semantic embedding and retrieval.
/// </summary>
public sealed class FactSerializer
{
    /// <summary>
    /// Generate a natural language fact sentence from an entity and its context.
    /// </summary>
    public string SerializeEntity(GraphEntity entity, IReadOnlyList<GraphEdge>? relatedEdges = null)
    {
        var baseFact = entity.Type switch
        {
            EntityTypes.Device => SerializeDevice(entity),
            EntityTypes.OsBuild => SerializeOsBuild(entity),
            EntityTypes.Update => SerializeUpdate(entity),
            EntityTypes.Driver => SerializeDriver(entity),
            EntityTypes.Failure => SerializeFailure(entity),
            EntityTypes.PerformanceSample => SerializePerformance(entity),
            EntityTypes.SecurityPosture => SerializeSecurity(entity),
            EntityTypes.HardwareComponent => SerializeHardware(entity),
            EntityTypes.ServicingOperation => SerializeServicing(entity),
            _ => $"{entity.Type} '{entity.Label}' observed at {entity.LastSeen:g}"
        };

        // Append relationship context if available
        if (relatedEdges is { Count: > 0 })
        {
            var causes = relatedEdges
                .Where(e => e.Type == EdgeTypes.CausedBy)
                .Select(e => e.TargetId)
                .Take(3);

            if (causes.Any())
            {
                baseFact += $" [possibly caused by: {string.Join(", ", causes)}]";
            }
        }

        return baseFact;
    }

    private static string SerializeDevice(GraphEntity e)
    {
        var mfg = e.Properties.GetValueOrDefault("manufacturer", "unknown");
        var model = e.Properties.GetValueOrDefault("model", "unknown");
        var arch = e.Properties.GetValueOrDefault("architecture", "");
        return $"This device is a {mfg} {model} ({arch}), hostname '{e.Properties.GetValueOrDefault("hostname", e.Label)}'";
    }

    private static string SerializeOsBuild(GraphEntity e)
    {
        var build = e.Properties.GetValueOrDefault("buildNumber", "");
        var edition = e.Properties.GetValueOrDefault("edition", "");
        return $"Operating system is Windows {edition} Build {build}";
    }

    private static string SerializeUpdate(GraphEntity e)
    {
        var kb = e.Properties.GetValueOrDefault("kbId", "");
        var title = e.Properties.GetValueOrDefault("title", e.Label);
        var state = e.Properties.GetValueOrDefault("state", "unknown");
        var errorCode = e.Properties.GetValueOrDefault("errorCode", "");

        return state switch
        {
            "failed" when !string.IsNullOrEmpty(errorCode) =>
                $"Update {kb} ({title}) FAILED with error code {errorCode}",
            "failed" =>
                $"Update {kb} ({title}) FAILED to install",
            "installed" =>
                $"Update {kb} ({title}) was successfully installed on {e.Properties.GetValueOrDefault("installedOn", "unknown date")}",
            "pending" =>
                $"Update {kb} ({title}) is pending installation",
            _ =>
                $"Update {kb} ({title}) has state: {state}"
        };
    }

    private static string SerializeDriver(GraphEntity e)
    {
        var name = e.Properties.GetValueOrDefault("name", e.Label);
        var version = e.Properties.GetValueOrDefault("version", "");
        var provider = e.Properties.GetValueOrDefault("provider", "");
        var cls = e.Properties.GetValueOrDefault("deviceClass", "");
        return $"Driver '{name}' version {version} by {provider} (class: {cls}) is installed";
    }

    private static string SerializeFailure(GraphEntity e)
    {
        var type = e.Properties.GetValueOrDefault("type", "unknown");
        var source = e.Properties.GetValueOrDefault("source", "unknown");
        var timestamp = e.Properties.GetValueOrDefault("timestamp", "");
        var errorCode = e.Properties.GetValueOrDefault("errorCode", "");
        var description = e.Properties.GetValueOrDefault("description", "");

        return type switch
        {
            "crash" => $"Application crash: '{source}' crashed on {timestamp}. {description}",
            "bsod" => $"BSOD (Blue Screen): bugcheck from '{source}' code {errorCode} on {timestamp}",
            "unexpected_shutdown" => $"Unexpected shutdown occurred on {timestamp}",
            "system_error" => $"System error from '{source}' on {timestamp}: {description}",
            _ => $"Failure ({type}) from '{source}' on {timestamp}"
        };
    }

    private static string SerializePerformance(GraphEntity e)
    {
        var cpu = e.Properties.GetValueOrDefault("cpuPercent", "?");
        var mem = e.Properties.GetValueOrDefault("memoryAvailGb", "?");
        var disk = e.Properties.GetValueOrDefault("diskUtilPercent", "?");
        return $"Performance at {e.FirstSeen:g}: CPU at {cpu}%, memory available {mem} GB, disk {disk}% utilized";
    }

    private static string SerializeSecurity(GraphEntity e)
    {
        var items = new List<string>();
        if (e.Properties.GetValueOrDefault("firewall", "") is var fw && !string.IsNullOrEmpty(fw))
            items.Add($"firewall={fw}");
        if (e.Properties.GetValueOrDefault("antivirus", "") is var av && !string.IsNullOrEmpty(av))
            items.Add($"antivirus={av}");
        if (e.Properties.GetValueOrDefault("secureBoot", "") is var sb && !string.IsNullOrEmpty(sb))
            items.Add($"secure boot={sb}");
        if (e.Properties.GetValueOrDefault("bitlocker", "") is var bl && !string.IsNullOrEmpty(bl))
            items.Add($"BitLocker={bl}");

        return $"Security posture: {string.Join(", ", items)}";
    }

    private static string SerializeHardware(GraphEntity e)
    {
        var name = e.Properties.GetValueOrDefault("name", e.Label);
        var cls = e.Properties.GetValueOrDefault("class", "");
        var status = e.Properties.GetValueOrDefault("status", "OK");
        return status == "OK"
            ? $"Hardware '{name}' ({cls}) is functioning normally"
            : $"Hardware '{name}' ({cls}) has status: {status}";
    }

    private static string SerializeServicing(GraphEntity e)
    {
        var phase = e.Properties.GetValueOrDefault("phase", "unknown");
        var operation = e.Properties.GetValueOrDefault("operation", "");
        var state = e.Properties.GetValueOrDefault("state", "");
        return $"Servicing operation in phase '{phase}': {operation} (state: {state})";
    }
}
