using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DeviceIntelligenceAI.App.Views;

public sealed partial class DashboardPage : Page
{
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

    private async Task LoadGraphStats()
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
    }

    private async Task LoadHealthSummary()
    {
        try
        {
            if (App.ReasoningEngine == null)
            {
                HealthSummaryText.Text = "Reasoning engine not initialized.";
                return;
            }

            var result = await App.ReasoningEngine.GetHealthSummaryAsync();
            HealthSummaryText.Text = result.Answer;
        }
        catch (Exception ex)
        {
            HealthSummaryText.Text = $"Error: {ex.Message}";
        }
    }

    private async void RefreshHealth_Click(object sender, RoutedEventArgs e)
    {
        HealthSummaryText.Text = "Refreshing...";
        App.ReasoningEngine?.InvalidateCache();
        await LoadHealthSummary();
        await LoadGraphStats();
    }

    private async void UpdateRisk_Click(object sender, RoutedEventArgs e)
    {
        await RunAction("Update Risk Assessment", () => App.ReasoningEngine!.PredictUpdateRiskAsync());
    }

    private async void ExplainFailure_Click(object sender, RoutedEventArgs e)
    {
        await RunAction("Update Failure Explanation", () => App.ReasoningEngine!.ExplainUpdateFailureAsync());
    }

    private async void WhatChanged_Click(object sender, RoutedEventArgs e)
    {
        await RunAction("Recent Changes", () => App.ReasoningEngine!.NarrateTimelineAsync());
    }

    private async Task RunAction(string title, Func<Task<Reasoning.ReasoningResult>> action)
    {
        ResultCard.Visibility = Visibility.Visible;
        ResultTitle.Text = title;
        ResultText.Text = "Thinking...";
        ResultSources.Text = "";

        try
        {
            var result = await action();
            ResultText.Text = result.Answer;
            ResultSources.Text = result.Sources.Count > 0
                ? $"Based on {result.RetrievedFactCount} facts | Template: {result.TemplateName}"
                : "No evidence found";
        }
        catch (Exception ex)
        {
            ResultText.Text = $"Error: {ex.Message}";
        }
    }
}
