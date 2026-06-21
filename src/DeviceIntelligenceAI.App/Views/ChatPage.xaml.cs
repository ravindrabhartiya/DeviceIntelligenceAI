using System.Diagnostics;
using System.Text;
using System.Threading;
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

    private CancellationTokenSource? _chatCts;

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

        // Cancel any in-flight query; cap each run so a slow/stuck model can't hang forever.
        _chatCts?.Cancel();
        _chatCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = _chatCts.Token;

        // Streaming answer bubble with a live elapsed-time indicator until the first token.
        var answerBubble = AddMessage("🤔 Thinking… 0s", isUser: false);
        var answerText = (TextBlock)answerBubble.Child;

        var sw = Stopwatch.StartNew();
        var sb = new StringBuilder();
        var factCount = 0;
        var template = "";
        var gotChunk = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            if (!gotChunk)
                answerText.Text = $"🤔 Thinking… {sw.Elapsed.TotalSeconds:F0}s";
        };
        timer.Start();

        try
        {
            await foreach (var ev in engine.StreamQueryAsync(question, ct).WithCancellation(ct))
            {
                if (ev.IsMetadata)
                {
                    factCount = ev.RetrievedFactCount;
                    template = ev.TemplateName ?? "";
                    continue;
                }

                if (!string.IsNullOrEmpty(ev.TextChunk))
                {
                    if (!gotChunk) { gotChunk = true; answerText.Text = ""; }
                    sb.Append(ev.TextChunk);
                    answerText.Text = sb.ToString();
                    ChatScrollViewer.ChangeView(null, ChatScrollViewer.ScrollableHeight, null);
                }
            }

            if (sb.Length == 0)
                answerText.Text = "No response was generated.";

            if (factCount > 0)
            {
                var sourcesText = new TextBlock
                {
                    Text = $"📎 Based on {factCount} facts ({template}) · {sw.Elapsed.TotalSeconds:F1}s",
                    FontSize = 11,
                    Opacity = 0.5,
                    Margin = new Thickness(12, -8, 12, 0)
                };
                ChatHistory.Children.Add(sourcesText);
            }
        }
        catch (OperationCanceledException)
        {
            answerText.Text = sb.Length > 0
                ? sb + "\n\n[Stopped — the response was taking too long.]"
                : "⏱️ The request timed out. The local model may be slow — try again, or set a smaller model via the DEVICE_AI_MODEL environment variable.";
        }
        catch (Exception ex)
        {
            answerText.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            timer.Stop();
            sw.Stop();
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
