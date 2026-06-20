using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace DeviceIntelligenceAI.App.Views;

public sealed partial class ChatPage : Page
{
    public ChatPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // If navigated via App Action with a pre-set question
        if (e.Parameter is string action && !string.IsNullOrEmpty(action))
        {
            var question = action switch
            {
                "failure" => "Why did my last Windows update fail?",
                "risk" => "Is it safe to install Windows updates right now?",
                "diagnose" => "My device feels slow. What's causing it?",
                _ => null
            };

            if (question != null)
            {
                QueryInput.Text = question;
                _ = SendQuery(question);
            }
        }
    }

    private void QueryInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = SendQuery(QueryInput.Text);
            e.Handled = true;
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendQuery(QueryInput.Text);
    }

    private async Task SendQuery(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;

        // Add user message
        AddMessage(question, isUser: true);
        QueryInput.Text = "";
        SendButton.IsEnabled = false;

        // Wait for reasoning engine if still initializing
        var engine = App.ReasoningEngine;
        if (engine == null)
        {
            var waitMsg = AddMessage("⏳ Initializing AI model...", isUser: false);
            engine = await App.WaitForReasoningEngineAsync();
            ChatHistory.Children.Remove(waitMsg);

            if (engine == null)
            {
                AddMessage(
                    App.CoreServicesReady
                        ? "❌ AI model not available. Please try again."
                        : App.StartupError ?? "❌ Core services failed to initialize.",
                    isUser: false);
                SendButton.IsEnabled = true;
                return;
            }
        }

        // Show thinking indicator
        var thinkingMsg = AddMessage("🤔 Thinking... (querying knowledge graph + SLM)", isUser: false);

        try
        {
            var result = await engine.QueryAsync(question);

            // Replace thinking with answer
            ChatHistory.Children.Remove(thinkingMsg);
            AddMessage(result.Answer, isUser: false);

            // Add sources footnote
            if (result.Sources?.Count > 0)
            {
                var sourcesText = new TextBlock
                {
                    Text = $"📎 Based on {result.RetrievedFactCount} facts ({result.TemplateName})",
                    FontSize = 11,
                    Opacity = 0.5,
                    Margin = new Thickness(12, -8, 12, 0)
                };
                ChatHistory.Children.Add(sourcesText);
            }
        }
        catch (Exception ex)
        {
            ChatHistory.Children.Remove(thinkingMsg);
            AddMessage($"❌ Error: {ex.Message}", isUser: false);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }

        // Scroll to bottom
        ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
    }

    private Border AddMessage(string text, bool isUser)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 600,
            Margin = new Thickness(0, 4, 0, 4)
        };

        if (isUser)
        {
            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 100, 200));
        }
        else
        {
            border.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 40, 40, 40));
        }

        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.White)
        };

        border.Child = textBlock;
        ChatHistory.Children.Add(border);
        return border;
    }
}
