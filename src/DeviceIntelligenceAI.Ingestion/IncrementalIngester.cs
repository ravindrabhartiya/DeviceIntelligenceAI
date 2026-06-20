using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Models;
using DeviceIntelligenceAI.Ingestion.McpClient;

namespace DeviceIntelligenceAI.Ingestion;

/// <summary>
/// Orchestrates incremental ingestion: fetches device twin from MCP,
/// builds/updates the knowledge graph, links edges, detects drift.
/// </summary>
public sealed class IncrementalIngester
{
    private readonly GraphStore _store;
    private readonly GraphBuilder _graphBuilder;
    private readonly EdgeLinker _edgeLinker;
    private readonly DriftDetector _driftDetector;
    private readonly FactSerializer _factSerializer;

    private DateTimeOffset _lastIngestionTime = DateTimeOffset.MinValue;

    public IncrementalIngester(GraphStore store)
    {
        _store = store;
        _graphBuilder = new GraphBuilder(store);
        _edgeLinker = new EdgeLinker(store);
        _driftDetector = new DriftDetector(store);
        _factSerializer = new FactSerializer();
    }

    /// <summary>
    /// Run one ingestion cycle using a pre-fetched device twin document.
    /// </summary>
    public IngestionResult Ingest(JsonDocument deviceTwin, string? sourceSnapshotId = null)
    {
        var now = DateTimeOffset.UtcNow;

        // Build graph from device twin
        var snapshot = _graphBuilder.BuildFromDeviceTwin(deviceTwin, now, sourceSnapshotId);

        // Link causal/correlational edges
        var newEdges = _edgeLinker.LinkAll();

        // Detect drift since last ingestion
        var drift = _lastIngestionTime != DateTimeOffset.MinValue
            ? _driftDetector.DetectDrift(_lastIngestionTime)
            : new DriftReport { Since = now, Severity = DriftSeverity.None, Summary = "First ingestion" };

        _lastIngestionTime = now;

        return new IngestionResult
        {
            Snapshot = snapshot,
            NewFactIds = _graphBuilder.GeneratedFactIds.ToList(),
            NewEdgesLinked = newEdges,
            Drift = drift
        };
    }

    /// <summary>
    /// Run one ingestion cycle by calling the MCP server directly.
    /// </summary>
    public async Task<IngestionResult> IngestFromMcpAsync(DeviceIntelligenceMcpClient mcpClient, CancellationToken ct = default)
    {
        var twinResponse = await mcpClient.BuildDeviceTwinAsync(saveSnapshot: true, ct);

        // Extract the content from MCP tool response
        var root = twinResponse.RootElement;
        JsonDocument twinDoc;
        string? snapshotId = null;

        if (root.TryGetProperty("result", out var result))
        {
            if (result.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var textContent = content[0];
                if (textContent.TryGetProperty("text", out var text))
                {
                    twinDoc = JsonDocument.Parse(text.GetString()!);
                    // Try to extract snapshot ID
                    var parsed = twinDoc.RootElement;
                    if (parsed.TryGetProperty("savedSnapshotId", out var snapId))
                        snapshotId = snapId.GetString();
                }
                else
                {
                    twinDoc = JsonDocument.Parse(content[0].GetRawText());
                }
            }
            else
            {
                twinDoc = JsonDocument.Parse(result.GetRawText());
            }
        }
        else
        {
            twinDoc = twinResponse;
        }

        return Ingest(twinDoc, snapshotId);
    }
}

public sealed class IngestionResult
{
    public required GraphSnapshot Snapshot { get; init; }
    public required List<string> NewFactIds { get; init; }
    public required int NewEdgesLinked { get; init; }
    public required DriftReport Drift { get; init; }
}
