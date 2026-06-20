using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Graph;

/// <summary>
/// Converts a Device Intelligence MCP device twin JSON into knowledge graph entities and edges.
/// </summary>
public sealed class GraphBuilder
{
    private readonly GraphStore _store;
    private readonly List<string> _generatedFacts = new();

    public GraphBuilder(GraphStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Facts generated during the last BuildFromDeviceTwin call, ready for semantic indexing.
    /// </summary>
    public IReadOnlyList<string> GeneratedFactIds => _generatedFacts;

    /// <summary>
    /// Ingest a device twin JSON document and populate the graph.
    /// </summary>
    public GraphSnapshot BuildFromDeviceTwin(JsonDocument twin, DateTimeOffset observedAt, string? sourceSnapshotId = null)
    {
        _generatedFacts.Clear();
        var root = twin.RootElement;

        BuildDeviceEntity(root, observedAt);
        BuildOsBuildEntity(root, observedAt);
        BuildUpdates(root, observedAt);
        BuildDrivers(root, observedAt);
        BuildHardware(root, observedAt);
        BuildReliability(root, observedAt);
        BuildPerformance(root, observedAt);
        BuildSecurityPosture(root, observedAt);

        var stats = _store.GetStats();
        var snapshot = new GraphSnapshot
        {
            Id = $"snap-{observedAt:yyyyMMdd-HHmmss}",
            Timestamp = observedAt,
            EntityCount = stats.EntityCount,
            EdgeCount = stats.EdgeCount,
            SourceSnapshotId = sourceSnapshotId
        };
        _store.InsertSnapshot(snapshot);

        return snapshot;
    }

    private void BuildDeviceEntity(JsonElement root, DateTimeOffset observedAt)
    {
        var hostname = GetStringProp(root, "inventory.computerName")
                    ?? GetStringProp(root, "hostname")
                    ?? Environment.MachineName;

        var entity = new GraphEntity
        {
            Id = $"device:{hostname.ToLowerInvariant()}",
            Type = EntityTypes.Device,
            Label = hostname,
            FirstSeen = observedAt,
            LastSeen = observedAt,
            Properties = new Dictionary<string, string>
            {
                ["hostname"] = hostname,
                ["architecture"] = GetStringProp(root, "inventory.architecture") ?? "unknown",
                ["manufacturer"] = GetStringProp(root, "inventory.manufacturer") ?? "unknown",
                ["model"] = GetStringProp(root, "inventory.model") ?? "unknown"
            }
        };
        _store.UpsertEntity(entity);
        EmitFact(entity.Id, $"Device '{hostname}' is a {entity.Properties["manufacturer"]} {entity.Properties["model"]} ({entity.Properties["architecture"]})", observedAt);
    }

    private void BuildOsBuildEntity(JsonElement root, DateTimeOffset observedAt)
    {
        var build = GetStringProp(root, "os.buildNumber")
                 ?? GetStringProp(root, "inventory.osBuild")
                 ?? "unknown";
        var version = GetStringProp(root, "os.version") ?? build;

        var entity = new GraphEntity
        {
            Id = $"os:{build}",
            Type = EntityTypes.OsBuild,
            Label = $"Windows Build {build}",
            FirstSeen = observedAt,
            LastSeen = observedAt,
            Properties = new Dictionary<string, string>
            {
                ["buildNumber"] = build,
                ["version"] = version,
                ["edition"] = GetStringProp(root, "os.edition") ?? "unknown"
            }
        };
        _store.UpsertEntity(entity);
        EmitFact(entity.Id, $"OS is Windows Build {build} ({entity.Properties["edition"]})", observedAt);

        // Link device → OS
        var deviceId = $"device:{(GetStringProp(root, "inventory.computerName") ?? Environment.MachineName).ToLowerInvariant()}";
        _store.UpsertEdge(new GraphEdge
        {
            Id = $"edge:{deviceId}->runs->{entity.Id}",
            SourceId = deviceId,
            TargetId = entity.Id,
            Type = EdgeTypes.PartOf,
            CreatedAt = observedAt
        });
    }

    private void BuildUpdates(JsonElement root, DateTimeOffset observedAt)
    {
        if (!TryGetElement(root, "updates.installedUpdates", out var updates)) return;

        foreach (var update in updates.EnumerateArray())
        {
            var kbId = GetStringProp(update, "kbId") ?? GetStringProp(update, "hotFixId");
            if (kbId is null) continue;
            var title = GetStringProp(update, "title") ?? kbId;
            var installedOn = GetStringProp(update, "installedOn");
            var state = GetStringProp(update, "state") ?? "installed";

            var entity = new GraphEntity
            {
                Id = $"update:{kbId.ToUpperInvariant()}",
                Type = EntityTypes.Update,
                Label = title,
                FirstSeen = observedAt,
                LastSeen = observedAt,
                Properties = new Dictionary<string, string>
                {
                    ["kbId"] = kbId,
                    ["title"] = title,
                    ["state"] = state,
                    ["installedOn"] = installedOn ?? ""
                }
            };
            _store.UpsertEntity(entity);

            var edgeType = state == "failed" ? EdgeTypes.FailedOn : EdgeTypes.Installed;
            var deviceId = $"device:{(GetStringProp(root, "inventory.computerName") ?? Environment.MachineName).ToLowerInvariant()}";
            _store.UpsertEdge(new GraphEdge
            {
                Id = $"edge:{entity.Id}->{edgeType}->{deviceId}",
                SourceId = entity.Id,
                TargetId = deviceId,
                Type = edgeType,
                CreatedAt = observedAt
            });

            var factText = state == "failed"
                ? $"Update {kbId} ({title}) failed on the device"
                : $"Update {kbId} ({title}) was installed on {installedOn ?? "unknown date"}";
            EmitFact(entity.Id, factText, observedAt);
        }

        // Also process failed updates if separate
        if (TryGetElement(root, "updates.recentFailures", out var failures))
        {
            foreach (var failure in failures.EnumerateArray())
            {
                var kbId = GetStringProp(failure, "kbId") ?? GetStringProp(failure, "title") ?? "unknown";
                var errorCode = GetStringProp(failure, "errorCode") ?? GetStringProp(failure, "hresult") ?? "";
                var title = GetStringProp(failure, "title") ?? kbId;

                var entity = new GraphEntity
                {
                    Id = $"update:{kbId.ToUpperInvariant()}",
                    Type = EntityTypes.Update,
                    Label = title,
                    FirstSeen = observedAt,
                    LastSeen = observedAt,
                    Properties = new Dictionary<string, string>
                    {
                        ["kbId"] = kbId,
                        ["title"] = title,
                        ["state"] = "failed",
                        ["errorCode"] = errorCode
                    }
                };
                _store.UpsertEntity(entity);
                EmitFact(entity.Id, $"Update {kbId} failed with error {errorCode}: {title}", observedAt);
            }
        }
    }

    private void BuildDrivers(JsonElement root, DateTimeOffset observedAt)
    {
        if (!TryGetElement(root, "drivers", out var drivers)) return;

        var driverArray = drivers.ValueKind == JsonValueKind.Array ? drivers : default;
        if (driverArray.ValueKind != JsonValueKind.Array) return;

        foreach (var driver in driverArray.EnumerateArray())
        {
            var name = GetStringProp(driver, "friendlyName") ?? GetStringProp(driver, "name") ?? "unknown";
            var version = GetStringProp(driver, "version") ?? "";
            var infName = GetStringProp(driver, "infName") ?? "";

            var entity = new GraphEntity
            {
                Id = $"driver:{DeterministicId(name + version)}",
                Type = EntityTypes.Driver,
                Label = $"{name} v{version}",
                FirstSeen = observedAt,
                LastSeen = observedAt,
                Properties = new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["version"] = version,
                    ["infName"] = infName,
                    ["provider"] = GetStringProp(driver, "provider") ?? "",
                    ["deviceClass"] = GetStringProp(driver, "deviceClass") ?? ""
                }
            };
            _store.UpsertEntity(entity);
            EmitFact(entity.Id, $"Driver '{name}' version {version} ({infName}) is installed", observedAt);
        }
    }

