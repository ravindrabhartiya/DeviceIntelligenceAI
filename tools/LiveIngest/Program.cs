using System.Diagnostics;
using System.Text.Json;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Graph.Schema;
using DeviceIntelligenceAI.Ingestion;
using DeviceIntelligenceAI.Ingestion.McpClient;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;
using DeviceIntelligenceAI.Reasoning;

// === Configuration ===
var mcpServerPath = @"C:\PC Health MCP\src\DeviceIntelligence.Mcp\bin\Release\net8.0-windows\win-x64\device-intelligence-mcp.exe";
if (!File.Exists(mcpServerPath))
{
    // Try debug build
    mcpServerPath = @"C:\PC Health MCP\src\DeviceIntelligence.Mcp\bin\Debug\net8.0-windows\device-intelligence-mcp.exe";
}

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "device-intelligence-ai", "knowledge-graph.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║         Device Intelligence AI — Live Ingestion             ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine($"  MCP Server: {mcpServerPath}");
Console.WriteLine($"  Graph DB:   {dbPath}");
Console.WriteLine();

// === Initialize Graph Store ===
Console.Write("  [1/5] Initializing knowledge graph... ");
var store = new GraphStore(dbPath);
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓");
Console.ResetColor();

// === Connect to MCP Server ===
Console.Write("  [2/5] Connecting to Device Intelligence MCP... ");
using var mcpClient = new DeviceIntelligenceMcpClient(mcpServerPath);
var initResponse = await mcpClient.InitializeAsync();
var serverName = initResponse.RootElement
    .GetProperty("result")
    .GetProperty("serverInfo")
    .GetProperty("name")
    .GetString();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✓ ({serverName})");
Console.ResetColor();

// === Run Ingestion ===
Console.Write("  [3/5] Ingesting device twin into knowledge graph... ");
var sw = Stopwatch.StartNew();
var ingester = new IncrementalIngester(store);
var result = await ingester.IngestFromMcpAsync(mcpClient);
sw.Stop();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✓ ({sw.ElapsedMilliseconds}ms)");
Console.ResetColor();

// === Index semantically ===
Console.Write("  [4/5] Building semantic index... ");
var index = SemanticIndexFactory.Create();
var indexer = new SemanticIndexer(store, index);
await indexer.IndexAllPendingAsync();
// Rehydrate the full persisted fact set so every prior fact is searchable in this session,
// not just the ones newly ingested above.
var indexed = await indexer.RehydrateAsync();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine($"✓ ({indexed} facts indexed)");
Console.ResetColor();

// === Generate Summary with Reasoning Engine ===
Console.Write("  [5/5] Generating health summary... ");
var (languageModel, backend) = await LanguageModelFactory.CreateAsync();
Console.Write($"({backend}) ");
var reasoning = new ReasoningEngine(index, languageModel, store);
var summaryResult = await reasoning.GetHealthSummaryAsync();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("✓");
Console.ResetColor();

// === Print Results ===
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("═══════════════════════ INGESTION RESULTS ═══════════════════════");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine($"  Snapshot ID:      {result.Snapshot.Id}");
Console.WriteLine($"  Timestamp:        {result.Snapshot.Timestamp:yyyy-MM-dd HH:mm:ss}");
Console.WriteLine($"  Entities created: {result.Snapshot.EntityCount}");
Console.WriteLine($"  Facts generated:  {result.NewFactIds.Count}");
Console.WriteLine($"  Edges linked:     {result.NewEdgesLinked}");
Console.WriteLine($"  Drift severity:   {result.Drift.Severity}");
Console.WriteLine($"  Drift summary:    {result.Drift.Summary}");
Console.WriteLine();

// Show entity breakdown
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("═══════════════════════ KNOWLEDGE GRAPH ═════════════════════════");
Console.ResetColor();
Console.WriteLine();
var stats = store.GetStats();
Console.WriteLine($"  Total entities: {stats.EntityCount}");
Console.WriteLine($"  Total edges:    {stats.EdgeCount}");
Console.WriteLine($"  Total facts:    {stats.FactCount}");
Console.WriteLine();

var entityTypes = new[] { EntityTypes.Device, EntityTypes.OsBuild, EntityTypes.Update,
    EntityTypes.Driver, EntityTypes.HardwareComponent, EntityTypes.Failure,
    EntityTypes.PerformanceSample, EntityTypes.SecurityPosture, EntityTypes.App,
    EntityTypes.ServicingOperation };

foreach (var type in entityTypes)
{
    var entities = store.GetEntitiesByType(type);
    if (entities.Count == 0) continue;
    Console.WriteLine($"    {type,-25} {entities.Count,4} entities");
    foreach (var entity in entities.Take(3))
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"      └─ {entity.Label}");
        Console.ResetColor();
    }
    if (entities.Count > 3)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"      └─ ... and {entities.Count - 3} more");
        Console.ResetColor();
    }
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("═══════════════════════ HEALTH SUMMARY ═════════════════════════");
Console.ResetColor();
Console.WriteLine();
Console.WriteLine($"  {summaryResult.Answer}");
Console.WriteLine();

// === Interactive query loop ===
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("═══════════════════════ INTERACTIVE QUERY ══════════════════════");
Console.ResetColor();
Console.WriteLine("  Ask questions about your device (type 'exit' to quit):");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("  > ");
    Console.ResetColor();
    var query = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var answer = await reasoning.QueryAsync(query);
    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"  {answer.Answer}");
    Console.ResetColor();
    Console.WriteLine();
}

Console.WriteLine("  Goodbye!");
