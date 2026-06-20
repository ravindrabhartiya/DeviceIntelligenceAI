using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeviceIntelligenceAI.App.Views;

public sealed partial class TimelinePage : Page
{
    private int _days = 7;

    public TimelinePage()
    {
        this.InitializeComponent();
        this.Loaded += TimelinePage_Loaded;
    }

    private async void TimelinePage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadTimeline();
    }

    private void TimeRange_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TimeRangeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _days = int.Parse(tag);
            _ = LoadTimeline();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        App.ReasoningEngine?.InvalidateCache();
        await LoadTimeline();
    }

    private async Task LoadTimeline()
    {
        NarrativeText.Text = "Generating narrative...";
        EntitiesList.Children.Clear();

        try
        {
            // Load narrative
            var from = DateTimeOffset.UtcNow.AddDays(-_days);
            var to = DateTimeOffset.UtcNow;

            var narrative = await App.ReasoningEngine!.NarrateTimelineAsync(from, to);
            NarrativeText.Text = narrative.Answer;

            // Load recent entities from graph
            var entities = App.GraphStore?.GetEntitiesInTimeRange(from, to);
            if (entities != null)
            {
                foreach (var entity in entities.Take(20))
                {
                    var item = new Border
                    {
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12, 8, 12, 8),
                    };

                    var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    stack.Children.Add(new TextBlock
                    {
                        Text = entity.Type,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Width = 100
                    });
                    stack.Children.Add(new TextBlock { Text = entity.Label, TextTrimming = TextTrimming.CharacterEllipsis });
                    stack.Children.Add(new TextBlock
                    {
                        Text = entity.LastSeen.ToString("g"),
                        Opacity = 0.5,
                        FontSize = 12
                    });

                    item.Child = stack;
                    EntitiesList.Children.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            NarrativeText.Text = $"Error: {ex.Message}";
        }
    }
}
