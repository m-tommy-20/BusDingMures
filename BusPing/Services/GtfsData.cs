using BusPing.Models;

namespace BusPing.Services;

/// <summary>All static schedule data, parsed once per app run.</summary>
public sealed class GtfsData
{
    public required IReadOnlyDictionary<string, Route> Routes { get; init; }
    public required IReadOnlyDictionary<string, Stop> Stops { get; init; }
    public required IReadOnlyDictionary<string, Trip> Trips { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<StopTime>> StopTimesByTrip { get; init; }
    public required IReadOnlyDictionary<string, ServiceCalendar> Calendars { get; init; }
    public required IReadOnlyList<ServiceException> Exceptions { get; init; }

    /// <summary>Route geometry by shape id, used to match unverified live vehicles.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<ShapePoint>> Shapes { get; init; } =
        new Dictionary<string, IReadOnlyList<ShapePoint>>();

    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.Now;

    public Stop? FindStop(string stopId) => Stops.GetValueOrDefault(stopId);

    public Route? FindRoute(string routeId) => Routes.GetValueOrDefault(routeId);

    public Trip? FindTrip(string tripId) => Trips.GetValueOrDefault(tripId);

    public IReadOnlyList<StopTime> StopTimes(string tripId) =>
        StopTimesByTrip.GetValueOrDefault(tripId, []);

    /// <summary>Trips of a route in a direction, earliest first.</summary>
    public IEnumerable<Trip> TripsFor(string routeId, string directionId) =>
        Trips.Values
            .Where(t => t.RouteId == routeId && t.DirectionId == directionId)
            .OrderBy(t => t.Id, StringComparer.Ordinal);

    /// <summary>True when the service id runs on the given local date.</summary>
    public bool IsServiceRunning(string serviceId, DateOnly date)
    {
        var exception = Exceptions.FirstOrDefault(e => e.ServiceId == serviceId && e.Date == date);
        if (exception is not null)
        {
            return exception.ExceptionType == 1;
        }

        return Calendars.TryGetValue(serviceId, out var calendar) && calendar.RunsOn(date);
    }
}
