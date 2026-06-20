using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

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
        DiagramStatus.Text = "Generating servicing pipeline diagram...";
        MermaidCode.Visibility = Visibility.Collapsed;

        try
        {
            var result = await App.ReasoningEngine!.GenerateServicingDiagramAsync();
            var code = result.Answer;

            // Extract mermaid code block if wrapped in markdown
            if (code.Contains("```mermaid"))
            {
                var start = code.IndexOf("```mermaid") + "```mermaid".Length;
                var end = code.IndexOf("```", start);
                if (end > start)
                {
                    code = code[start..end].Trim();
                }
            }

            MermaidCode.Text = code;
            MermaidCode.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
            DiagramStatus.Text = $"Generated from {result.RetrievedFactCount} facts. " +
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
