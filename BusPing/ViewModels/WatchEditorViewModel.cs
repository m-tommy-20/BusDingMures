using System.Collections.ObjectModel;
using BusPing.Models;
using BusPing.Services;
using BusPing.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BusPing.ViewModels;

public sealed record RouteOption(string Id, string ShortName, string Label);

/// <summary>One stop in the editor's timeline, with selection state.</summary>
public sealed partial class EditorStopViewModel : ObservableObject
{
    private bool _isBoarding;
    private bool _isAlert;
    private bool _canAlert;

    public EditorStopViewModel(StopTime stopTime, Stop stop, bool isIncoming = false)
    {
        StopTime = stopTime;
        Stop = stop;
        IsIncoming = isIncoming;
    }

    public StopTime StopTime { get; }
    public Stop Stop { get; }
    public bool IsIncoming { get; }

    public string Name => Stop.Name;
    public string DisplayName => IsIncoming ? $"{Stop.Name}  (incoming)" : Stop.Name;
    public string Time => $"{StopTime.Arrival.Hours:00}:{StopTime.Arrival.Minutes:00}";

    public bool IsBoarding
    {
        get => _isBoarding;
        set
        {
            if (SetProperty(ref _isBoarding, value))
            {
                OnPropertyChanged(nameof(BoardingButtonText));
            }
        }
    }

    public bool CanAlert
    {
        get => _canAlert;
        set => SetProperty(ref _canAlert, value);
    }

    public bool IsAlert
    {
        get => _isAlert;
        set
        {
            if (SetProperty(ref _isAlert, value))
            {
                OnPropertyChanged(nameof(AlertButtonText));
            }
        }
    }

    public string BoardingButtonText => IsBoarding ? "Boarding ✓" : "Board";
    public string AlertButtonText => IsAlert ? "Warn ✓" : "Warn";
}

