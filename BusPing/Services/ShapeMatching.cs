using BusPing.Models;

namespace BusPing.Services;

public readonly record struct ShapeProjection(
    double DistanceMeters,
    double AlongMeters,
    double TotalMeters,
    double SegmentBearing);

/// <summary>
/// Geometry helpers for matching a GPS fix to a GTFS shape. City-scale
/// equirectangular projection is accurate enough and much cheaper than a full
/// geodesic computation.
/// </summary>
public static class ShapeMatching
{
    private const double MetersPerDegreeLat = 110_540;

    public static ShapeProjection? Project(
        IReadOnlyList<ShapePoint> shape, double latitude, double longitude)
    {
        if (shape.Count < 2)
        {
            return null;
        }

        var lat0 = shape[0].Latitude;
        var cosLat = Math.Cos(lat0 * Math.PI / 180);
        var px = (longitude - shape[0].Longitude) * 111_320 * cosLat;
        var py = (latitude - lat0) * MetersPerDegreeLat;

        var bestDistance = double.MaxValue;
        var bestAlong = 0.0;
        var bestBearing = 0.0;
        var along = 0.0;

        for (var i = 0; i < shape.Count - 1; i++)
        {
            var a = shape[i];
            var b = shape[i + 1];

            var ax = (a.Longitude - shape[0].Longitude) * 111_320 * cosLat;
            var ay = (a.Latitude - lat0) * MetersPerDegreeLat;
            var bx = (b.Longitude - shape[0].Longitude) * 111_320 * cosLat;
            var by = (b.Latitude - lat0) * MetersPerDegreeLat;

            var dx = bx - ax;
            var dy = by - ay;
            var lengthSquared = dx * dx + dy * dy;
            var segmentLength = Math.Sqrt(lengthSquared);

            var t = lengthSquared > 0
                ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1)
                : 0;

            var cx = ax + t * dx;
            var cy = ay + t * dy;
            var distance = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestAlong = along + t * segmentLength;
                bestBearing = Geo.Bearing(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
            }

            along += segmentLength;
        }

        return new ShapeProjection(bestDistance, bestAlong, along, bestBearing);
    }

    /// <summary>Smallest absolute difference between two bearings, 0–180°.</summary>
    public static double BearingDelta(double a, double b)
    {
        var delta = Math.Abs(a - b) % 360;
        return delta > 180 ? 360 - delta : delta;
    }
}
