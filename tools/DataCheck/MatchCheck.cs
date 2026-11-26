using BusPing.Models;
using BusPing.Services;

namespace DataCheck;

/// <summary>
/// Deterministic check of shape-based trip matching. Two overlapping shapes run
/// in opposite directions along the same street; only the compass bearing can
/// tell them apart, exactly like the two directions of a real bus line.
/// </summary>
public static class MatchCheck
{
    public static int Run()
    {
        var failures = 0;

        void Check(string name, bool condition, string detail = "")
        {
            Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? ": " + detail : "")}");
            if (!condition)
            {
                failures++;
            }
        }

        var eastbound = new List<ShapePoint>
        {
            new() { ShapeId = "E", Sequence = 1, Latitude = 46.0, Longitude = 24.000 },
            new() { ShapeId = "E", Sequence = 2, Latitude = 46.0, Longitude = 24.010 },
        };
        var westbound = new List<ShapePoint>
        {
            new() { ShapeId = "W", Sequence = 1, Latitude = 46.0, Longitude = 24.010 },
            new() { ShapeId = "W", Sequence = 2, Latitude = 46.0, Longitude = 24.000 },
        };

        var data = new GtfsData
        {
            Routes = new Dictionary<string, Route>
            {
                ["1"] = new Route { Id = "1", ShortName = "1" },
            },
            Stops = new Dictionary<string, Stop>
            {
                ["A"] = new Stop { Id = "A", Name = "A", Latitude = 46.0, Longitude = 24.001 },
                ["B"] = new Stop { Id = "B", Name = "B", Latitude = 46.0, Longitude = 24.009 },
            },
            Trips = new Dictionary<string, Trip>
            {
                ["East"] = new Trip { Id = "East", RouteId = "1", ServiceId = "3", DirectionId = "1", ShapeId = "E" },
                ["West"] = new Trip { Id = "West", RouteId = "1", ServiceId = "3", DirectionId = "0", ShapeId = "W" },
            },
            StopTimesByTrip = new Dictionary<string, IReadOnlyList<StopTime>>(),
            Calendars = new Dictionary<string, ServiceCalendar>
            {
                ["3"] = new ServiceCalendar
                {
                    ServiceId = "3",
                    Monday = true, Tuesday = true, Wednesday = true,
                    Thursday = true, Friday = true, Saturday = true, Sunday = true,
                    StartDate = new DateOnly(2020, 1, 1),
                    EndDate = new DateOnly(2035, 1, 1),
                },
            },
            Exceptions = [],
            Shapes = new Dictionary<string, IReadOnlyList<ShapePoint>>
            {
                ["E"] = eastbound,
                ["W"] = westbound,
            },
        };

        var today = DateOnly.FromDateTime(DateTime.Now);

        var east = TripMatcher.Match(data, Vehicle("EAST", 46.0, 24.005, bearing: 90, speed: 8), "1", today);
        Check("Eastbound bearing matches the eastbound shape", east?.Trip.DirectionId == "1", Describe(east));
        Check("Eastbound match is direction-confident", east?.IsDirectionConfident == true);

        var west = TripMatcher.Match(data, Vehicle("WEST", 46.0, 24.005, bearing: 270, speed: 8), "1", today);
        Check("Westbound bearing matches the westbound shape", west?.Trip.DirectionId == "0", Describe(west));

        var stopped = TripMatcher.Match(data, Vehicle("STOPPED", 46.0, 24.005, bearing: 270, speed: 0), "1", today);
        Check("Stopped bus still matches a shape", stopped is not null, Describe(stopped));

        var offRoute = TripMatcher.Match(data, Vehicle("FAR", 46.05, 24.2, bearing: 90, speed: 8), "1", today);
        Check("Vehicle far from the route is rejected", offRoute is null, Describe(offRoute));

        return failures;
    }

    private static VehiclePosition Vehicle(
        string id, double lat, double lon, double bearing, double speed) => new()
    {
        VehicleId = id,
        Route = "1",
        RouteId = "1",
        Latitude = lat,
        Longitude = lon,
        Bearing = bearing,
        SpeedMetersPerSecond = speed,
        PositionSource = "thoreb",
        GpsTimestamp = DateTimeOffset.Now,
    };

    private static string Describe(TripMatch? match) =>
        match is null
            ? "no match"
            : $"dir {match.Trip.DirectionId}, {match.DistanceMeters:0.0} m, " +
              $"bearing Δ {match.BearingDelta:0}°, confident={match.IsDirectionConfident}";
}
