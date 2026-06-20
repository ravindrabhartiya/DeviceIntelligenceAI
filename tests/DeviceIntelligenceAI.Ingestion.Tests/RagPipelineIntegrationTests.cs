using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;
using DeviceIntelligenceAI.Reasoning;

namespace DeviceIntelligenceAI.Ingestion.Tests;

/// <summary>
/// Integration test: Graph → Semantic Index → Reasoning Engine (full RAG pipeline).
/// Uses MockLanguageModel to verify the pipeline wiring without a real LLM.
/// </summary>
public class RagPipelineIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GraphStore _store;
    private readonly LocalSemanticIndex _semanticIndex;
    private readonly SemanticIndexer _indexer;
    private readonly MockLanguageModel _llm;
    private readonly ReasoningEngine _engine;

    public RagPipelineIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-rag-{Guid.NewGuid():N}.db");
        _store = new GraphStore(_dbPath);
        _semanticIndex = new LocalSemanticIndex();
        _indexer = new SemanticIndexer(_store, _semanticIndex);
        _llm = new MockLanguageModel();
        _engine = new ReasoningEngine(_semanticIndex, _llm, _store);
    }

    private async Task IngestSampleTwin()
    {
        var builder = new GraphBuilder(_store);
        var twin = JsonDocument.Parse(SampleTwin);
        builder.BuildFromDeviceTwin(twin, DateTimeOffset.UtcNow);
        var linker = new EdgeLinker(_store);
        linker.LinkAll();
        await _indexer.IndexAllPendingAsync();
    }

    [Fact]
    public async Task QueryAsync_ReturnsAnswerWithSources()
    {
        await IngestSampleTwin();

        var result = await _engine.QueryAsync("Why did the update fail?");

        Assert.NotEmpty(result.Answer);
        Assert.NotEmpty(result.Sources);
        Assert.True(result.RetrievedFactCount > 0);
    }

    [Fact]
    public async Task GetHealthSummary_UsesCorrectTemplate()
    {
        await IngestSampleTwin();

        var result = await _engine.GetHealthSummaryAsync();

        Assert.NotEmpty(result.Answer);
        Assert.Equal("SummarizeHealth", result.TemplateName);
        // Verify the LLM received the right template
        Assert.Contains(_llm.ReceivedPrompts, p => p.Contains("health") || p.Contains("summary"));
    }

    [Fact]
    public async Task ExplainUpdateFailure_RetrievesFailureFacts()
    {
        await IngestSampleTwin();

        var result = await _engine.ExplainUpdateFailureAsync("KB5039212");

        Assert.NotEmpty(result.Answer);
        Assert.Equal("ExplainFailure", result.TemplateName);
        // Should retrieve the failed update fact
        Assert.Contains(result.Sources, s => s.FactText.Contains("KB5039212") || s.FactText.Contains("fail"));
    }

    [Fact]
    public async Task PredictUpdateRisk_ReturnsCachedOnSecondCall()
    {
        await IngestSampleTwin();

        var result1 = await _engine.PredictUpdateRiskAsync();
        var promptCountAfterFirst = _llm.ReceivedPrompts.Count;

        var result2 = await _engine.PredictUpdateRiskAsync();
        var promptCountAfterSecond = _llm.ReceivedPrompts.Count;

        // Second call should use cache, not invoke LLM again
        Assert.Equal(promptCountAfterFirst, promptCountAfterSecond);
        Assert.Equal(result1.Answer, result2.Answer);
    }

    [Fact]
    public async Task NarrateTimeline_FiltersbyTimeRange()
    {
        await IngestSampleTwin();

        var result = await _engine.NarrateTimelineAsync(
            from: DateTimeOffset.UtcNow.AddDays(-7),
            to: DateTimeOffset.UtcNow);

        Assert.NotEmpty(result.Answer);
        Assert.Equal("NarrateTimeline", result.TemplateName);
    }

    [Fact]
    public async Task GenerateServicingDiagram_ReturnsResult()
    {
        await IngestSampleTwin();

        var result = await _engine.GenerateServicingDiagramAsync();

        Assert.NotEmpty(result.Answer);
        Assert.Equal("GenerateDiagram", result.TemplateName);
    }

    [Fact]
    public async Task InvalidateCache_ForcesRecomputation()
    {
        await IngestSampleTwin();

        await _engine.GetHealthSummaryAsync();
        var promptCountBefore = _llm.ReceivedPrompts.Count;

        _engine.InvalidateCache();
        await _engine.GetHealthSummaryAsync();
        var promptCountAfter = _llm.ReceivedPrompts.Count;

        // After invalidation, LLM should be called again
        Assert.True(promptCountAfter > promptCountBefore);
    }

    private const string SampleTwin = """
    {
        "inventory": {
            "computerName": "RAGTEST",
            "architecture": "x64",
            "manufacturer": "Microsoft",
            "model": "Surface Laptop",
            "osBuild": "26100.3"
        },
        "os": {
            "buildNumber": "26100.3",
            "version": "24H2",
            "edition": "Pro"
        },
        "updates": {
            "installedUpdates": [
                { "kbId": "KB5034441", "title": "Recovery Update", "state": "installed", "installedOn": "2024-06-10" },
                { "kbId": "KB5039212", "title": "June Cumulative", "state": "failed", "installedOn": null }
            ],
            "recentFailures": [
                { "kbId": "KB5039212", "title": "June Cumulative", "errorCode": "0x80070032" }
            ]
        },
        "drivers": [
            { "friendlyName": "Intel UHD Graphics", "version": "31.0.101", "infName": "iigd_dch.inf", "provider": "Intel", "deviceClass": "Display" }
        ],
        "reliability": {
            "applicationCrashes": [
                { "source": "explorer.exe", "timestamp": "2024-06-14T08:00:00Z", "description": "Explorer crashed" }
            ],
            "systemErrors": [],
            "bugchecks": [],
            "unexpectedShutdowns": []
        },
        "performance": { "cpuUsagePercent": "45", "memoryAvailableGb": "6.1", "diskUtilizationPercent": "55" },
        "security": { "firewallEnabled": "true", "antivirusStatus": "active", "secureBootEnabled": "true", "bitlockerStatus": "enabled", "tpmVersion": "2.0" }
    }
    """;

    public void Dispose()
    {
        _semanticIndex.Dispose();
        _llm.Dispose();
        _store.Dispose();
        try { File.Delete(_dbPath); } catch { }
    }
}
