using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    // Shared services
    internal static GraphStore? GraphStore { get; private set; }
    internal static ISemanticIndex? SemanticIndex { get; private set; }
    internal static ReasoningEngine? ReasoningEngine { get; private set; }

    public App()
    {
        this.InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Show window immediately, initialize services in background
        InitializeCoreServices();

        _window = new MainWindow();
        _window.Activate();

        // Initialize LLM async (may involve HTTP calls to Ollama)
        _ = InitializeLlmAsync();

        // Check if launched via App Action
        var launchArgs = args.Arguments;
        if (!string.IsNullOrEmpty(launchArgs))
        {
            HandleAppAction(launchArgs);
        }
    }

    private void InitializeCoreServices()
    {
        var dbPath = GetDatabasePath();
        GraphStore = new GraphStore(dbPath);
        SemanticIndex = SemanticIndexFactory.Create(forceLocal: !WindowsSemanticIndex.IsAvailable());
    }

    private async Task InitializeLlmAsync()
    {
        try
        {
            var (llm, backend) = await LanguageModelFactory.CreateAsync();
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] LLM backend: {backend}");
            ReasoningEngine = new ReasoningEngine(SemanticIndex!, llm, GraphStore!);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DeviceIntelligenceAI] LLM init failed: {ex.Message}");
            var mock = new MockLanguageModel();
            ReasoningEngine = new ReasoningEngine(SemanticIndex!, mock, GraphStore!);
        }
    }

    private void HandleAppAction(string arguments)
    {
        // Parse "action=check-health" style arguments
        var parts = arguments.Split('=', 2);
        if (parts.Length != 2 || parts[0] != "action") return;

        var action = parts[1];
        // Navigate to appropriate view based on action
        if (_window?.Content is Frame rootFrame)
        {
            var navParam = action switch
            {
                "check-health" => "health",
                "explain-update-failure" => "failure",
                "update-readiness" => "risk",
                "show-servicing-state" => "servicing",
                "what-changed" => "timeline",
                "diagnose-slow" => "diagnose",
                _ => "dashboard"
            };

            // MainWindow handles navigation via its NavigationView
            MainWindow.Instance?.NavigateToAction(navParam);
        }
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
