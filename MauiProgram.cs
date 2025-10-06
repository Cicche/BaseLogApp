using CommunityToolkit.Maui;
using BaseLogApp.Data;
using BaseLogApp.ViewModels;
//using BaseLogApp.Views;
using BaseLogApp;

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

        //var dbPath = Path.Combine(FileSystem.AppDataDirectory, "baselog.db3");


        var dbPath =
            #if WINDOWS
                  @"C:\temp\BASELogbook.sqlite";
            #else
                Path.Combine(FileSystem.AppDataDirectory, "BASELogbook.sqlite");
            #endif

        


       
        builder.Services.AddSingleton<IJumpsRepository>(_ => new JumpsRepository(dbPath));
        builder.Services.AddTransient<JumpsPageViewModel>();
        builder.Services.AddTransient<JumpsPage>();

        return builder.Build();
    }
}
