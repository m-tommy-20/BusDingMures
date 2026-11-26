using BusPing.Models;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BusPing;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, ISettingsService settings)
    {
        InitializeComponent();
        _services = services;

        UserAppTheme = settings.Current.Theme switch
        {
            AppThemeMode.Light => AppTheme.Light,
            AppThemeMode.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<AppShell>());
}
