using System.Text.Json.Serialization;

namespace BusPing.Models;

/// <summary>
/// One live vehicle from live.transportlocal.ro/journeys.json, reduced to the
/// fields BusPing actually uses.
/// </summary>
public sealed class VehiclePosition
{
    [JsonPropertyName("vehicle_id")]
    public string VehicleId { get; init; } = string.Empty;

    /// <summary>Short route name, e.g. "2B". May differ from the GTFS route id.</summary>
    [JsonPropertyName("route")]
    public string Route { get; init; } = string.Empty;

    [JsonPropertyName("route_id")]
    public string? RouteId { get; init; }

    [JsonPropertyName("trip_id")]
    public string? TripId { get; init; }

    [JsonPropertyName("headsign")]
    public string? Headsign { get; init; }

    [JsonPropertyName("direction_id")]
    public string? DirectionId { get; init; }

    [JsonPropertyName("service_id")]
    public string? ServiceId { get; init; }

    [JsonPropertyName("shape_id")]
    public string? ShapeId { get; init; }

    [JsonPropertyName("lat")]
    public double Latitude { get; init; }

    [JsonPropertyName("lon")]
    public double Longitude { get; init; }

    /// <summary>Reported speed. The feed's unit has varied, so callers should clamp.</summary>
    [JsonPropertyName("speed")]
    public double? Speed { get; init; }

    [JsonPropertyName("gps_timestamp")]
    public DateTimeOffset? GpsTimestamp { get; init; }

    /// <summary>Compass bearing of travel (the feed's "direction" field), in degrees.</summary>
    [JsonPropertyName("direction")]
    public double? Bearing { get; init; }

    [JsonPropertyName("speed_mps")]
    public double? SpeedMetersPerSecond { get; init; }

    /// <summary>"thoreb" means real GPS; "legacy" means a generated position.</summary>
    [JsonPropertyName("position_source")]
    public string? PositionSource { get; init; }

    /// <summary>"gtfs-confirmed", "unverified" or "unavailable".</summary>
    [JsonPropertyName("assignment_status")]
    public string? AssignmentStatus { get; init; }

    [JsonPropertyName("schedule")]
    public LiveSchedule? Schedule { get; init; }

    /// <summary>True when the position is real GPS and the trip is confidently matched.</summary>
    public bool IsReliable =>
        string.Equals(PositionSource, "thoreb", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(TripId) &&
        string.Equals(AssignmentStatus, "gtfs-confirmed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The operator's own next-stop prediction for a vehicle.</summary>
public sealed class LiveSchedule
{
    [JsonPropertyName("stop_id")]
    public string? StopId { get; init; }

    [JsonPropertyName("stop_name")]
    public string? StopName { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    /// <summary>Predicted time in epoch seconds.</summary>
    [JsonPropertyName("time")]
    public long? Time { get; init; }

    [JsonPropertyName("reported_delay_seconds")]
    public double? ReportedDelaySeconds { get; init; }

    [JsonPropertyName("reference_quality")]
    public string? ReferenceQuality { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}
