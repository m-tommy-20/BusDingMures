using System.Collections.ObjectModel;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed partial class DashboardViewModel : BaseViewModel
{
    private readonly IGtfsService _gtfs;
    private readonly ILiveVehicleService _live;
    private readonly IWatchRepository _watches;
    private readonly IWatchHost _host;

    public DashboardViewModel(
        IGtfsService gtfs, ILiveVehicleService live, IWatchRepository watches, IWatchHost host)
    {
        _gtfs = gtfs;
        _live = live;
        _watches = watches;
        _host = host;
        Title = "Dashboard";
        _isWatching = host.IsRunning;
    }

    public string Title { get; }

    [ObservableProperty]
    private string _scheduleStatus = "Schedule not loaded";

    [ObservableProperty]
    private string _liveStatus = "Live data not loaded";

    [ObservableProperty]
    private int _vehicleCount;

    [ObservableProperty]
    private int _activeWatchCount;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private bool _isWatching;

    public bool IsNotWatching => !IsWatching;

    public string WatchingText => IsWatching
        ? "Background watching is on. Alerts arrive even when the app is closed."
        : "Background watching is off. Start it to receive alerts on your phone.";

    partial void OnIsWatchingChanged(bool value)
    {
        OnPropertyChanged(nameof(WatchingText));
        OnPropertyChanged(nameof(IsNotWatching));
    }

    public ObservableCollection<WatchCardViewModel> Watches { get; } = [];

    public async Task InitializeAsync()
    {
        await LoadScheduleAsync();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        HasError = false;

        try
        {
            var rules = await _watches.GetAllAsync();
            Watches.Clear();
            foreach (var rule in rules.OrderBy(r => r.RouteShortName))
            {
                Watches.Add(new WatchCardViewModel(rule));
            }

            ActiveWatchCount = rules.Count(r => r.Enabled);
            IsEmpty = rules.Count == 0;
            IsWatching = _host.IsRunning;

            var vehicles = await _live.GetVehiclesAsync();
            VehicleCount = vehicles.Count;
            LiveStatus = _live.IsStale
                ? $"Live data unavailable — showing {vehicles.Count} cached vehicles"
                : $"Live: {vehicles.Count} vehicles seen {_live.LastUpdated:HH:mm:ss}";

            HasError = _live.LastError is not null;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartWatchingAsync()
    {
        try
        {
            await _host.StartAsync();
            IsWatching = _host.IsRunning;
            StatusMessage = IsWatching
                ? "Watching in the background."
                : "Android did not start the watcher. Check notification permissions.";
            HasError = !IsWatching;
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task StopWatchingAsync()
    {
        await _host.StopAsync();
        IsWatching = _host.IsRunning;
        StatusMessage = "Background watching stopped.";
        HasError = false;
    }

    private async Task LoadScheduleAsync()
    {
        try
        {
            ScheduleStatus = "Loading schedule…";
            var data = await _gtfs.LoadAsync();
            ScheduleStatus = $"Schedule: {data.Routes.Count} lines · {data.Stops.Count} stops";
        }
        catch (Exception ex)
        {
            ScheduleStatus = "Schedule unavailable";
            HasError = true;
            StatusMessage = ex.Message;
        }
    }
}
