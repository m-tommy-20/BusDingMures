using BusPing.Models;
using BusPing.Services.Interfaces;

namespace BusPing.Services;

/// <summary>
/// The app's own domain calculation: how long until this vehicle reaches a
/// given stop of its trip.
///
/// The live feed only predicts the *next* stop, so anything further ahead has to
/// be estimated here: remaining distance along the stop sequence divided by an
/// effective speed, plus dwell time at intermediate stops, blended with the
/// operator's reported delay when it looks trustworthy.
/// </summary>
public sealed class EtaService : IEtaService
{
    private const double DwellSecondsPerStop = 20;
    private const double AtStopRadiusMeters = 120;
    private const double MinSpeedKmh = 12;
    private const double MaxSpeedKmh = 40;
    private const double AssumedSpeedKmh = 22;

    public EtaResult? Compute(
        GtfsData data,
        VehiclePosition vehicle,
        Trip trip,
        StopTime target,
        DateTimeOffset now,
        bool useSchedule = true)
    {
        var stopTimes = data.StopTimes(trip.Id);
        if (stopTimes.Count == 0)
        {
            return null;
        }

        var targetIndex = IndexOfSequence(stopTimes, target.Sequence);
        if (targetIndex < 0)
        {
            return null;
        }

        var nextIndex = FindNextStopIndex(data, vehicle, trip, stopTimes);
        if (nextIndex < 0)
        {
            return null;
        }

        var targetStop = data.FindStop(target.StopId);
        if (targetStop is null)
        {
            return null;
        }

        // The vehicle already called at (or passed) the target stop.
        if (nextIndex > targetIndex)
        {
            return new EtaResult(target, 0, 0, 0, false, true, EtaConfidence.Low);
        }

        var nextStop = data.FindStop(stopTimes[nextIndex].StopId);
        var distance = nextStop is null ? 0 : Geo.DistanceMeters(nextStop, vehicle);
        for (var i = nextIndex; i < targetIndex; i++)
        {
            var from = data.FindStop(stopTimes[i].StopId);
            var to = data.FindStop(stopTimes[i + 1].StopId);
            if (from is not null && to is not null)
            {
                distance += Geo.DistanceMeters(from, to);
            }
        }

        var speedKmh = EffectiveSpeedKmh(vehicle.Speed);
        var travelSeconds = distance / (speedKmh / 3.6);
        var intermediateStops = Math.Max(0, targetIndex - nextIndex - 1);
        var dwellSeconds = intermediateStops * DwellSecondsPerStop;
        var distanceMinutes = (travelSeconds + dwellSeconds) / 60;

        var scheduleMinutes = useSchedule ? ScheduleMinutes(target, now) : null;
        var (minutes, confidence) = Blend(distanceMinutes, scheduleMinutes, vehicle);

        // "At the stop" must mean physically there. The feed's next-stop
        // prediction is not enough: a bus 10 minutes away can already have this
        // stop as its next call, which used to produce premature alerts.
        var isAtStop = distance <= AtStopRadiusMeters;

        if (isAtStop)
        {
            confidence = EtaConfidence.High;
        }

        return new EtaResult(
            target,
            Math.Max(0, minutes),
            targetIndex - nextIndex,
            distance,
            isAtStop,
            false,
            confidence);
    }

    private static int IndexOfSequence(IReadOnlyList<StopTime> stopTimes, int sequence)
    {
        for (var i = 0; i < stopTimes.Count; i++)
        {
            if (stopTimes[i].Sequence == sequence)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Picks the stop the vehicle is heading to: the operator's next-stop
    /// prediction when present, otherwise the first stop ahead of the vehicle's
    /// projection on the route shape, and only then the nearest stop.
    /// </summary>
    private static int FindNextStopIndex(
        GtfsData data, VehiclePosition vehicle, Trip trip, IReadOnlyList<StopTime> stopTimes)
    {
        var predictedStopId = vehicle.Schedule?.StopId;
        if (!string.IsNullOrEmpty(predictedStopId))
        {
            for (var i = 0; i < stopTimes.Count; i++)
            {
                if (stopTimes[i].StopId == predictedStopId)
                {
                    return i;
                }
            }
        }

        if (trip.ShapeId is not null &&
            data.Shapes.TryGetValue(trip.ShapeId, out var shape) &&
            ShapeMatching.Project(shape, vehicle.Latitude, vehicle.Longitude) is { } vehicleProjection)
        {
            for (var i = 0; i < stopTimes.Count; i++)
            {
                var stop = data.FindStop(stopTimes[i].StopId);
                if (stop is null ||
                    ShapeMatching.Project(shape, stop.Latitude, stop.Longitude) is not { } stopProjection)
                {
                    continue;
                }

                // A small tolerance absorbs GPS jitter around the stop itself.
                if (stopProjection.AlongMeters >= vehicleProjection.AlongMeters - 25)
                {
                    return i;
                }
            }

            return stopTimes.Count - 1;
        }

        var bestIndex = 0;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < stopTimes.Count; i++)
        {
            var stop = data.FindStop(stopTimes[i].StopId);
            if (stop is null)
            {
                continue;
            }

            var distance = Geo.DistanceMeters(stop, vehicle);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// Clamps the reported speed to a plausible city-bus range. The feed's unit
    /// has been inconsistent, so a single plausible value is safer than trusting
    /// it blindly.
    /// </summary>
    private static double EffectiveSpeedKmh(double? reported)
    {
        if (reported is not > 0)
        {
            return AssumedSpeedKmh;
        }

        var value = reported.Value;
        var kmh = value <= 60 ? value : value * 3.6;
        return Math.Clamp(kmh, MinSpeedKmh, MaxSpeedKmh);
    }

    /// <summary>Minutes until the scheduled call on the current service day.</summary>
    private static double? ScheduleMinutes(StopTime target, DateTimeOffset now)
    {
        var localNow = now.ToLocalTime();
        var serviceDate = DateOnly.FromDateTime(localNow.DateTime);
        var midnight = new DateTimeOffset(
            serviceDate.ToDateTime(TimeOnly.MinValue), localNow.Offset);

        var scheduled = midnight.AddSeconds(target.ArrivalSeconds);
        var minutes = (scheduled - now).TotalMinutes;
        return minutes > -180 ? minutes : null;
    }

    /// <summary>
    /// A distance estimate and a timetable estimate rarely agree. Weight them by
    /// how much the feed can be trusted, and fall back to distance alone when the
    /// schedule estimate is missing or absurd.
    /// </summary>
    private static (double Minutes, EtaConfidence Confidence) Blend(
        double distanceMinutes, double? scheduleMinutes, VehiclePosition vehicle)
    {
        var delayKnown =
            vehicle.Schedule?.ReportedDelaySeconds is { } delaySeconds &&
            Math.Abs(delaySeconds) < 30 * 60;

        if (scheduleMinutes is not { } scheduled || scheduled < -5 || scheduled > 180)
        {
            return (distanceMinutes, EtaConfidence.Low);
        }

        if (!delayKnown)
        {
            return (distanceMinutes * 0.7 + scheduled * 0.3, EtaConfidence.Low);
        }

        var delay = vehicle.Schedule!.ReportedDelaySeconds!.Value / 60.0;
        var corrected = scheduled + delay;
        return (distanceMinutes * 0.55 + corrected * 0.45, EtaConfidence.Medium);
    }
}
