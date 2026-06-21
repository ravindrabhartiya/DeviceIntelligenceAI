using Microsoft.UI.Xaml;
using DeviceIntelligenceAI.Graph;
using DeviceIntelligenceAI.Ingestion.SemanticIndex;
using DeviceIntelligenceAI.Reasoning;

namespace DeviceIntelligenceAI.App;

/// <summary>
/// Application entry point. Handles App Action activation and navigation.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    // Shared services. GraphStore/SemanticIndex are created synchronously at startup.
    internal static GraphStore? GraphStore { get; private set; }
    internal static ISemanticIndex? SemanticIndex { get; private set; }

    // ReasoningEngine is published from a background continuation and read from the UI
    // thread, so the backing field is volatile to guarantee visibility across threads.
    private static volatile ReasoningEngine? _reasoningEngine;
    internal static ReasoningEngine? ReasoningEngine => _reasoningEngine;

    /// <summary>True once the core services (graph + semantic index) initialized successfully.</summary>
    internal static bool CoreServicesReady { get; private set; }

    /// <summary>Human-readable startup failure message, surfaced by pages when init fails.</summary>
    internal static string? StartupError { get; private set; }

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 1. Core services first (synchronous). Must never crash the app on launch.
        InitializeCoreServices();

        // 2. Show the window.
        _window = new MainWindow();
        _window.Activate();

        // 3. Initialize the language model in the background (Phi Silica → Ollama → Mock).
        _ = InitializeLlmAsync();

        // 4. Honor App Action launch arguments (Windows Search / Copilot).
        HandleAppAction(args.Arguments);
    }

    private void InitializeCoreServices()
    {
        try
        {
            var dbPath = GetDatabasePath();
            GraphStore = new GraphStore(dbPath);
            // Enforce the 30-day rolling window on startup.
            GraphStore.PruneOlderThan(DateTimeOffset.UtcNow.AddDays(-30));
            SemanticIndex = SemanticIndexFactory.Create(forceLocal: !WindowsSemanticIndex.IsAvailable());
            CoreServicesReady = true;
        }
        catch (Exception ex)
        {
            CoreServicesReady = false;
            StartupError = $"Failed to initialize core services: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] {StartupError}");
        }
    }

    private async Task InitializeLlmAsync()
    {
        // Reasoning requires the graph + index; skip entirely if core init failed.
        if (!CoreServicesReady || GraphStore == null || SemanticIndex == null)
            return;

        try
        {
            // Rehydrate the in-memory semantic index from persisted facts so prior device
            // data is searchable without re-running a scan.
            try
            {
                var rehydrated = await new SemanticIndexer(GraphStore, SemanticIndex).RehydrateAsync();
                System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] Rehydrated {rehydrated} facts into semantic index.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] Index rehydration failed: {ex.Message}");
            }

            var (llm, backend) = await LanguageModelFactory.CreateAsync();
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] LLM backend: {backend}");
            _reasoningEngine = new ReasoningEngine(SemanticIndex, llm, GraphStore);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] LLM init failed: {ex.Message}");
            _reasoningEngine = new ReasoningEngine(SemanticIndex, new MockLanguageModel(), GraphStore);
        }
    }

    /// <summary>
    /// Wait for the asynchronously-initialized <see cref="ReasoningEngine"/> to become
    /// available. Returns the engine, or null if core services failed or the timeout
    /// elapsed. Safe to call from the UI thread; pages use this as the single readiness gate.
    /// </summary>
    internal static async Task<ReasoningEngine?> WaitForReasoningEngineAsync(TimeSpan? timeout = null)
    {
        if (!CoreServicesReady) return null;

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (_reasoningEngine == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
        return _reasoningEngine;
    }

    private void HandleAppAction(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments)) return;

        // Parse "action=check-health" style arguments (see Package.appxmanifest).
        var parts = arguments.Split('=', 2);
        if (parts.Length != 2 || parts[0] != "action") return;

        var navParam = parts[1] switch
        {
            "check-health" => "health",
            "explain-update-failure" => "failure",
            "update-readiness" => "risk",
            "show-servicing-state" => "servicing",
            "what-changed" => "timeline",
            "diagnose-slow" => "diagnose",
            _ => "dashboard"
        };

        // MainWindow hosts a NavigationView (not a Frame), so navigate through it directly.
        MainWindow.Instance?.NavigateToAction(navParam);
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
