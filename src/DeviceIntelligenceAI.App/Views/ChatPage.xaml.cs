using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        if (App.ReasoningEngine == null)
        {
            var waitBorder = AddMessage("Initializing AI model (first time may take a moment)...", isUser: false);
            for (int i = 0; i < 30 && App.ReasoningEngine == null; i++)
                await Task.Delay(500);

            ChatHistory.Children.Remove(waitBorder);

            if (App.ReasoningEngine == null)
            {
                AddMessage("AI model not available. Please try again in a moment.", isUser: false);
                SendButton.IsEnabled = true;
                return;
            }
        }

        // Add thinking indicator
        var thinkingBorder = AddMessage("Thinking...", isUser: false);

        try
        {
            var result = await App.ReasoningEngine.QueryAsync(question);

            // Replace thinking with answer
            ChatHistory.Children.Remove(thinkingBorder);
            AddMessage(result.Answer, isUser: false);

            // Add sources footnote
            if (result.Sources.Count > 0)
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
            ChatHistory.Children.Remove(thinkingBorder);
            AddMessage($"Error: {ex.Message}", isUser: false);
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
            Background = isUser
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 600
        };

        var textBlock = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isUser
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
                : null
        };

        border.Child = textBlock;
        ChatHistory.Children.Add(border);
        return border;
    }
}
