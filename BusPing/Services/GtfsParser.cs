using System.Globalization;
using BusPing.Models;

namespace BusPing.Services;

/// <summary>
/// Pure GTFS parsing (no MAUI dependencies), so it can be verified outside the
/// app. Files come from the operator's gtfs.zip.
/// </summary>
public static class GtfsParser
{
    public static GtfsData Parse(string directory)
    {
        var routes = new Dictionary<string, Route>(StringComparer.Ordinal);
        foreach (var r in Csv.ReadRecords(Path.Combine(directory, "routes.txt")))
        {
            var id = Get(r, "route_id");
            if (id.Length == 0)
            {
                continue;
            }

            routes[id] = new Route
            {
                Id = id,
                ShortName = Fallback(Get(r, "route_short_name"), id),
                LongName = Get(r, "route_long_name"),
                Color = Get(r, "route_color"),
                RouteType = ParseInt(Get(r, "route_type")),
            };
        }

        var stops = new Dictionary<string, Stop>(StringComparer.Ordinal);
        foreach (var r in Csv.ReadRecords(Path.Combine(directory, "stops.txt")))
        {
            var id = Get(r, "stop_id");
            if (id.Length == 0)
            {
                continue;
            }

            stops[id] = new Stop
            {
                Id = id,
                Code = Get(r, "stop_code"),
                Name = Fallback(Get(r, "stop_name"), id),
                Latitude = ParseDouble(Get(r, "stop_lat")),
                Longitude = ParseDouble(Get(r, "stop_lon")),
            };
        }

        var trips = new Dictionary<string, Trip>(StringComparer.Ordinal);
        foreach (var r in Csv.ReadRecords(Path.Combine(directory, "trips.txt")))
        {
            var id = Get(r, "trip_id");
            if (id.Length == 0)
            {
                continue;
            }

            trips[id] = new Trip
            {
                Id = id,
                RouteId = Get(r, "route_id"),
                ServiceId = Get(r, "service_id"),
                Headsign = Get(r, "trip_headsign"),
                DirectionId = Fallback(Get(r, "direction_id"), "0"),
                ShapeId = NullIfEmpty(Get(r, "shape_id")),
            };
        }

        var stopTimes = new Dictionary<string, List<StopTime>>(StringComparer.Ordinal);
        foreach (var r in Csv.ReadRecords(Path.Combine(directory, "stop_times.txt")))
        {
            var tripId = Get(r, "trip_id");
            var stopId = Get(r, "stop_id");
            if (tripId.Length == 0 || stopId.Length == 0)
            {
                continue;
            }

            if (!stopTimes.TryGetValue(tripId, out var list))
            {
                list = [];
                stopTimes[tripId] = list;
            }

            list.Add(new StopTime
            {
                TripId = tripId,
                StopId = stopId,
                Sequence = ParseInt(Get(r, "stop_sequence")),
                ArrivalSeconds = ParseClock(Get(r, "arrival_time")),
                DepartureSeconds = ParseClock(Get(r, "departure_time")),
            });
        }

        foreach (var list in stopTimes.Values)
        {
            list.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        }

        return new GtfsData
        {
            Routes = routes,
            Stops = stops,
            Trips = trips,
            StopTimesByTrip = stopTimes.ToDictionary(
                kvp => kvp.Key, kvp => (IReadOnlyList<StopTime>)kvp.Value, StringComparer.Ordinal),
            Calendars = ParseCalendars(directory),
            Exceptions = ParseExceptions(directory),
            Shapes = ParseShapes(directory),
        };
    }

    private static Dictionary<string, IReadOnlyList<ShapePoint>> ParseShapes(string directory)
    {
        var path = Path.Combine(directory, "shapes.txt");
        if (!File.Exists(path))
        {
            return [];
        }

        var shapes = new Dictionary<string, IReadOnlyList<ShapePoint>>(StringComparer.Ordinal);
        foreach (var group in Csv.ReadRecords(path)
                     .Select(r => new ShapePoint
                     {
                         ShapeId = Get(r, "shape_id"),
                         Sequence = ParseInt(Get(r, "shape_pt_sequence")),
                         Latitude = ParseDouble(Get(r, "shape_pt_lat")),
                         Longitude = ParseDouble(Get(r, "shape_pt_lon")),
                     })
                     .Where(p => p.ShapeId.Length > 0)
                     .GroupBy(p => p.ShapeId, StringComparer.Ordinal))
        {
            shapes[group.Key] = group.OrderBy(p => p.Sequence).ToList();
        }

        return shapes;
    }

    private static Dictionary<string, ServiceCalendar> ParseCalendars(string directory)
    {
        var calendars = new Dictionary<string, ServiceCalendar>(StringComparer.Ordinal);
        var path = Path.Combine(directory, "calendar.txt");
        if (!File.Exists(path))
        {
            return calendars;
        }

        foreach (var r in Csv.ReadRecords(path))
        {
            var id = Get(r, "service_id");
            if (id.Length == 0)
            {
                continue;
            }

            calendars[id] = new ServiceCalendar
            {
                ServiceId = id,
                Monday = ParseBool(Get(r, "monday")),
                Tuesday = ParseBool(Get(r, "tuesday")),
                Wednesday = ParseBool(Get(r, "wednesday")),
                Thursday = ParseBool(Get(r, "thursday")),
                Friday = ParseBool(Get(r, "friday")),
                Saturday = ParseBool(Get(r, "saturday")),
                Sunday = ParseBool(Get(r, "sunday")),
                StartDate = ParseDate(Get(r, "start_date")),
                EndDate = ParseDate(Get(r, "end_date")),
            };
        }

        return calendars;
    }

    private static List<ServiceException> ParseExceptions(string directory)
    {
        var exceptions = new List<ServiceException>();
        var path = Path.Combine(directory, "calendar_dates.txt");
        if (!File.Exists(path))
        {
            return exceptions;
        }

        foreach (var r in Csv.ReadRecords(path))
        {
            var id = Get(r, "service_id");
            if (id.Length == 0)
            {
                continue;
            }

            exceptions.Add(new ServiceException
            {
                ServiceId = id,
                Date = ParseDate(Get(r, "date")),
                ExceptionType = ParseInt(Get(r, "exception_type")),
            });
        }

        return exceptions;
    }

    private static string Get(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;

    private static bool ParseBool(string value) => value == "1";

    private static DateOnly ParseDate(string value) =>
        DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed)
            : DateOnly.MinValue;

    /// <summary>Parses "HH:mm:ss" including GTFS times past midnight (e.g. 25:10:00).</summary>
    private static int ParseClock(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3)
        {
            return 0;
        }

        return ParseInt(parts[0]) * 3600 + ParseInt(parts[1]) * 60 + ParseInt(parts[2]);
    }
}