    private void BuildHardware(JsonElement root, DateTimeOffset observedAt)
    {
        if (!TryGetElement(root, "inventory.hardware", out var hardware) &&
            !TryGetElement(root, "hardware", out hardware)) return;

        if (hardware.ValueKind != JsonValueKind.Array) return;

        foreach (var component in hardware.EnumerateArray())
        {
            var name = GetStringProp(component, "name") ?? "unknown";
            var deviceClass = GetStringProp(component, "class") ?? "";
            var status = GetStringProp(component, "status") ?? "OK";

            var entity = new GraphEntity
            {
                Id = $"hw:{DeterministicId(name + deviceClass)}",
                Type = EntityTypes.HardwareComponent,
                Label = name,
                FirstSeen = observedAt,
                LastSeen = observedAt,
                Properties = new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["class"] = deviceClass,
                    ["status"] = status
                }
            };
            _store.UpsertEntity(entity);

            if (status != "OK")
            {
                EmitFact(entity.Id, $"Hardware component '{name}' ({deviceClass}) has status: {status}", observedAt);
            }
        }
    }

    private void BuildReliability(JsonElement root, DateTimeOffset observedAt)
    {
        var sections = new[] { "reliability.applicationCrashes", "reliability.systemErrors", "reliability.bugchecks", "reliability.unexpectedShutdowns" };

        foreach (var section in sections)
        {
            if (!TryGetElement(root, section, out var events)) continue;
            if (events.ValueKind != JsonValueKind.Array) continue;

            foreach (var evt in events.EnumerateArray())
            {
                var source = GetStringProp(evt, "source") ?? GetStringProp(evt, "faultingModule") ?? "unknown";
                var timestamp = GetStringProp(evt, "timestamp") ?? observedAt.ToString("o");
                var description = GetStringProp(evt, "description") ?? section.Split('.').Last();
                var errorCode = GetStringProp(evt, "errorCode") ?? GetStringProp(evt, "bugcheckCode") ?? "";

                var failureType = section.Contains("Crashes") ? "crash"
                    : section.Contains("bugcheck") ? "bsod"
                    : section.Contains("Shutdown") ? "unexpected_shutdown"
                    : "system_error";

                var entity = new GraphEntity
                {
                    Id = $"failure:{DeterministicId(source + timestamp + failureType)}",
                    Type = EntityTypes.Failure,
                    Label = $"{failureType}: {source}",
                    FirstSeen = observedAt,
                    LastSeen = observedAt,
                    Properties = new Dictionary<string, string>
                    {
                        ["type"] = failureType,
                        ["source"] = source,
                        ["timestamp"] = timestamp,
                        ["description"] = description,
                        ["errorCode"] = errorCode
                    }
                };
                _store.UpsertEntity(entity);

                var factText = failureType switch
                {
                    "crash" => $"Application '{source}' crashed on {timestamp}",
                    "bsod" => $"Blue screen (BSOD) occurred: {source} code {errorCode} on {timestamp}",
                    "unexpected_shutdown" => $"Unexpected shutdown occurred on {timestamp}",
                    _ => $"System error from '{source}' on {timestamp}: {description}"
                };
                EmitFact(entity.Id, factText, observedAt);
            }
        }
    }

    private void BuildPerformance(JsonElement root, DateTimeOffset observedAt)
    {
        if (!TryGetElement(root, "performance", out var perf)) return;

        var cpuPct = GetStringProp(perf, "cpuUsagePercent") ?? GetStringProp(perf, "cpu") ?? "";
        var memAvail = GetStringProp(perf, "memoryAvailableGb") ?? GetStringProp(perf, "memAvailGb") ?? "";
        var diskUtil = GetStringProp(perf, "diskUtilizationPercent") ?? "";

        if (string.IsNullOrEmpty(cpuPct) && string.IsNullOrEmpty(memAvail)) return;

        var entity = new GraphEntity
        {
            Id = $"perf:{observedAt:yyyyMMdd-HHmmss}",
            Type = EntityTypes.PerformanceSample,
            Label = $"Performance at {observedAt:g}",
            FirstSeen = observedAt,
            LastSeen = observedAt,
            Properties = new Dictionary<string, string>
            {
                ["cpuPercent"] = cpuPct,
                ["memoryAvailGb"] = memAvail,
                ["diskUtilPercent"] = diskUtil
            }
        };
        _store.UpsertEntity(entity);
        EmitFact(entity.Id, $"Performance sample: CPU {cpuPct}%, memory available {memAvail} GB, disk {diskUtil}% utilized", observedAt);
    }

    private void BuildSecurityPosture(JsonElement root, DateTimeOffset observedAt)
    {
        if (!TryGetElement(root, "security", out var security)) return;

        var entity = new GraphEntity
        {
            Id = $"security:{observedAt:yyyyMMdd}",
            Type = EntityTypes.SecurityPosture,
            Label = $"Security posture on {observedAt:d}",
            FirstSeen = observedAt,
            LastSeen = observedAt,
            Properties = new Dictionary<string, string>
            {
                ["firewall"] = GetStringProp(security, "firewallEnabled") ?? "unknown",
                ["antivirus"] = GetStringProp(security, "antivirusStatus") ?? "unknown",
                ["secureBoot"] = GetStringProp(security, "secureBootEnabled") ?? "unknown",
                ["bitlocker"] = GetStringProp(security, "bitlockerStatus") ?? "unknown",
                ["tpmVersion"] = GetStringProp(security, "tpmVersion") ?? "unknown"
            }
        };
        _store.UpsertEntity(entity);

        var issues = entity.Properties
            .Where(kv => kv.Value == "false" || kv.Value == "disabled" || kv.Value == "off")
            .Select(kv => kv.Key)
            .ToList();

        if (issues.Count > 0)
        {
            EmitFact(entity.Id, $"Security concerns: {string.Join(", ", issues)} are not enabled", observedAt);
        }
        else
        {
            EmitFact(entity.Id, "Security posture appears healthy: firewall, antivirus, secure boot, and BitLocker all active", observedAt);
        }
    }

    #region Helpers

    private void EmitFact(string entityId, string factText, DateTimeOffset observedAt)
    {
        var factId = $"fact:{DeterministicId(factText + observedAt.ToString("o"))}";
        _store.InsertFact(factId, entityId, factText, observedAt);
        _generatedFacts.Add(factId);
    }

    private static string? GetStringProp(JsonElement element, string dotPath)
    {
        var parts = dotPath.Split('.');
        var current = element;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString()
             : current.ValueKind == JsonValueKind.Number ? current.GetRawText()
             : current.ValueKind == JsonValueKind.True ? "true"
             : current.ValueKind == JsonValueKind.False ? "false"
             : null;
    }

    private static bool TryGetElement(JsonElement root, string dotPath, out JsonElement result)
    {
        var parts = dotPath.Split('.');
        var current = root;
        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                result = default;
                return false;
            }
        }
        result = current;
        return true;
    }

    private static string DeterministicId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    #endregion
}
