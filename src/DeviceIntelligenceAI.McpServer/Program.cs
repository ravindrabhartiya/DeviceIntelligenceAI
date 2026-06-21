using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Ingestion;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;
using DeviceIntelligenceAI.Reasoning;

namespace DeviceIntelligenceAI.McpServer;

/// <summary>
/// MCP server entry point for the Device Intelligence AI knowledge graph.
/// Communicates over stdio using JSON-RPC 2.0.
/// </summary>
public static class Program
{
    private static GraphStore? _graphStore;
    private static ISemanticIndex? _semanticIndex;
    private static SemanticIndexer? _semanticIndexer;
    private static ReasoningEngine? _reasoningEngine;
    private static IncrementalIngester? _ingester;

    public static async Task Main(string[] args)
    {
        // Force all logging/output to stderr
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

        var dbPath = GetDatabasePath();
        _graphStore = new GraphStore(dbPath);
        // Enforce the 30-day rolling window on startup.
        _graphStore.PruneOlderThan(DateTimeOffset.UtcNow.AddDays(-30));
        _semanticIndex = SemanticIndexFactory.Create(forceLocal: !WindowsSemanticIndex.IsAvailable());
        _semanticIndexer = new SemanticIndexer(_graphStore, _semanticIndex);
        _ingester = new IncrementalIngester(_graphStore);

        // Rehydrate the in-memory semantic index from persisted facts so queries are grounded
        // immediately after restart without requiring a fresh ingestion.
        var rehydrated = await _semanticIndexer.RehydrateAsync();
        await Console.Error.WriteLineAsync($"[device-intelligence-ai] Rehydrated {rehydrated} facts into semantic index.");

        // Select the best available LLM: Phi Silica (Windows AI) → Ollama → Mock
        var (llm, backend) = await LanguageModelFactory.CreateAsync();
        await Console.Error.WriteLineAsync($"[device-intelligence-ai] LLM backend: {backend}");

        _reasoningEngine = new ReasoningEngine(_semanticIndex, llm, _graphStore);

        await Console.Error.WriteLineAsync("[device-intelligence-ai] Server started. Listening on stdio...");

        // Main JSON-RPC loop
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break; // stdin closed

            try
            {
                var request = JsonDocument.Parse(line);
                var response = await HandleRequest(request);
                if (response != null) // Notifications don't get responses
                {
                    await writer.WriteLineAsync(response);
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[ERROR] {ex.Message}");
            }
        }
    }

