using CommunityToolkit.Maui;
using BaseLogApp.Core.Data;
using BaseLogApp.Core.ViewModels;
using BaseLogApp.Views;
using Microsoft.Extensions.Logging;

namespace BaseLogApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
        .UseMauiApp<App>()
        .UseMauiCommunityToolkit()
        .ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });

        builder.Services.AddSingleton<IJumpsReader, JumpsReader>();
        builder.Services.AddSingleton<JumpsViewModel>();
        builder.Services.AddTransient<JumpsPage>();
        builder.Services.AddTransient<SummaryPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<DbToolsPage>();
        builder.Services.AddTransient<AddRigPage>();
        builder.Services.AddTransient<AddObjectPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
