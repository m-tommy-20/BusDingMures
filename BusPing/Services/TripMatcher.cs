using BusPing.Models;

namespace BusPing.Services;

public sealed record TripMatch(
    Trip Trip,
    double DistanceMeters,
    double BearingDelta,
    bool IsDirectionConfident);

/// <summary>
/// Matches a live vehicle to a GTFS trip when the feed does not provide one.
///
/// Most vehicles arrive as <c>assignment_status: unverified</c> with an internal
/// trip id that does not exist in GTFS, and no headsign or direction. Their GPS
/// fix is still real, so the route's shapes decide: the vehicle must lie on the
/// shape and its compass bearing must point the same way as the shape segment.
/// That distinguishes the two directions of the same street.
/// </summary>
public static class TripMatcher
{
    private const double MaxDistanceMeters = 200;
    private const double MaxBearingDelta = 70;
    private const double MinMovingSpeedMps = 2;

    public static TripMatch? Match(
        GtfsData data, VehiclePosition vehicle, string routeId, DateOnly serviceDate)
    {
        if (vehicle.Latitude == 0 && vehicle.Longitude == 0)
        {
            return null;
        }

        var speed = vehicle.SpeedMetersPerSecond ?? (vehicle.Speed ?? 0) / 3.6;
        var moving = speed >= MinMovingSpeedMps;

        TripMatch? best = null;
        var bestScore = double.MaxValue;

        foreach (var group in data.Trips.Values
                     .Where(t => t.RouteId == routeId && !string.IsNullOrEmpty(t.ShapeId))
                     .GroupBy(t => t.ShapeId!))
        {
            var trips = group.ToList();
            if (trips.Count == 0 ||
                !data.Shapes.TryGetValue(trips[0].ShapeId!, out var shape) ||
                ShapeMatching.Project(shape, vehicle.Latitude, vehicle.Longitude) is not { } projection)
            {
                continue;
            }

            if (projection.DistanceMeters > MaxDistanceMeters)
            {
                continue;
            }

            var delta = vehicle.Bearing is { } bearing
                ? ShapeMatching.BearingDelta(bearing, projection.SegmentBearing)
                : 0;

            if (moving && delta > MaxBearingDelta)
            {
                continue;
            }

            // When the bus is stopped the bearing is meaningless, so distance
            // alone decides and confidence is reported as low.
            var score = projection.DistanceMeters + delta * (moving ? 1.5 : 0.4);
            if (score >= bestScore)
            {
                continue;
            }

            var trip = trips.FirstOrDefault(t => data.IsServiceRunning(t.ServiceId, serviceDate))
                       ?? trips[0];

            bestScore = score;
            best = new TripMatch(trip, projection.DistanceMeters, delta, moving && delta < 35);
        }

        return best;
    }
}
