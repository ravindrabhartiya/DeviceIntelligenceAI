using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using DeviceIntelligenceAI.Graph.Schema;
using System.Text;

namespace DeviceIntelligenceAI.App.Views;

public sealed partial class ServicingDiagramPage : Page
{
    public ServicingDiagramPage()
    {
        this.InitializeComponent();
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        GenerateButton.IsEnabled = false;
        DiagramStatus.Text = "Building diagram from knowledge graph...";
        MermaidCode.Visibility = Visibility.Collapsed;

        try
        {
            var code = await Task.Run(BuildServicingDiagramFromGraph);

            MermaidCode.Text = code;
            MermaidCode.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;

            var stats = App.GraphStore?.GetStats();
            DiagramStatus.Text = $"Generated from live knowledge graph ({stats?.EntityCount ?? 0} entities, {stats?.EdgeCount ?? 0} edges). " +
                                 "Copy the Mermaid code below and paste into mermaid.live to render.";
        }
        catch (Exception ex)
        {
            DiagramStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private string BuildServicingDiagramFromGraph()
    {
        var store = App.GraphStore;
        if (store == null) return "graph LR\n  NoData[Knowledge graph not initialized]";

        var sb = new StringBuilder();

        // Get real entities from the graph
        var device = store.GetEntitiesByType(EntityTypes.Device).FirstOrDefault();
        var osBuild = store.GetEntitiesByType(EntityTypes.OsBuild).FirstOrDefault();
        var updates = store.GetEntitiesByType(EntityTypes.Update);
        var drivers = store.GetEntitiesByType(EntityTypes.Driver);
        var failures = store.GetEntitiesByType(EntityTypes.Failure);
        var security = store.GetEntitiesByType(EntityTypes.SecurityPosture).FirstOrDefault();
        var servicingOps = store.GetEntitiesByType(EntityTypes.ServicingOperation);

        sb.AppendLine("graph TD");
        sb.AppendLine("  %% Device Intelligence AI — Live Servicing State");
        sb.AppendLine();

        // Device node
        if (device != null)
        {
            sb.AppendLine($"  DEV[\"{device.Label}\"]");
            sb.AppendLine("  style DEV fill:#1a73e8,color:#fff");
        }

        // OS Build
        if (osBuild != null)
        {
            var buildLabel = osBuild.Label.Length > 40 ? osBuild.Label[..40] + "..." : osBuild.Label;
            sb.AppendLine($"  OS[\"{buildLabel}\"]");
            sb.AppendLine("  DEV --> OS");
        }

        // Security posture
        if (security != null)
        {
            sb.AppendLine($"  SEC[\"🛡️ {security.Label}\"]");
            sb.AppendLine("  DEV --> SEC");
        }

        // Updates (show all, mark status)
        if (updates.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  subgraph Updates[\"Windows Updates\"]");
            foreach (var update in updates.Take(15))
            {
                var id = SanitizeId(update.Id);
                var label = update.Label.Length > 50 ? update.Label[..50] + "..." : update.Label;
                sb.AppendLine($"    {id}[\"{label}\"]");
            }
            sb.AppendLine("  end");
            sb.AppendLine("  OS --> Updates");

            if (updates.Count > 15)
                sb.AppendLine($"  %% ... and {updates.Count - 15} more updates");
        }

        // Drivers (summarize by category)
        if (drivers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"  subgraph Drivers[\"{drivers.Count} Drivers Installed\"]");
            // Show top 5 most recent or notable
            foreach (var driver in drivers.Take(5))
            {
                var id = SanitizeId(driver.Id);
                var label = driver.Label.Length > 45 ? driver.Label[..45] + "..." : driver.Label;
                sb.AppendLine($"    {id}[\"{label}\"]");
            }
            if (drivers.Count > 5)
            {
                sb.AppendLine($"    MORE_DRV[\"... +{drivers.Count - 5} more\"]");
            }
            sb.AppendLine("  end");
            sb.AppendLine("  DEV --> Drivers");
        }

        // Failures (highlight in red)
        if (failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  subgraph Failures[\"⚠️ Failures\"]");
            foreach (var failure in failures.Take(10))
            {
                var id = SanitizeId(failure.Id);
                var label = failure.Label.Length > 50 ? failure.Label[..50] + "..." : failure.Label;
                sb.AppendLine($"    {id}[\"{label}\"]");
                sb.AppendLine($"    style {id} fill:#d32f2f,color:#fff");
            }
            sb.AppendLine("  end");
            sb.AppendLine("  OS --> Failures");
        }

        // Servicing operations
        if (servicingOps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  subgraph ServicingOps[\"Servicing Operations\"]");
            foreach (var op in servicingOps.Take(10))
            {
                var id = SanitizeId(op.Id);
                var label = op.Label.Length > 50 ? op.Label[..50] + "..." : op.Label;
                sb.AppendLine($"    {id}[\"{label}\"]");
            }
            sb.AppendLine("  end");
            sb.AppendLine("  OS --> ServicingOps");
        }

        // If graph is mostly empty, add a note
        if (updates.Count == 0 && failures.Count == 0 && servicingOps.Count == 0 && drivers.Count == 0)
        {
            sb.AppendLine("  NOTE[\"Run 'Refresh from Device' on Dashboard to ingest device state\"]");
            sb.AppendLine("  style NOTE fill:#ff9800,color:#fff");
        }

        return sb.ToString();
    }

    private static string SanitizeId(string id)
    {
        // Mermaid IDs can't have special chars
        return "n_" + id.Replace("-", "_").Replace(".", "_").Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(MermaidCode.Text);
        Clipboard.SetContent(package);
        CopyButton.Content = "Copied!";
        _ = ResetCopyButton();
    }

    private async Task ResetCopyButton()
    {
        await Task.Delay(2000);
        CopyButton.Content = "Copy Mermaid Code";
    }
}
