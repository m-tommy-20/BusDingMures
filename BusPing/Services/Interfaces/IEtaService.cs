using BusPing.Models;

namespace BusPing.Services.Interfaces;

public enum EtaConfidence
{
    /// <summary>Distance-based estimate from live GPS only.</summary>
    Low = 0,

    /// <summary>GPS plus the operator's reported delay.</summary>
    Medium = 1,

    /// <summary>Vehicle is at/near the stop, or the operator predicts this exact stop.</summary>
    High = 2,
}

public sealed record EtaResult(
    StopTime Target,
    double Minutes,
    int StopsAway,
    double DistanceMeters,
    bool IsAtStop,
    bool HasPassed,
    EtaConfidence Confidence)
{
    public int RoundedMinutes => (int)Math.Round(Minutes, MidpointRounding.AwayFromZero);
}

public interface IEtaService
{
    /// <summary>
    /// Estimates the time until <paramref name="trip"/> reaches
    /// <paramref name="target"/>. Returns null when the trip or target is
    /// unknown to the feed.
    /// </summary>
    EtaResult? Compute(
        GtfsData data,
        VehiclePosition vehicle,
        Trip trip,
        StopTime target,
        DateTimeOffset now,
        bool useSchedule = true);
}
