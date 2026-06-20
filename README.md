# Device Intelligence AI

> **Turning the Windows device into a continuously maintained semantic knowledge graph with a callable action surface for agents.**

A local MCP server that builds and maintains a semantic knowledge graph of your Windows device state — health, updates, drivers, reliability, performance, security — and exposes AI-powered reasoning over that graph via MCP tools. Any AI agent can query the graph to get grounded, evidence-based answers.

## Architecture

```
Agent (Copilot CLI / VS Code / Claude) → MCP (stdio) → Device Intelligence AI
                                                              ↓
                                                     Knowledge Graph (SQLite)
                                                     Semantic Index (Windows AI / Local)
                                                     Reasoning Engine (Phi Silica / Mock)
                                                              ↓
                                                     Device Intelligence MCP (existing)
                                                              ↓
                                                     Windows (WMI, EventLog, CBS, etc.)
```

## MCP Tools

| Tool | Purpose |
|------|---------|
| `query_knowledge_graph` | Natural language query → reasoned answer with citations |
| `get_health_summary` | Device health narrative |
| `explain_update_failure` | Root cause analysis for update failures |
| `predict_update_risk` | Is it safe to update? |
| `get_device_narrative` | What happened on this device recently? |
| `get_causal_chain` | Trace cause → effect for failures |
| `generate_servicing_diagram` | Mermaid diagram of servicing pipeline |
| `ingest_device_twin` | Feed device twin JSON into the graph |
| `get_graph_stats` | Entity/edge/fact counts |

## Quick Start

```pwsh
# Build
dotnet build

# Run tests (30 passing)
dotnet test

# Run the MCP server
dotnet run --project src/DeviceIntelligenceAI.McpServer
```

### Hook up to Copilot CLI

```pwsh
copilot mcp add device-intelligence-ai -- dotnet run --project "C:\Windows AI APIs\DeviceIntelligenceAI\src\DeviceIntelligenceAI.McpServer"
```

### Hook up to VS Code

Add to `.vscode/mcp.json`:
```json
{
  "servers": {
    "device-intelligence-ai": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "C:\\Windows AI APIs\\DeviceIntelligenceAI\\src\\DeviceIntelligenceAI.McpServer"]
    }
  }
}
```

## Knowledge Graph

- **Entities**: Device, OsBuild, Update, Driver, HardwareComponent, Failure, PerformanceSample, SecurityPosture, ServicingOperation
- **Edges**: installed, failed_on, drives, caused_by, preceded, degraded, blocked_by, depends_on, changed
- **Temporal**: 30-day rolling window, every entity carries first_seen/last_seen
- **Storage**: SQLite in `%LOCALAPPDATA%\device-intelligence-ai\knowledge-graph.db`

## Windows Copilot Runtime Integration

On Copilot+ PCs with Windows App SDK 1.8+:
- **Phi Silica** (LanguageModel API) for on-device reasoning
- **AppContentIndexer** for semantic search over device facts
- **Content Moderation** for output safety

On other machines: falls back to keyword search + mock LLM (for development/testing).

## Project Structure

```
src/
├── DeviceIntelligenceAI.Graph/        # Knowledge graph (SQLite, entities, edges, traversal)
├── DeviceIntelligenceAI.Ingestion/    # MCP client, fact indexing, drift detection
├── DeviceIntelligenceAI.Reasoning/    # Phi Silica, RAG pipeline, prompt templates, cache
├── DeviceIntelligenceAI.Visualization/ # Servicing diagram generation (WIP)
└── DeviceIntelligenceAI.McpServer/    # MCP server (stdio JSON-RPC)
tests/
├── DeviceIntelligenceAI.Graph.Tests/
└── DeviceIntelligenceAI.Ingestion.Tests/
```

## License

MIT
