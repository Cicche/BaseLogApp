using CommunityToolkit.Maui;
using BaseLog.Data;
using BaseLog.ViewModels;
using BaseLog.Views;

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

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "baselog.db3");

        builder.Services.AddSingleton<IJumpsRepository>(_ => new JumpsRepository(dbPath));
        builder.Services.AddTransient<JumpsPageViewModel>();
        builder.Services.AddTransient<JumpsPage>();

        return builder.Build();
    }
}
