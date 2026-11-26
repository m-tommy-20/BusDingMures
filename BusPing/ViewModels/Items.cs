using BusPing.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BusPing.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;
}

/// <summary>Display wrapper for a route in the routes list.</summary>
public sealed class RouteItemViewModel
{
    public RouteItemViewModel(Route route, int tripCount, int stopCount)
    {
        Route = route;
        TripCount = tripCount;
        StopCount = stopCount;
    }

    public Route Route { get; }
    public int TripCount { get; }
    public int StopCount { get; }

    public string Title => Route.ShortName;
    public string Subtitle => string.IsNullOrWhiteSpace(Route.LongName)
        ? $"{TripCount} trips"
        : Route.LongName;

    public string Detail => $"{TripCount} scheduled trips · {StopCount} stops";
}

/// <summary>Display wrapper for one scheduled call inside a direction's timeline.</summary>
public sealed class TimelineStopViewModel
{
    public TimelineStopViewModel(StopTime stopTime, Stop stop)
    {
        StopTime = stopTime;
        Stop = stop;
    }

    public StopTime StopTime { get; }
    public Stop Stop { get; }

    public string Name => Stop.Name;
    public string Time => $"{StopTime.Arrival.Hours:00}:{StopTime.Arrival.Minutes:00}";
    public string SequenceText => StopTime.Sequence.ToString();
}

/// <summary>Display wrapper for a watch rule in lists.</summary>
public sealed class WatchCardViewModel
{
    public WatchCardViewModel(WatchRule watch)
    {
        Watch = watch;
    }

    public WatchRule Watch { get; }

    public string Name => string.IsNullOrWhiteSpace(Watch.Name)
        ? $"Line {Watch.RouteShortName}"
        : Watch.Name;

    public string RouteLine => string.IsNullOrWhiteSpace(Watch.Headsign)
        ? $"Line {Watch.RouteShortName}"
        : $"Line {Watch.RouteShortName} → {Watch.Headsign}";

    public string BoardingLine => $"Board at {Watch.BoardingStopName}";

    public string AlertLine => Watch.AlertStops.Count == 0
        ? "No warning stops selected"
        : "Warn at " + string.Join(", ", Watch.AlertStops.OrderBy(a => a.StopSequence).Select(a => a.StopName));

    public string WindowText =>
        $"{Watch.WindowStartMinutes / 60:00}:{Watch.WindowStartMinutes % 60:00}–" +
        $"{Watch.WindowEndMinutes / 60:00}:{Watch.WindowEndMinutes % 60:00} · {DescribeDays(Watch.Days)}";

    public bool Enabled => Watch.Enabled;

    private static string DescribeDays(WeekdayFlags days) => days switch
    {
        WeekdayFlags.EveryDay => "every day",
        WeekdayFlags.Weekdays => "weekdays",
        WeekdayFlags.Weekend => "weekends",
        _ => days.ToString(),
    };
}

/// <summary>Display wrapper for a fired alert in history.</summary>
public sealed class AlertItemViewModel
{
    public AlertItemViewModel(AlertEvent alertEvent)
    {
        Event = alertEvent;
    }

    public AlertEvent Event { get; }

    public string Title => $"{Event.RouteShortName} · {Event.TriggerStopName}";
    public string Subtitle => Event.Message;
    public string TimeText => Event.FiredAt.ToLocalTime().ToString("MMM d, HH:mm");
    public string EtaText => $"{Event.EtaMinutes:0} min to {Event.BoardingStopName}";
}
