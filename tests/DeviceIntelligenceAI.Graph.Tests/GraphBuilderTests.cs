using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Schema;

namespace DeviceIntelligenceAI.Graph.Tests;

public class GraphBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GraphStore _store;
    private readonly GraphBuilder _builder;

    public GraphBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-builder-{Guid.NewGuid():N}.db");
        _store = new GraphStore(_dbPath);
        _builder = new GraphBuilder(_store);
    }

    [Fact]
    public void BuildFromDeviceTwin_CreatesDeviceAndOsEntities()
    {
        var twin = CreateSampleTwin();
        var snapshot = _builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.EntityCount >= 2); // At least device + OS

        var devices = _store.GetEntitiesByType(EntityTypes.Device);
        Assert.Single(devices);
        Assert.Equal("TESTPC", devices[0].Properties["hostname"]);

        var osBuilds = _store.GetEntitiesByType(EntityTypes.OsBuild);
        Assert.Single(osBuilds);
    }

    [Fact]
    public void BuildFromDeviceTwin_ProcessesUpdates()
    {
        var twin = CreateSampleTwin();
        _builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        var updates = _store.GetEntitiesByType(EntityTypes.Update);
        Assert.Equal(2, updates.Count);
    }

    [Fact]
    public void BuildFromDeviceTwin_GeneratesFacts()
    {
        var twin = CreateSampleTwin();
        _builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        var facts = _store.GetUnindexedFacts(100);
        Assert.True(facts.Count > 0);
        Assert.Contains(facts, f => f.FactText.Contains("KB5034441"));
    }

    [Fact]
    public void BuildFromDeviceTwin_RescanDoesNotDuplicateStableFacts()
    {
        // Ingesting the same twin twice (different observed times) must dedup content-stable
        // facts (drivers, updates, etc.) instead of appending near-duplicate rows per scan.
        _builder.BuildFromDeviceTwin(CreateSampleTwin(), DateTimeOffset.UtcNow.AddHours(-1));
        _builder.BuildFromDeviceTwin(CreateSampleTwin(), DateTimeOffset.UtcNow);

        var driverFacts = _store.GetAllFacts().Where(f => f.FactText.Contains("Intel UHD")).ToList();
        Assert.Single(driverFacts);

        var updateFacts = _store.GetAllFacts().Where(f => f.FactText.Contains("KB5034441")).ToList();
        Assert.Single(updateFacts);
    }

    [Fact]
    public void BuildFromDeviceTwin_ProcessesFailures()
    {
        var twin = CreateSampleTwin();
        _builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);

        var failures = _store.GetEntitiesByType(EntityTypes.Failure);
        Assert.Single(failures);
        Assert.Contains("crash", failures[0].Properties["type"]);
    }

    private static JsonDocument CreateSampleTwin()
    {
        var json = """
        {
            "inventory": {
                "computerName": "TESTPC",
                "architecture": "x64",
                "manufacturer": "Microsoft",
                "model": "Surface Pro",
                "osBuild": "26100.1"
            },
            "os": {
                "buildNumber": "26100.1",
                "version": "24H2",
                "edition": "Pro"
            },
            "updates": {
                "installedUpdates": [
                    {
                        "kbId": "KB5034441",
                        "title": "Windows Recovery Environment Update",
                        "state": "installed",
                        "installedOn": "2024-06-15"
                    },
                    {
                        "kbId": "KB5039212",
                        "title": "2024-06 Cumulative Update",
                        "state": "failed",
                        "installedOn": null
                    }
                ],
                "recentFailures": []
            },
            "drivers": [
                {
                    "friendlyName": "Intel UHD Graphics",
                    "version": "31.0.101.4502",
                    "infName": "iigd_dch.inf",
                    "provider": "Intel Corporation",
                    "deviceClass": "Display"
                }
            ],
            "reliability": {
                "applicationCrashes": [
                    {
                        "source": "explorer.exe",
                        "timestamp": "2024-06-14T10:30:00Z",
                        "description": "Windows Explorer stopped working"
                    }
                ],
                "systemErrors": [],
                "bugchecks": [],
                "unexpectedShutdowns": []
            },
            "performance": {
                "cpuUsagePercent": "35",
                "memoryAvailableGb": "8.2",
                "diskUtilizationPercent": "45"
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
        return JsonDocument.Parse(json);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
