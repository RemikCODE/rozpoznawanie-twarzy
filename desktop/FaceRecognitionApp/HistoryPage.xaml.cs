using FaceRecognitionApp.Models;
using FaceRecognitionApp.Services;

namespace FaceRecognitionApp;

public partial class HistoryPage : ContentPage
{
    private readonly ApiService _apiService;

    public HistoryPage(ApiService apiService)
    {
        InitializeComponent();
        _apiService = apiService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAsync();

    private async void OnPullRefreshing(object sender, EventArgs e)
    {
        await LoadAsync();
        RefreshView.IsRefreshing = false;
    }

    private async Task LoadAsync()
    {
        ShowLoading(true);
        ErrorPanel.IsVisible = false;

        try
        {
            var logs = await _apiService.GetRecentLogsAsync(20);
            var items = logs.Select(HistoryItem.From).ToList();
            ResultsList.ItemsSource = items;
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = $"nie mozna zaladowac historii\n\n{ex.Message}\n\nupewnij sie ze serwer backendu dziala na porcie 5233";
            ErrorPanel.IsVisible = true;
            ResultsList.ItemsSource = null;
        }
        finally
        {
            ShowLoading(false);
        }
    }

    private void ShowLoading(bool loading)
    {
        LoadingPanel.IsVisible = loading;
        RefreshView.IsVisible = !loading;
        RefreshButton.IsEnabled = !loading;
    }
}
