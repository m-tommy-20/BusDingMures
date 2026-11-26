using System.Collections.ObjectModel;
using BusPing.Models;
using BusPing.Services;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed record DirectionOption(string DirectionId, string Label, Trip FirstTrip);

public sealed partial class RoutesViewModel : BaseViewModel
{
    private readonly IGtfsService _gtfs;
    private GtfsData? _data;
    private List<RouteItemViewModel> _allRoutes = [];

    public RoutesViewModel(IGtfsService gtfs)
    {
        _gtfs = gtfs;
        Title = "Routes";
    }

    public string Title { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private RouteItemViewModel? _selectedRoute;

    [ObservableProperty]
    private DirectionOption? _selectedDirection;

    [ObservableProperty]
    private string _timelineSummary = "Choose a line to see its stops";

    public ObservableCollection<RouteItemViewModel> Routes { get; } = [];

    public ObservableCollection<DirectionOption> Directions { get; } = [];

    public ObservableCollection<TimelineStopViewModel> Timeline { get; } = [];

    public async Task InitializeAsync()
    {
        if (_data is not null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _data = await _gtfs.LoadAsync();
            _allRoutes = _data.Routes.Values
                .Select(route => new RouteItemViewModel(
                    route,
                    _data.Trips.Values.Count(t => t.RouteId == route.Id),
                    _data.Trips.Values
                        .Where(t => t.RouteId == route.Id)
                        .SelectMany(t => _data.StopTimes(t.Id))
                        .Select(st => st.StopId)
                        .Distinct()
                        .Count()))
                .OrderBy(r => r.Route.ShortName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ApplyFilter();
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
    private async Task ReloadAsync()
    {
        _data = null;
        _allRoutes = [];
        Routes.Clear();
        Directions.Clear();
        Timeline.Clear();
        TimelineSummary = "Choose a line to see its stops";
        await InitializeAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedRouteChanged(RouteItemViewModel? value)
    {
        if (value is null || _data is null)
        {
            Directions.Clear();
            Timeline.Clear();
            return;
        }

        Directions.Clear();
        var directions = _data.Trips.Values
            .Where(t => t.RouteId == value.Route.Id)
            .GroupBy(t => t.DirectionId)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var firstTrip = g.OrderBy(t => t.Id, StringComparer.Ordinal).First();
                var label = string.IsNullOrWhiteSpace(firstTrip.Headsign)
                    ? $"Direction {g.Key}"
                    : $"→ {firstTrip.Headsign}";
                return new DirectionOption(g.Key, label, firstTrip);
            })
            .ToList();

        foreach (var direction in directions)
        {
            Directions.Add(direction);
        }

        SelectedDirection = Directions.FirstOrDefault();
    }

    partial void OnSelectedDirectionChanged(DirectionOption? value)
    {
        Timeline.Clear();
        if (value is null || _data is null)
        {
            return;
        }

        var stopTimes = _data.StopTimes(value.FirstTrip.Id);
        foreach (var stopTime in stopTimes)
        {
            if (_data.FindStop(stopTime.StopId) is { } stop)
            {
                Timeline.Add(new TimelineStopViewModel(stopTime, stop));
            }
        }

        var first = stopTimes.FirstOrDefault();
        var last = stopTimes.LastOrDefault();
        TimelineSummary = first is null || last is null
            ? "No scheduled stops for this direction"
            : $"{stopTimes.Count} stops · departs {Time(first.DepartureSeconds)} · arrives {Time(last.ArrivalSeconds)}";
    }

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allRoutes
            : _allRoutes
                .Where(r => r.Route.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            r.Route.LongName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Routes.Clear();
        foreach (var route in filtered)
        {
            Routes.Add(route);
        }
    }

    private static string Time(int seconds) => $"{seconds / 3600:00}:{seconds / 60 % 60:00}";
}
