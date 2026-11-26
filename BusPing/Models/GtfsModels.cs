namespace BusPing.Models;

/// <summary>A GTFS route ("line") from routes.txt.</summary>
public sealed class Route
{
    public required string Id { get; init; }
    public required string ShortName { get; init; }
    public string LongName { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public int RouteType { get; init; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(LongName) ? ShortName : $"{ShortName} · {LongName}";
}

/// <summary>A single scheduled run of a route from trips.txt.</summary>
public sealed class Trip
{
    public required string Id { get; init; }
    public required string RouteId { get; init; }
    public required string ServiceId { get; init; }
    public string Headsign { get; init; } = string.Empty;
    public string DirectionId { get; init; } = "0";
    public string? ShapeId { get; init; }
}

/// <summary>A physical stop from stops.txt.</summary>
public sealed class Stop
{
    public required string Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public required string Name { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

/// <summary>
/// One scheduled call of a trip at a stop. Times are seconds after midnight of
/// the service day and may exceed 24:00:00 for trips running past midnight.
/// </summary>
public sealed class StopTime
{
    public required string TripId { get; init; }
    public required string StopId { get; init; }
    public int Sequence { get; init; }
    public int ArrivalSeconds { get; init; }
    public int DepartureSeconds { get; init; }

    public TimeSpan Arrival => TimeSpan.FromSeconds(ArrivalSeconds);
}

/// <summary>Service availability from calendar.txt.</summary>
public sealed class ServiceCalendar
{
    public required string ServiceId { get; init; }
    public bool Monday { get; init; }
    public bool Tuesday { get; init; }
    public bool Wednesday { get; init; }
    public bool Thursday { get; init; }
    public bool Friday { get; init; }
    public bool Saturday { get; init; }
    public bool Sunday { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    public bool RunsOn(DateOnly date)
    {
        if (date < StartDate || date > EndDate)
        {
            return false;
        }

        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => Monday,
            DayOfWeek.Tuesday => Tuesday,
            DayOfWeek.Wednesday => Wednesday,
            DayOfWeek.Thursday => Thursday,
            DayOfWeek.Friday => Friday,
            DayOfWeek.Saturday => Saturday,
            _ => Sunday,
        };
    }
}

/// <summary>A calendar exception from calendar_dates.txt.</summary>
public sealed class ServiceException
{
    public required string ServiceId { get; init; }
    public DateOnly Date { get; init; }
    /// <summary>1 = service added on this date, 2 = service removed.</summary>
    public int ExceptionType { get; init; }
}

/// <summary>A point on a route geometry from shapes.txt.</summary>
public sealed class ShapePoint
{
    public required string ShapeId { get; init; }
    public int Sequence { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
