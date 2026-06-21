using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DeviceIntelligenceAI.Ingestion;
using DeviceIntelligenceAI.Ingestion.McpClient;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;
using DeviceIntelligenceAI.Reasoning;

namespace DeviceIntelligenceAI.App.Views;

public sealed partial class DashboardPage : Page
{
    private static readonly string McpServerPath = FindMcpServer();

    public DashboardPage()
    {
        this.InitializeComponent();
        this.Loaded += DashboardPage_Loaded;
    }

    private async void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadGraphStats();
        await LoadHealthSummary();
    }

    private Task LoadGraphStats()
    {
        try
        {
            var stats = App.GraphStore?.GetStats();
            if (stats.HasValue)
            {
                EntityCountText.Text = stats.Value.EntityCount.ToString();
                EdgeCountText.Text = stats.Value.EdgeCount.ToString();
                FactCountText.Text = stats.Value.FactCount.ToString();
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    private async Task LoadHealthSummary()
    {
        try
        {
            HealthSummaryText.Text = "⏳ Initializing AI model...";
            var engine = await App.WaitForReasoningEngineAsync();
            if (engine == null)
            {
                HealthSummaryText.Text = App.CoreServicesReady
                    ? "AI model still initializing. Click 'Refresh from Device' when ready."
                    : App.StartupError ?? "Core services failed to initialize.";
                return;
            }

            HealthSummaryText.Text = "🤔 Generating health summary...";
            var result = await engine.GetHealthSummaryAsync();
            HealthSummaryText.Text = result.Answer;
        }
        catch (Exception ex)
        {
            HealthSummaryText.Text = $"Error: {ex.Message}";
        }
    }

    private async void RefreshHealth_Click(object sender, RoutedEventArgs e)
    {
        RefreshHealthButton.IsEnabled = false;
        ProgressCard.Visibility = Visibility.Visible;
        IngestionProgress.Value = 0;
        ResetSteps();

        var sw = Stopwatch.StartNew();

        try
        {
            // Step 1: Initialize graph store
            SetStepActive(Step1Icon, Step1Text, "Initializing knowledge graph...");
            IngestionProgress.Value = 1;
            await Task.Delay(100); // Let UI update
            if (App.GraphStore == null)
            {
                SetStepFailed(Step1Icon, Step1Text, "Graph store not available");
                return;
            }
            SetStepDone(Step1Icon, Step1Text, "Graph store ready");

            // Step 2: Connect to MCP
            SetStepActive(Step2Icon, Step2Text, "Connecting to Device Intelligence MCP...");
            IngestionProgress.Value = 2;

            if (!File.Exists(McpServerPath))
            {
                SetStepFailed(Step2Icon, Step2Text, $"MCP server not found at {McpServerPath}");
                IngestionStatusText.Text = "Build the PC Health MCP project first.";
                return;
            }

            using var mcpClient = new DeviceIntelligenceMcpClient(McpServerPath);
            var initResponse = await mcpClient.InitializeAsync();
            var serverName = initResponse.RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("name")
                .GetString();
            SetStepDone(Step2Icon, Step2Text, $"Connected to {serverName}");

            // Step 3: Ingest device twin
            SetStepActive(Step3Icon, Step3Text, "Ingesting device twin — collecting WMI, EventLog, Registry...");
            IngestionProgress.Value = 3;

            var ingester = new IncrementalIngester(App.GraphStore);
            var result = await Task.Run(() => ingester.IngestFromMcpAsync(mcpClient));
            SetStepDone(Step3Icon, Step3Text,
                $"Ingested {result.Snapshot.EntityCount} entities, {result.NewFactIds.Count} facts ({result.Drift.Severity} drift)");

            // Step 4: Semantic index
            SetStepActive(Step4Icon, Step4Text, "Building semantic index...");
            IngestionProgress.Value = 4;

            var indexer = new SemanticIndexer(App.GraphStore, App.SemanticIndex!);
            var indexed = await indexer.IndexAllPendingAsync();
            SetStepDone(Step4Icon, Step4Text, $"{indexed} facts indexed for semantic search");

            // Step 5: Health summary
            SetStepActive(Step5Icon, Step5Text, "Generating health summary...");
            IngestionProgress.Value = 5;

            App.ReasoningEngine?.InvalidateCache();
            await LoadHealthSummary();
            await LoadGraphStats();
            SetStepDone(Step5Icon, Step5Text, "Health summary updated");

            sw.Stop();
            IngestionStatusText.Text = $"Completed in {sw.Elapsed.TotalSeconds:F1}s — " +
                $"{result.Snapshot.EntityCount} entities, {result.NewFactIds.Count} facts, {result.NewEdgesLinked} edges linked";
        }
        catch (Exception ex)
        {
            IngestionStatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshHealthButton.IsEnabled = true;
        }
    }

    private void SetStepActive(FontIcon icon, TextBlock text, string status)
    {
        icon.Glyph = "\uF13C"; // ProgressRing-like
        icon.Opacity = 1.0;
        text.Opacity = 1.0;
        IngestionStatusText.Text = status;
    }

    private void SetStepDone(FontIcon icon, TextBlock text, string detail)
    {
        icon.Glyph = "\uE73E"; // Checkmark
        icon.Opacity = 1.0;
        text.Opacity = 1.0;
        text.Text = detail;
    }

    private void SetStepFailed(FontIcon icon, TextBlock text, string detail)
    {
        icon.Glyph = "\uE711"; // Error
        icon.Opacity = 1.0;
        text.Opacity = 1.0;
        text.Text = detail;
    }

    private void ResetSteps()
    {
        var steps = new[] {
            (Step1Icon, Step1Text, "Initialize graph store"),
            (Step2Icon, Step2Text, "Connect to Device Intelligence MCP"),
            (Step3Icon, Step3Text, "Ingest device twin into knowledge graph"),
            (Step4Icon, Step4Text, "Build semantic index"),
            (Step5Icon, Step5Text, "Generate health summary")
        };
        foreach (var (icon, text, label) in steps)
        {
            icon.Glyph = "\uEA3A"; // Circle
            icon.Opacity = 0.4;
            text.Text = label;
            text.Opacity = 0.4;
        }
        IngestionStatusText.Text = "";
    }

    private CancellationTokenSource? _actionCts;

    private async void UpdateRisk_Click(object sender, RoutedEventArgs e)
    {
        await RunStreamedAction("Update Risk Assessment", (engine, ct) => engine.StreamUpdateRiskAsync(ct));
    }

    private async void ExplainFailure_Click(object sender, RoutedEventArgs e)
    {
        await RunStreamedAction("Update Failure Explanation", (engine, ct) => engine.StreamUpdateFailureAsync(ct: ct));
    }

    private async void WhatChanged_Click(object sender, RoutedEventArgs e)
    {
        await RunStreamedAction("Recent Changes", (engine, ct) => engine.StreamTimelineAsync(ct: ct));
    }

    private async Task RunStreamedAction(
        string title,
        Func<Reasoning.ReasoningEngine, CancellationToken, IAsyncEnumerable<RagStreamEvent>> action)
    {
        // Cancel any in-flight action; cap each run so a slow/stuck model can't hang forever.
        _actionCts?.Cancel();
        _actionCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = _actionCts.Token;

        ResultCard.Visibility = Visibility.Visible;
        ResultTitle.Text = title;
        ResultText.Text = "";
        ResultSources.Text = "";
        ResultStatusPanel.Visibility = Visibility.Visible;
        ResultProgress.IsActive = true;

        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        var phase = "Thinking";
        timer.Tick += (_, _) => ResultStatus.Text = $"{phase}… {sw.Elapsed.TotalSeconds:F0}s elapsed";
        ResultStatus.Text = "Thinking… 0s elapsed";
        timer.Start();

        var sb = new StringBuilder();
        var factCount = 0;
        var template = "";

        try
        {
            var engine = await App.WaitForReasoningEngineAsync();
            if (engine == null)
            {
                ResultText.Text = App.CoreServicesReady
                    ? "AI model still initializing. Please try again in a moment."
                    : App.StartupError ?? "Core services failed to initialize.";
                return;
            }

            await foreach (var ev in action(engine, ct).WithCancellation(ct))
            {
                if (ev.IsMetadata)
                {
                    factCount = ev.RetrievedFactCount;
                    template = ev.TemplateName ?? "";
                    phase = factCount > 0 ? $"Reading {factCount} facts" : "Generating";
                    continue;
                }

                if (!string.IsNullOrEmpty(ev.TextChunk))
                {
                    phase = "Generating";
                    sb.Append(ev.TextChunk);
                    ResultText.Text = sb.ToString();
                }
            }

            if (sb.Length == 0)
                ResultText.Text = "No response was generated.";

            ResultSources.Text = factCount > 0
                ? $"Based on {factCount} facts | Template: {template} | {sw.Elapsed.TotalSeconds:F1}s"
                : $"No evidence found | {sw.Elapsed.TotalSeconds:F1}s";
        }
        catch (OperationCanceledException)
        {
            ResultText.Text = sb.Length > 0
                ? sb + "\n\n[Stopped — the response was taking too long.]"
                : "The request timed out. The local model may be slow — try again, or set a smaller model via the DEVICE_AI_MODEL environment variable.";
        }
        catch (Exception ex)
        {
            ResultText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            timer.Stop();
            sw.Stop();
            ResultProgress.IsActive = false;
            ResultStatusPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string FindMcpServer()
    {
        var paths = new[]
        {
            @"C:\PC Health MCP\src\DeviceIntelligence.Mcp\bin\Release\net8.0-windows\win-x64\device-intelligence-mcp.exe",
            @"C:\PC Health MCP\src\DeviceIntelligence.Mcp\bin\Debug\net8.0-windows\device-intelligence-mcp.exe",
            @"C:\PC Health MCP\src\DeviceIntelligence.Mcp\bin\Release\net8.0-windows\device-intelligence-mcp.exe"
        };
        return paths.FirstOrDefault(File.Exists) ?? paths[0];
    }
}
