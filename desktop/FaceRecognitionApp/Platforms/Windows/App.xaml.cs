namespace FaceRecognitionApp.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static async void OnUnhandledException(object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var xamlRoot = (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.Content?.XamlRoot;
        if (xamlRoot is null) return;

        try
        {
            await new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "nieoczekiwany blad",
                Content = e.Exception?.Message ?? e.Message,
                CloseButtonText = "ok",
                XamlRoot = xamlRoot,
            }.ShowAsync();
        }
        catch { }
    }
}
