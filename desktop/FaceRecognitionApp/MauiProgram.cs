using FaceRecognitionApp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;

namespace FaceRecognitionApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddHttpClient<ApiService>();
        builder.Services.AddSingleton<ApiService>();

        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<AddPersonPage>();
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
