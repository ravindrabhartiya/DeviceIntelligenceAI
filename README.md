# Device Intelligence AI

> **Turning the Windows device into a continuously maintained semantic knowledge graph with a callable action surface for agents.**

A WinUI 3 app + MCP server that builds and maintains a semantic knowledge graph of your Windows device state — health, updates, drivers, reliability, performance, security — and exposes AI-powered reasoning via both a visual UI and MCP tools. Any AI agent can query the graph to get grounded, evidence-based answers, and users can interact through Windows Search, Copilot, or the app directly.

## Architecture

```
┌──────────────────────────────────────────────────────────────────┐
│  WinUI 3 App (MSIX)                                              │
│  ┌─────────┐ ┌──────┐ ┌───────────┐ ┌──────────┐               │
│  │Dashboard│ │ Chat │ │ Servicing │ │ Timeline │               │
│  └────┬────┘ └──┬───┘ └─────┬─────┘ └────┬─────┘               │
│       └─────────┴───────────┴─────────────┘                      │
│                         ↓                                         │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │ Reasoning Engine (Phi Silica / Mock LLM)                    │ │
│  │ RAG Pipeline · Prompt Templates · Insight Cache             │ │
│  └─────────────────────────┬───────────────────────────────────┘ │
│                            ↓                                      │
│  ┌──────────────────────────────────────────────────────────────┐│
│  │ Knowledge Graph (SQLite) · Semantic Index (AppContentIndexer)││
│  │ 115+ entities · typed edges · temporal facts · drift detect  ││
│  └──────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────┘
         ↕ MCP (stdio)                    ↕ App Actions (OS)
┌────────────────────┐           ┌──────────────────────────┐
│ AI Agents          │           │ Windows Search / Copilot │
│ (CLI, VS Code,    │           │ "Is it safe to update?"  │
│  Claude, etc.)     │           │ "Why did update fail?"   │
└────────────────────┘           └──────────────────────────┘
         ↓
┌──────────────────────────────────────────────────────────────────┐
│ Device Intelligence MCP (existing @ C:\PC Health MCP)            │
│ WMI · EventLog · CBS · Registry · Performance Counters           │
└──────────────────────────────────────────────────────────────────┘
```

## WinUI 3 App

The app provides a visual interface for interacting with the device knowledge graph:

- **Dashboard** — Health summary, graph stats, quick actions with live ingestion progress
- **Chat** — Natural language queries ("Why did KB5041585 fail?")
- **Servicing** — Mermaid diagram generation of update pipeline state
- **Timeline** — Temporal fact browser with date range filtering

### App Actions (Windows Search / Copilot Integration)

The app declares 6 App Actions so Windows Search and Copilot can invoke it directly:

| Action | Trigger phrase |
|--------|---------------|
| `CheckDeviceHealth` | "Check my device health" |
| `ExplainUpdateFailure` | "Why did my update fail?" |
| `AssessUpdateReadiness` | "Is it safe to update?" |
| `ShowServicingState` | "Show servicing state" |
| `WhatChanged` | "What changed on my device?" |
| `DiagnoseSlow` | "Why is my PC slow?" |

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
# Build everything
dotnet build

# Run tests (30 passing)
dotnet test

# Run the MCP server (for AI agents)
dotnet run --project src/DeviceIntelligenceAI.McpServer

# Run live ingestion from your device (console)
dotnet run --project tools/LiveIngest

# Launch the WinUI app
dotnet build src/DeviceIntelligenceAI.App -p:Platform=x64
.\src\DeviceIntelligenceAI.App\bin\x64\Debug\net8.0-windows10.0.22621.0\DeviceIntelligenceAI.App.exe
```

### Live Ingestion

The `tools/LiveIngest` console app connects to the Device Intelligence MCP server, builds the knowledge graph from real device state, indexes facts for semantic search, and drops into an interactive query session:

```
╔══════════════════════════════════════════════════════════════╗
║         Device Intelligence AI — Live Ingestion             ║
╚══════════════════════════════════════════════════════════════╝

  [1/5] Initializing knowledge graph... ✓
  [2/5] Connecting to Device Intelligence MCP... ✓ (device-intelligence-mcp)
  [3/5] Ingesting device twin into knowledge graph... ✓ (17819ms)
  [4/5] Building semantic index... ✓ (115 facts indexed)
  [5/5] Generating health summary... (Mock) ✓

═══════════════════════ KNOWLEDGE GRAPH ═════════════════════════

  Total entities: 115
  Total edges:    1
  Total facts:    115

    Device                       1 entities
    OsBuild                      1 entities
    Driver                     112 entities
    SecurityPosture              1 entities
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

### Hardware Requirements

| Feature | Requirement |
|---------|-------------|
| Full AI (NPU) | Copilot+ PC (Snapdragon X, Ryzen AI 300+, Intel Core Ultra) |
| Full AI (GPU) | NVIDIA RTX 30+ with 6GB+ vRAM, Developer Mode enabled |
| Fallback mode | Any Windows 11 machine (keyword search + mock LLM) |

## Project Structure

```
src/
├── DeviceIntelligenceAI.App/             # WinUI 3 app (MSIX, App Actions, 4 pages)
├── DeviceIntelligenceAI.Graph/           # Knowledge graph (SQLite, entities, edges, traversal)
├── DeviceIntelligenceAI.Ingestion/       # MCP client, fact indexing, drift detection
├── DeviceIntelligenceAI.Reasoning/       # Phi Silica, RAG pipeline, prompt templates, cache
└── DeviceIntelligenceAI.McpServer/       # MCP server (stdio JSON-RPC, 9 tools, 2 resources)
tools/
└── LiveIngest/                           # Console app for live device ingestion + interactive query
tests/
├── DeviceIntelligenceAI.Graph.Tests/     # 12 tests
└── DeviceIntelligenceAI.Ingestion.Tests/ # 18 tests
```

## Prerequisites

- .NET 8 SDK
- Windows 11 (Build 22621+)
- Windows App SDK 1.6+ (for WinUI app)
- [Device Intelligence MCP](https://github.com/ravindrabhartiya/device-intelligence-mcp) server built and available on PATH (for live ingestion)

## License

MIT
