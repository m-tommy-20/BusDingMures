namespace BusPing.Models;

[Flags]
public enum WeekdayFlags
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    EveryDay = Weekdays | Weekend,
}

public enum TriggerMode
{
    /// <summary>Fire when the vehicle reaches or passes the alert stop.</summary>
    AtStop = 0,

    /// <summary>Fire when the bus is N minutes away from the boarding stop.</summary>
    MinutesBefore = 1,

    /// <summary>Fire when the bus is N stops away from the boarding stop.</summary>
    StopsBefore = 2,
}

/// <summary>
/// The main user entity: "watch line 1 towards X, board at stop A, warn me at
/// stops B and C on weekday mornings".
/// </summary>
public sealed class WatchRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>GTFS route id, e.g. "1".</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Denormalized for display, so a GTFS refresh cannot blank the UI.</summary>
    public string RouteShortName { get; set; } = string.Empty;

    public string DirectionId { get; set; } = "0";
    public string Headsign { get; set; } = string.Empty;

    public string BoardingStopId { get; set; } = string.Empty;
    public string BoardingStopName { get; set; } = string.Empty;

    /// <summary>Index of the boarding stop inside the trip's stop sequence.</summary>
    public int BoardingSequence { get; set; }

    public bool Enabled { get; set; } = true;

    public WeekdayFlags Days { get; set; } = WeekdayFlags.Weekdays;

    /// <summary>Minutes after midnight; the watch window is [Start, End).</summary>
    public int WindowStartMinutes { get; set; } = 6 * 60;

    public int WindowEndMinutes { get; set; } = 9 * 60;

    /// <summary>Default lead time for MinutesBefore triggers.</summary>
    public int LeadMinutes { get; set; } = 5;

    public int CooldownMinutes { get; set; } = 10;

    public List<WatchAlertStop> AlertStops { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public bool IsActiveOn(DateTimeOffset localNow)
    {
        if (!Enabled)
        {
            return false;
        }

        var flag = localNow.DayOfWeek switch
        {
            DayOfWeek.Monday => WeekdayFlags.Monday,
            DayOfWeek.Tuesday => WeekdayFlags.Tuesday,
            DayOfWeek.Wednesday => WeekdayFlags.Wednesday,
            DayOfWeek.Thursday => WeekdayFlags.Thursday,
            DayOfWeek.Friday => WeekdayFlags.Friday,
            DayOfWeek.Saturday => WeekdayFlags.Saturday,
            _ => WeekdayFlags.Sunday,
        };

        if (!Days.HasFlag(flag))
        {
            return false;
        }

        var minutes = localNow.Hour * 60 + localNow.Minute;
        return minutes >= WindowStartMinutes && minutes < WindowEndMinutes;
    }
}

/// <summary>One stop the user wants to be warned about, earlier in the trip.</summary>
public sealed class WatchAlertStop
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;

    /// <summary>Sequence of this stop in the trip; must be before BoardingSequence.</summary>
    public int StopSequence { get; set; }

    public TriggerMode TriggerMode { get; set; } = TriggerMode.AtStop;

    /// <summary>Used by MinutesBefore triggers.</summary>
    public int MinutesBefore { get; set; } = 5;

    /// <summary>Used by StopsBefore triggers.</summary>
    public int StopsBefore { get; set; } = 2;

    /// <summary>
    /// True when this stop belongs to the opposite run that terminates at the
    /// boarding stop (the bus that will later become the user's bus).
    /// </summary>
    public bool IsIncoming { get; set; }
}