public sealed partial class WatchEditorViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IGtfsService _gtfs;
    private readonly IWatchRepository _repository;
    private readonly ISettingsService _settings;

    private GtfsData? _data;
    private Guid? _editingId;
    private WatchRule? _existing;
    private bool _initialized;

    public WatchEditorViewModel(
        IGtfsService gtfs, IWatchRepository repository, ISettingsService settings)
    {
        _gtfs = gtfs;
        _repository = repository;
        _settings = settings;

        Title = "New watch";
        _leadMinutes = settings.Current.DefaultLeadMinutes;

        // A brand-new watch defaults to a window around the current time, so the
        // user can try it immediately instead of waiting for tomorrow morning.
        var now = DateTimeOffset.Now;
        var startMinutes = Math.Max(0, now.Hour * 60 + now.Minute - 30);
        _startTime = TimeSpan.FromMinutes(startMinutes);
        _endTime = TimeSpan.FromMinutes(Math.Min(23 * 60 + 59, startMinutes + 180));
    }

    public string Title { get; private set; }

    public string[] DayPresets { get; } = ["Every day", "Weekdays", "Weekend"];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private RouteOption? _selectedRoute;

    [ObservableProperty]
    private DirectionOption? _selectedDirection;

    [ObservableProperty]
    private int _dayPresetIndex = 1;

    [ObservableProperty]
    private TimeSpan _startTime;

    [ObservableProperty]
    private TimeSpan _endTime;

    [ObservableProperty]
    private int _leadMinutes;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _selectionSummary = "Pick a line, then choose your boarding stop.";

    public ObservableCollection<RouteOption> Routes { get; } = [];

    public ObservableCollection<DirectionOption> Directions { get; } = [];

    public ObservableCollection<EditorStopViewModel> Stops { get; } = [];

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("watchId", out var value) &&
            Guid.TryParse(value?.ToString(), out var id))
        {
            _editingId = id;
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        IsBusy = true;

        try
        {
            _data = await _gtfs.LoadAsync();

            foreach (var route in _data.Routes.Values
                         .OrderBy(r => r.ShortName, StringComparer.OrdinalIgnoreCase))
            {
                Routes.Add(new RouteOption(
                    route.Id,
                    route.ShortName,
                    string.IsNullOrWhiteSpace(route.LongName)
                        ? route.ShortName
                        : $"{route.ShortName} · {route.LongName}"));
            }

            if (_editingId is { } id)
            {
                _existing = await _repository.GetAsync(id);
                if (_existing is not null)
                {
                    IsEditing = true;
                    Title = "Edit watch";
                    OnPropertyChanged(nameof(Title));

                    Name = _existing.Name;
                    DayPresetIndex = PresetFor(_existing.Days);
                    StartTime = TimeSpan.FromMinutes(_existing.WindowStartMinutes);
                    EndTime = TimeSpan.FromMinutes(_existing.WindowEndMinutes);
                    LeadMinutes = _existing.LeadMinutes;

                    SelectedRoute = Routes.FirstOrDefault(r => r.Id == _existing.RouteId);
                }
            }

            SelectedRoute ??= Routes.FirstOrDefault();
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

    partial void OnSelectedRouteChanged(RouteOption? value)
    {
        Directions.Clear();
        Stops.Clear();
        if (value is null || _data is null)
        {
            return;
        }

        foreach (var group in _data.Trips.Values
                     .Where(t => t.RouteId == value.Id)
                     .GroupBy(t => t.DirectionId)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var firstTrip = group.OrderBy(t => t.Id, StringComparer.Ordinal).First();
            var label = string.IsNullOrWhiteSpace(firstTrip.Headsign)
                ? $"Direction {group.Key}"
                : $"→ {firstTrip.Headsign}";

            Directions.Add(new DirectionOption(group.Key, label, firstTrip));
        }

        SelectedDirection = _existing is not null
            ? Directions.FirstOrDefault(d => d.DirectionId == _existing.DirectionId) ?? Directions.FirstOrDefault()
            : Directions.FirstOrDefault();
    }

    private readonly List<EditorStopViewModel> _forwardStops = [];

    partial void OnSelectedDirectionChanged(DirectionOption? value)
    {
        Stops.Clear();
        _forwardStops.Clear();
        if (value is null || _data is null)
        {
            return;
        }

        foreach (var stopTime in _data.StopTimes(value.FirstTrip.Id))
        {
            if (_data.FindStop(stopTime.StopId) is { } stop)
            {
                _forwardStops.Add(new EditorStopViewModel(stopTime, stop));
            }
        }

        // The bus that will run this trip arrives from the opposite direction.
        // When boarding is near the start, its last stops are useful warnings.
        foreach (var item in BuildIncoming(value.FirstTrip.RouteId, value.DirectionId))
        {
            Stops.Add(item);
        }

        foreach (var item in _forwardStops)
        {
            Stops.Add(item);
        }

        if (_existing is not null)
        {
            var boarding = _forwardStops.FirstOrDefault(s => s.StopTime.Sequence == _existing.BoardingSequence)
                           ?? _forwardStops.FirstOrDefault(s => s.Stop.Id == _existing.BoardingStopId);
            if (boarding is not null)
            {
                SelectBoarding(boarding, notify: false);
            }

            foreach (var alertStop in _existing.AlertStops)
            {
                var item = alertStop.IsIncoming
                    ? Stops.FirstOrDefault(s => s.IsIncoming && s.Stop.Id == alertStop.StopId)
                    : _forwardStops.FirstOrDefault(s =>
                        s.Stop.Id == alertStop.StopId && s.StopTime.Sequence == alertStop.StopSequence);

                if (item is not null)
                {
                    item.IsAlert = true;
                }
            }

            RefreshSelectionState();
        }
    }

    /// <summary>Last three calls of the opposite run before its terminus.</summary>
    private List<EditorStopViewModel> BuildIncoming(string routeId, string directionId)
    {
        var result = new List<EditorStopViewModel>();
        if (_data is null)
        {
            return result;
        }

        var opposite = _data.Trips.Values
            .Where(t => t.RouteId == routeId && t.DirectionId != directionId)
            .OrderByDescending(t => _data.StopTimes(t.Id).Count)
            .FirstOrDefault();

        if (opposite is null)
        {
            return result;
        }

        var calls = _data.StopTimes(opposite.Id);
        if (calls.Count <= 1)
        {
            return result;
        }

        foreach (var call in calls.Take(calls.Count - 1).TakeLast(3))
        {
            if (_data.FindStop(call.StopId) is { } stop)
            {
                result.Add(new EditorStopViewModel(call, stop, isIncoming: true));
            }
        }

        return result;
    }

    [RelayCommand]
    private void SelectBoarding(EditorStopViewModel? stop) => SelectBoarding(stop, notify: true);

    private void SelectBoarding(EditorStopViewModel? stop, bool notify)
    {
        if (stop is null || stop.IsIncoming)
        {
            return;
        }

        foreach (var item in _forwardStops)
        {
            item.IsBoarding = item == stop;
            if (item.StopTime.Sequence >= stop.StopTime.Sequence)
            {
                item.IsAlert = false;
            }
        }

        RefreshSelectionState();

        if (notify)
        {
            SelectionSummary = $"Boarding at {stop.Name} ({stop.Time}). " +
                               "Now pick stops before it to be warned at.";
        }
    }

    [RelayCommand]
    private void ToggleAlert(EditorStopViewModel? stop)
    {
        if (stop is null)
        {
            return;
        }

        var boarding = _forwardStops.FirstOrDefault(s => s.IsBoarding);
        if (boarding is null)
        {
            HasError = true;
            StatusMessage = "Choose a boarding stop first.";
            return;
        }

        if (stop.IsIncoming)
        {
            if (_forwardStops.IndexOf(boarding) >= 3)
            {
                HasError = true;
                StatusMessage = "Incoming-run warnings are only available when boarding at one of the first three stops.";
                return;
            }
        }
        else if (stop.StopTime.Sequence >= boarding.StopTime.Sequence)
        {
            HasError = true;
            StatusMessage = "Warning stops must come before your boarding stop.";
            return;
        }

        stop.IsAlert = !stop.IsAlert;
        HasError = false;
        StatusMessage = string.Empty;
        RefreshSelectionState();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var boarding = Stops.FirstOrDefault(s => s.IsBoarding);
        var alerts = Stops.Where(s => s.IsAlert).OrderBy(s => s.StopTime.Sequence).ToList();

        if (SelectedRoute is null || SelectedDirection is null)
        {
            HasError = true;
            StatusMessage = "Select a line and a direction.";
            return;
        }

        if (boarding is null)
        {
            HasError = true;
            StatusMessage = "Select your boarding stop.";
            return;
        }

        if (alerts.Count == 0)
        {
            HasError = true;
            StatusMessage = "Select at least one stop to be warned at.";
            return;
        }

        if (EndTime <= StartTime)
        {
            HasError = true;
            StatusMessage = "The watch window must end after it starts.";
            return;
        }

        var watch = _existing ?? new WatchRule();
        watch.Name = string.IsNullOrWhiteSpace(Name)
            ? $"Line {SelectedRoute.ShortName} → {SelectedDirection.Label.TrimStart('→', ' ')}"
            : Name.Trim();
        watch.RouteId = SelectedRoute.Id;
        watch.RouteShortName = SelectedRoute.ShortName;
        watch.DirectionId = SelectedDirection.DirectionId;
        watch.Headsign = SelectedDirection.FirstTrip.Headsign;
        watch.BoardingStopId = boarding.Stop.Id;
        watch.BoardingStopName = boarding.Name;
        watch.BoardingSequence = boarding.StopTime.Sequence;
        watch.Days = PresetFor(DayPresetIndex);
        watch.WindowStartMinutes = (int)StartTime.TotalMinutes;
        watch.WindowEndMinutes = (int)EndTime.TotalMinutes;
        watch.LeadMinutes = LeadMinutes;
        watch.AlertStops = alerts
            .Select((item, index) => new WatchAlertStop
            {
                StopId = item.Stop.Id,
                StopName = item.Name,
                StopSequence = item.StopTime.Sequence,
                TriggerMode = TriggerMode.AtStop,
                MinutesBefore = LeadMinutes,
                StopsBefore = 2,
                IsIncoming = item.IsIncoming,
            })
            .ToList();

        await _repository.UpsertAsync(watch);
        StatusMessage = "Watch saved";
        HasError = false;
        await Shell.Current.GoToAsync("..");
    }

    private void RefreshSelectionState()
    {
        var boarding = _forwardStops.FirstOrDefault(s => s.IsBoarding);
        var boardingIndex = boarding is null ? -1 : _forwardStops.IndexOf(boarding);

        foreach (var item in Stops)
        {
            item.CanAlert = item.IsIncoming
                ? boardingIndex is >= 0 and < 3
                : boarding is not null && item.StopTime.Sequence < boarding.StopTime.Sequence;

            if (!item.CanAlert)
            {
                item.IsAlert = false;
            }
        }

        if (boarding is not null)
        {
            var count = Stops.Count(s => s.IsAlert);
            SelectionSummary = $"Boarding at {boarding.Name} · {count} warning stop(s)";
        }
    }

    private static WeekdayFlags PresetFor(int index) => index switch
    {
        0 => WeekdayFlags.EveryDay,
        2 => WeekdayFlags.Weekend,
        _ => WeekdayFlags.Weekdays,
    };

    private static int PresetFor(WeekdayFlags days) => days switch
    {
        WeekdayFlags.EveryDay => 0,
        WeekdayFlags.Weekend => 2,
        _ => 1,
    };
}
