namespace BusPing.Models;

/// <summary>A fired alert, kept for history and statistics.</summary>
public sealed class AlertEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WatchRuleId { get; set; }
    public string WatchName { get; set; } = string.Empty;
    public string RouteShortName { get; set; } = string.Empty;

    public string TripId { get; set; } = string.Empty;

    /// <summary>The service day the trip belongs to (local date).</summary>
    public DateOnly ServiceDate { get; set; }

    public string TriggerStopId { get; set; } = string.Empty;
    public string TriggerStopName { get; set; } = string.Empty;
    public string BoardingStopName { get; set; } = string.Empty;

    public DateTimeOffset FiredAt { get; set; } = DateTimeOffset.Now;
    public string VehicleId { get; set; } = string.Empty;

    /// <summary>Estimated minutes to the boarding stop when the alert fired.</summary>
    public double EtaMinutes { get; set; }

    public string Message { get; set; } = string.Empty;
}
