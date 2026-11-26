using BusPing.Services;
using BusPing.Services.Interfaces;
using BusPing.ViewModels;
using BusPing.Views;
using Microsoft.Extensions.Logging;

namespace BusPing;

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

        // Services: one instance per app run, all behind interfaces.
        builder.Services.AddSingleton<IGtfsService, GtfsService>();
        builder.Services.AddSingleton<ILiveVehicleService, LiveVehicleService>();
        builder.Services.AddSingleton<IWatchRepository, JsonWatchRepository>();
        builder.Services.AddSingleton<IAlertHistoryRepository, JsonAlertHistoryRepository>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IEtaService, EtaService>();
        builder.Services.AddSingleton<IAlertEngine, AlertEngine>();
        builder.Services.AddSingleton<INotificationService, BusPing.Services.Android.AndroidNotificationService>();
        builder.Services.AddSingleton<IWatchHost, BusPing.Services.Android.AndroidWatchHost>();

        // View models.
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<RoutesViewModel>();
        builder.Services.AddTransient<WatchesViewModel>();
        builder.Services.AddTransient<AlertsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<WatchEditorViewModel>();

        // Pages and the shell.
        builder.Services.AddTransient<DashboardPage>();
        builder.Services.AddTransient<RoutesPage>();
        builder.Services.AddTransient<WatchesPage>();
        builder.Services.AddTransient<AlertsPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<WatchEditorPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
