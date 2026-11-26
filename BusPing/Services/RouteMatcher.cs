namespace BusPing.Services;

/// <summary>
/// The live feed and the GTFS feed do not always use the same route labels:
/// the live map publishes "202" where GTFS calls the line "2B". Everything is
/// compared in one canonical, lower-case form.
/// </summary>
public static class RouteMatcher
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["202"] = "2b",
        ["102"] = "10b",
        ["192"] = "19l",
        ["232"] = "23b",
        ["302"] = "30b",
    };

    public static string Canonical(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return Aliases.TryGetValue(normalized, out var alias) ? alias : normalized;
    }

    public static bool Matches(string routeShortName, string? routeId, string liveRoute, string? liveRouteId)
    {
        var watchName = Canonical(routeShortName);
        var watchId = Canonical(routeId);

        return Exists(watchName, liveRoute) ||
               Exists(watchName, liveRouteId) ||
               Exists(watchId, liveRoute) ||
               Exists(watchId, liveRouteId);
    }

    private static bool Exists(string expected, string? actual) =>
        expected.Length > 0 &&
        actual is not null &&
        Canonical(actual) == expected;
}
