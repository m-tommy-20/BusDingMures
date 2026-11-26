using BusPing.Models;

namespace BusPing.Services;

/// <summary>Small geo helpers used by the ETA calculation.</summary>
public static class Geo
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(a));
    }

    public static double DistanceMeters(Stop a, Stop b) =>
        DistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);

    public static double DistanceMeters(Stop stop, VehiclePosition vehicle) =>
        DistanceMeters(stop.Latitude, stop.Longitude, vehicle.Latitude, vehicle.Longitude);

    /// <summary>Bearing in degrees (0 = north), used to sanity-check trip matching.</summary>
    public static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRadians(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRadians(lat2));
        var x = Math.Cos(ToRadians(lat1)) * Math.Sin(ToRadians(lat2)) -
                Math.Sin(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Cos(dLon);
        var degrees = Math.Atan2(y, x) * 180 / Math.PI;
        return (degrees + 360) % 360;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