    private static async Task<string?> HandleRequest(JsonDocument request)
    {
        var root = request.RootElement;
        var method = root.GetProperty("method").GetString()!;
        var hasId = root.TryGetProperty("id", out var idElement);

        // Notifications (no id) don't get responses
        if (!hasId) return null;

        var id = idElement.ValueKind == JsonValueKind.Number ? idElement.GetInt32() : 0;

        try
        {
            object result = method switch
            {
                "initialize" => HandleInitialize(),
                "tools/list" => HandleListTools(),
                "tools/call" => await HandleToolCall(root),
                "resources/list" => HandleListResources(),
                "resources/read" => HandleReadResource(root),
                _ => throw new NotSupportedException($"Unknown method: {method}")
            };

            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result });
        }
        catch (Exception ex)
        {
            var error = new { code = -32603, message = ex.Message };
            return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error });
        }
    }

    private static object HandleInitialize()
    {
        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { },
                resources = new { }
            },
            serverInfo = new
            {
                name = "device-intelligence-ai",
                version = "1.0.0"
            }
        };
    }

    private static object HandleListTools()
    {
        return new
        {
            tools = GetToolDefinitions()
        };
    }

    private static object[] GetToolDefinitions()
    {
        return new object[]
        {
            new Dictionary<string, object> { ["name"] = "query_knowledge_graph", ["description"] = "Query the device knowledge graph with a natural language question. Returns a reasoned answer grounded in indexed device facts.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["query"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Natural language question about the device" } }, ["required"] = new[] { "query" } } },
            new Dictionary<string, object> { ["name"] = "get_health_summary", ["description"] = "Get a natural language summary of the device's current health state.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>(), ["required"] = Array.Empty<string>() } },
            new Dictionary<string, object> { ["name"] = "explain_update_failure", ["description"] = "Explain why a Windows update failed and suggest remediation.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["kb_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "KB article ID (e.g., KB5034441). If omitted, explains the most recent failure." } }, ["required"] = Array.Empty<string>() } },
            new Dictionary<string, object> { ["name"] = "predict_update_risk", ["description"] = "Assess whether it's safe to install Windows updates based on device history.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>(), ["required"] = Array.Empty<string>() } },
            new Dictionary<string, object> { ["name"] = "get_device_narrative", ["description"] = "Narrate what happened on the device over a time period.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["days"] = new Dictionary<string, string> { ["type"] = "integer", ["description"] = "Number of days to look back (default: 7)" } }, ["required"] = Array.Empty<string>() } },
            new Dictionary<string, object> { ["name"] = "get_causal_chain", ["description"] = "Trace the causal chain for a specific failure, explaining root cause.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["failure_id"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Entity ID of the failure to trace" } }, ["required"] = new[] { "failure_id" } } },
            new Dictionary<string, object> { ["name"] = "generate_servicing_diagram", ["description"] = "Generate a Mermaid state diagram of the Windows servicing pipeline.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>(), ["required"] = Array.Empty<string>() } },
            new Dictionary<string, object> { ["name"] = "ingest_device_twin", ["description"] = "Ingest a device twin JSON document into the knowledge graph.", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["twin_json"] = new Dictionary<string, string> { ["type"] = "string", ["description"] = "Device twin JSON (from Device Intelligence MCP)" } }, ["required"] = new[] { "twin_json" } } },
            new Dictionary<string, object> { ["name"] = "get_graph_stats", ["description"] = "Get statistics about the knowledge graph (entity/edge/fact counts).", ["inputSchema"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object>(), ["required"] = Array.Empty<string>() } },
        };
    }

    private static async Task<object> HandleToolCall(JsonElement root)
    {
        var @params = root.GetProperty("params");
        var toolName = @params.GetProperty("name").GetString()!;
        var arguments = @params.TryGetProperty("arguments", out var args) ? args : default;

        var result = toolName switch
        {
            "query_knowledge_graph" => await ToolQueryKnowledgeGraph(arguments),
            "get_health_summary" => await ToolGetHealthSummary(),
            "explain_update_failure" => await ToolExplainUpdateFailure(arguments),
            "predict_update_risk" => await ToolPredictUpdateRisk(),
            "get_device_narrative" => await ToolGetDeviceNarrative(arguments),
            "get_causal_chain" => await ToolGetCausalChain(arguments),
            "generate_servicing_diagram" => await ToolGenerateServicingDiagram(),
            "ingest_device_twin" => await ToolIngestDeviceTwin(arguments),
            "get_graph_stats" => ToolGetGraphStats(),
            _ => throw new NotSupportedException($"Unknown tool: {toolName}")
        };

        return result;
    }

    private static async Task<object> ToolQueryKnowledgeGraph(JsonElement args)
    {
        var query = args.GetProperty("query").GetString()!;
        var result = await _reasoningEngine!.QueryAsync(query);

        return FormatToolResult(result);
    }

    private static async Task<object> ToolGetHealthSummary()
    {
        var result = await _reasoningEngine!.GetHealthSummaryAsync();
        return FormatToolResult(result);
    }

    private static async Task<object> ToolExplainUpdateFailure(JsonElement args)
    {
        string? kbId = null;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("kb_id", out var kb))
            kbId = kb.GetString();

        var result = await _reasoningEngine!.ExplainUpdateFailureAsync(kbId);
        return FormatToolResult(result);
    }

    private static async Task<object> ToolPredictUpdateRisk()
    {
        var result = await _reasoningEngine!.PredictUpdateRiskAsync();
        return FormatToolResult(result);
    }

    private static async Task<object> ToolGetDeviceNarrative(JsonElement args)
    {
        int days = 7;
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("days", out var d))
            days = d.GetInt32();

        var result = await _reasoningEngine!.NarrateTimelineAsync(
            DateTimeOffset.UtcNow.AddDays(-days),
            DateTimeOffset.UtcNow);
        return FormatToolResult(result);
    }

    private static async Task<object> ToolGetCausalChain(JsonElement args)
    {
        var failureId = args.GetProperty("failure_id").GetString()!;
        var result = await _reasoningEngine!.ExplainCausalChainAsync(failureId);
        return FormatToolResult(result);
    }

    private static async Task<object> ToolGenerateServicingDiagram()
    {
        var result = await _reasoningEngine!.GenerateServicingDiagramAsync();
        return FormatToolResult(result);
    }

    private static async Task<object> ToolIngestDeviceTwin(JsonElement args)
    {
        var twinJson = args.GetProperty("twin_json").GetString()!;
        var twin = JsonDocument.Parse(twinJson);

        var ingestionResult = _ingester!.Ingest(twin);
        await _semanticIndexer!.IndexAllPendingAsync();
        _reasoningEngine!.InvalidateCache();

        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(new
                    {
                        status = "ingested",
                        snapshotId = ingestionResult.Snapshot.Id,
                        newFacts = ingestionResult.NewFactIds.Count,
                        newEdges = ingestionResult.NewEdgesLinked,
                        drift = new
                        {
                            severity = ingestionResult.Drift.Severity.ToString(),
                            summary = ingestionResult.Drift.Summary
                        }
                    })
                }
            }
        };
    }

    private static object ToolGetGraphStats()
    {
        var stats = _graphStore!.GetStats();
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(new
                    {
                        entities = stats.EntityCount,
                        edges = stats.EdgeCount,
                        facts = stats.FactCount
                    })
                }
            }
        };
    }

    private static object HandleListResources()
    {
        return new
        {
            resources = new[]
            {
                new { uri = "knowledge://graph/stats", name = "Graph Statistics", description = "Current knowledge graph entity, edge, and fact counts.", mimeType = "application/json" },
                new { uri = "knowledge://insights/cached", name = "Cached Insights", description = "Currently cached proactive reasoning results.", mimeType = "application/json" },
            }
        };
    }

    private static object HandleReadResource(JsonElement root)
    {
        var @params = root.GetProperty("params");
        var uri = @params.GetProperty("uri").GetString()!;

        var content = uri switch
        {
            "knowledge://graph/stats" => JsonSerializer.Serialize(_graphStore!.GetStats()),
            "knowledge://insights/cached" => JsonSerializer.Serialize(
                _reasoningEngine!.GetCachedInsights().Select(i => new { i.Key, answer = i.Result.Answer, cachedAt = i.CachedAt })
            ),
            _ => throw new NotSupportedException($"Unknown resource: {uri}")
        };

        return new
        {
            contents = new[]
            {
                new { uri, mimeType = "application/json", text = content }
            }
        };
    }

    private static object FormatToolResult(ReasoningResult result)
    {
        var output = new
        {
            answer = result.Answer,
            sources = result.Sources.Select(s => new
            {
                factId = s.FactId,
                text = s.FactText,
                observedAt = s.ObservedAt.ToString("o"),
                score = s.Score
            }),
            metadata = new
            {
                template = result.TemplateName,
                retrievedFacts = result.RetrievedFactCount,
                contextTokens = result.ContextTokenEstimate
            }
        };

        return new
        {
            content = new[]
            {
                new { type = "text", text = JsonSerializer.Serialize(output) }
            }
        };
    }

    private static string GetDatabasePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "device-intelligence-ai");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "knowledge-graph.db");
    }
}
