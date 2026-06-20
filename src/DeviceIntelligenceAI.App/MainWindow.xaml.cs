using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeviceIntelligenceAI.App;

/// <summary>
/// Main window with NavigationView for switching between pages.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        Instance = this;
        this.InitializeComponent();
        Title = "Device Intelligence AI";
        ExtendsContentIntoTitleBar = true;

        // Navigate to dashboard by default
        ContentFrame.Navigate(typeof(Views.DashboardPage));
    }

    /// <summary>
    /// Navigate to a specific view based on App Action parameter.
    /// </summary>
    public void NavigateToAction(string action)
    {
        var pageType = action switch
        {
            "health" or "dashboard" => typeof(Views.DashboardPage),
            "failure" or "diagnose" => typeof(Views.ChatPage),
            "risk" => typeof(Views.ChatPage),
            "servicing" => typeof(Views.ServicingDiagramPage),
            "timeline" => typeof(Views.TimelinePage),
            _ => typeof(Views.DashboardPage)
        };

        ContentFrame.Navigate(pageType, action);
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString() ?? "dashboard";
            var pageType = tag switch
            {
                "dashboard" => typeof(Views.DashboardPage),
                "chat" => typeof(Views.ChatPage),
                "servicing" => typeof(Views.ServicingDiagramPage),
                "timeline" => typeof(Views.TimelinePage),
                _ => typeof(Views.DashboardPage)
            };
            ContentFrame.Navigate(pageType);
        }
    }
}
