using BusPing.Views;
using Microsoft.Extensions.DependencyInjection;

namespace BusPing;

public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();

        var tabs = new TabBar();
        tabs.Items.Add(CreateTab("Dashboard", services.GetRequiredService<DashboardPage>));
        tabs.Items.Add(CreateTab("Routes", services.GetRequiredService<RoutesPage>));
        tabs.Items.Add(CreateTab("Watches", services.GetRequiredService<WatchesPage>));
        tabs.Items.Add(CreateTab("Alerts", services.GetRequiredService<AlertsPage>));
        tabs.Items.Add(CreateTab("Settings", services.GetRequiredService<SettingsPage>));

        Items.Add(tabs);

        Routing.RegisterRoute(nameof(WatchEditorPage), new DiRouteFactory<WatchEditorPage>(services));
    }

    /// <summary>Creates a page from the DI container for a registered Shell route.</summary>
    private sealed class DiRouteFactory<TPage>(IServiceProvider services) : RouteFactory
        where TPage : Element
    {
        public override Element GetOrCreate() => services.GetRequiredService<TPage>();

        public override Element GetOrCreate(IServiceProvider serviceProvider) =>
            serviceProvider.GetRequiredService<TPage>();
    }

    /// <summary>
    /// A tab whose content is resolved from the DI container instead of being
    /// instantiated by XAML, so pages can take constructor dependencies.
    /// </summary>
    private static ShellContent CreateTab(string title, Func<Page> factory) => new()
    {
        Title = title,
        Route = title,
        ContentTemplate = new DataTemplate(() => factory()),
    };
}
