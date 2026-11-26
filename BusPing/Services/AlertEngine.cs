using BusPing.Models;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusPing.Services;

/// <summary>
/// Evaluates the user's watches against the live feed and fires notifications.
///
/// The rule for a warning stop is deliberately conservative: the bus must be
/// matched to a GTFS trip, that trip must run today, the stop must be earlier in
/// the trip than the boarding stop, and the same trip instance must not have
/// already produced the same alert.
/// </summary>
public sealed class AlertEngine : IAlertEngine
{
    private static readonly TimeSpan MaxGpsAge = TimeSpan.FromMinutes(10);

    private readonly IGtfsService _gtfs;
    private readonly ILiveVehicleService _live;
    private readonly IWatchRepository _watches;
    private readonly IAlertHistoryRepository _history;
    private readonly IEtaService _eta;
    private readonly INotificationService _notifications;
    private readonly ILogger<AlertEngine> _logger;

    public AlertEngine(
        IGtfsService gtfs,
        ILiveVehicleService live,
        IWatchRepository watches,
        IAlertHistoryRepository history,
        IEtaService eta,
        INotificationService notifications,
        ILogger<AlertEngine> logger)
    {
        _gtfs = gtfs;
        _live = live;
        _watches = watches;
        _history = history;
        _eta = eta;
        _notifications = notifications;
        _logger = logger;
    }

    public DateTimeOffset? LastRun { get; private set; }

    public string? LastError { get; private set; }

    public event EventHandler<AlertEvent>? AlertFired;

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.Now;
        try
        {
            var watches = await _watches.GetAllAsync(cancellationToken);
            var active = watches.Where(w => w.IsActiveOn(now)).ToList();
            if (active.Count == 0)
            {
                LastRun = now;
                return;
            }

            var data = await _gtfs.LoadAsync(cancellationToken: cancellationToken);
            var vehicles = await _live.GetVehiclesAsync(cancellationToken);
            var history = await _history.GetAllAsync(cancellationToken);
            var firedCount = 0;

            foreach (var watch in active)
            {
                foreach (var vehicle in vehicles)
                {
                    if (!RouteMatcher.Matches(
                            watch.RouteShortName, watch.RouteId, vehicle.Route, vehicle.RouteId))
                    {
                        continue;
                    }

                    firedCount += await EvaluateVehicleAsync(
                        data, watch, vehicle, history, now, cancellationToken);
                }
            }

            if (firedCount > 0)
            {
                _logger.LogInformation("Fired {Count} alert(s)", firedCount);
            }

            LastError = null;
            LastRun = now;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "Alert evaluation failed");
        }
    }

    private async Task<int> EvaluateVehicleAsync(
        GtfsData data,
        WatchRule watch,
        VehiclePosition vehicle,
        IReadOnlyList<AlertEvent> history,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.DateTime);
        var (trip, isExactTrip) = ResolveTrip(data, watch.RouteId, vehicle, today);
        if (trip is null)
        {
            return 0;
        }

        if (!string.IsNullOrEmpty(watch.DirectionId) &&
            !string.IsNullOrEmpty(trip.DirectionId) &&
            trip.DirectionId != watch.DirectionId)
        {
            return await EvaluateIncomingAsync(
                data, watch, vehicle, trip, history, now, isExactTrip, cancellationToken);
        }

        if (!data.IsServiceRunning(trip.ServiceId, today))
        {
            return 0;
        }

        if (IsPositionStale(vehicle, now))
        {
            return 0;
        }

        var stopTimes = data.StopTimes(trip.Id);
        var boarding = FindCall(stopTimes, watch.BoardingStopId, watch.BoardingSequence);
        if (boarding is null)
        {
            return 0;
        }

        var boardingEta = _eta.Compute(data, vehicle, trip, boarding, now, isExactTrip);
        if (boardingEta is null || boardingEta.HasPassed)
        {
            return 0;
        }

        var fired = 0;
        foreach (var alertStop in watch.AlertStops.OrderBy(a => a.StopSequence))
        {
            var call = FindCall(stopTimes, alertStop.StopId, alertStop.StopSequence, boarding.Sequence);
            if (call is null || call.Sequence >= boarding.Sequence)
            {
                continue;
            }

            if (WasAlreadyFired(history, watch, trip.Id, today, alertStop.StopId, vehicle.VehicleId))
            {
                continue;
            }

            // Cooldown is per stop, not per watch: warning at three stops must
            // produce three notifications, not one.
            if (InCooldown(history, watch, alertStop.StopId, vehicle.VehicleId, now))
            {
                continue;
            }

            if (!ShouldFire(data, vehicle, trip, call, boardingEta, alertStop, now, isExactTrip))
            {
                continue;
            }

            var alertEta = _eta.Compute(data, vehicle, trip, call, now, isExactTrip);
            var alertEvent = new AlertEvent
            {
                WatchRuleId = watch.Id,
                WatchName = watch.Name,
                RouteShortName = watch.RouteShortName,
                TripId = trip.Id,
                ServiceDate = today,
                TriggerStopId = alertStop.StopId,
                TriggerStopName = alertStop.StopName,
                BoardingStopName = watch.BoardingStopName,
                FiredAt = now,
                VehicleId = vehicle.VehicleId,
                EtaMinutes = boardingEta.Minutes,
                Message = BuildMessage(vehicle, watch, trip, alertStop, boardingEta, alertEta),
            };

            await _history.AddAsync(alertEvent, cancellationToken);
            await _notifications.NotifyAlertAsync(watch, alertEvent, cancellationToken);
            AlertFired?.Invoke(this, alertEvent);

            history = [alertEvent, .. history];
            fired++;
            break;
        }

        return fired;
    }

    /// <summary>
    /// Alerts on the opposite run that terminates at the watch's first stop.
    /// These fire only when the vehicle is actually heading to that terminus,
    /// so a bus leaving in the other direction cannot trigger them.
    /// </summary>
    private async Task<int> EvaluateIncomingAsync(
        GtfsData data,
        WatchRule watch,
        VehiclePosition vehicle,
        Trip trip,
        IReadOnlyList<AlertEvent> history,
        DateTimeOffset now,
        bool isExactTrip,
        CancellationToken cancellationToken)
    {
        var incomingAlerts = watch.AlertStops.Where(a => a.IsIncoming).ToList();
        if (incomingAlerts.Count == 0 || string.IsNullOrEmpty(watch.DirectionId))
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        var outbound = data.TripsFor(watch.RouteId, watch.DirectionId)
                           .FirstOrDefault(t => data.IsServiceRunning(t.ServiceId, today))
                       ?? data.TripsFor(watch.RouteId, watch.DirectionId).FirstOrDefault();

        if (outbound is null)
        {
            return 0;
        }

        var outboundCalls = data.StopTimes(outbound.Id);
        var calls = data.StopTimes(trip.Id);
        if (outboundCalls.Count == 0 || calls.Count == 0)
        {
            return 0;
        }

        var terminusStopId = outboundCalls[0].StopId;
        var terminusCall = calls[^1];
        if (terminusCall.StopId != terminusStopId)
        {
            return 0;
        }

        var terminusEta = _eta.Compute(data, vehicle, trip, terminusCall, now, isExactTrip);
        if (terminusEta is null || terminusEta.HasPassed)
        {
            return 0;
        }

        var terminusName = data.FindStop(terminusStopId)?.Name ?? watch.BoardingStopName;

        foreach (var alertStop in incomingAlerts.OrderBy(a => a.StopSequence))
        {
            var call = FindCall(calls, alertStop.StopId, alertStop.StopSequence, terminusCall.Sequence);
            if (call is null || call.Sequence >= terminusCall.Sequence)
            {
                continue;
            }

            if (WasAlreadyFired(history, watch, trip.Id, today, alertStop.StopId, vehicle.VehicleId) ||
                InCooldown(history, watch, alertStop.StopId, vehicle.VehicleId, now) ||
                !ShouldFire(data, vehicle, trip, call, terminusEta, alertStop, now, isExactTrip))
            {
                continue;
            }

            var alertEta = _eta.Compute(data, vehicle, trip, call, now, isExactTrip);
            var alertEvent = new AlertEvent
            {
                WatchRuleId = watch.Id,
                WatchName = watch.Name,
                RouteShortName = watch.RouteShortName,
                TripId = trip.Id,
                ServiceDate = today,
                TriggerStopId = alertStop.StopId,
                TriggerStopName = alertStop.StopName,
                BoardingStopName = watch.BoardingStopName,
                FiredAt = now,
                VehicleId = vehicle.VehicleId,
                EtaMinutes = terminusEta.Minutes,
                Message = BuildIncomingMessage(vehicle, watch, trip, alertStop, terminusName, terminusEta, alertEta),
            };

            await _history.AddAsync(alertEvent, cancellationToken);
            await _notifications.NotifyAlertAsync(watch, alertEvent, cancellationToken);
            AlertFired?.Invoke(this, alertEvent);
            return 1;
        }

        return 0;
    }

    private static string BuildIncomingMessage(
        VehiclePosition vehicle,
        WatchRule watch,
        Trip trip,
        WatchAlertStop alertStop,
        string terminusName,
        EtaResult terminusEta,
        EtaResult? alertEta)
    {
        var bus = string.IsNullOrEmpty(vehicle.VehicleId) ? "The bus" : $"Bus {vehicle.VehicleId}";
        var position = alertEta?.IsAtStop == true
            ? $"is at {alertStop.StopName}"
            : $"just passed {alertStop.StopName}";
        var towards = string.IsNullOrWhiteSpace(trip.Headsign)
            ? string.Empty
            : $" towards {trip.Headsign}";
        var continues = string.Equals(terminusName, watch.BoardingStopName, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $" Your bus leaves from {watch.BoardingStopName} after it arrives.";

        return $"{bus} on line {watch.RouteShortName}{towards} {position}. " +
               $"{terminusEta.RoundedMinutes} min to {terminusName}.{continues}";
    }

    private bool ShouldFire(
        GtfsData data,
        VehiclePosition vehicle,
        Trip trip,
        StopTime alertCall,
        EtaResult boardingEta,
        WatchAlertStop alertStop,
        DateTimeOffset now,
        bool isExactTrip)
    {
        switch (alertStop.TriggerMode)
        {
            case TriggerMode.AtStop:
                var alertEta = _eta.Compute(data, vehicle, trip, alertCall, now, isExactTrip);
                if (alertEta is null)
                {
                    return false;
                }

                if (alertEta.IsAtStop)
                {
                    return true;
                }

                if (!alertEta.HasPassed)
                {
                    return false;
                }

                // The stop is behind the bus. Only alert when it was just
                // missed (polling gap), never minutes later: an alert about a
                // bus that passed long ago only causes a pointless rush.
                var stop = data.FindStop(alertStop.StopId);
                return stop is not null && Geo.DistanceMeters(stop, vehicle) <= 250;

            case TriggerMode.MinutesBefore:
                var minutes = alertStop.MinutesBefore > 0 ? alertStop.MinutesBefore : 5;
                return boardingEta.Minutes <= minutes;

            case TriggerMode.StopsBefore:
                var stops = alertStop.StopsBefore > 0 ? alertStop.StopsBefore : 2;
                return boardingEta.StopsAway <= stops;

            default:
                return false;
        }
    }

    private static bool IsPositionStale(VehiclePosition vehicle, DateTimeOffset now) =>
        vehicle.GpsTimestamp is { } gps && now - gps > MaxGpsAge;

    private static bool InCooldown(
        IReadOnlyList<AlertEvent> history,
        WatchRule watch,
        string stopId,
        string vehicleId,
        DateTimeOffset now)
    {
        var cooldown = TimeSpan.FromMinutes(Math.Max(1, watch.CooldownMinutes));
        return history.Any(h =>
            h.WatchRuleId == watch.Id &&
            h.TriggerStopId == stopId &&
            h.VehicleId == vehicleId &&
            now - h.FiredAt < cooldown);
    }

    private static bool WasAlreadyFired(
        IReadOnlyList<AlertEvent> history,
        WatchRule watch,
        string tripId,
        DateOnly serviceDate,
        string stopId,
        string vehicleId) =>
        history.Any(h =>
            h.WatchRuleId == watch.Id &&
            h.TripId == tripId &&
            h.ServiceDate == serviceDate &&
            h.TriggerStopId == stopId &&
            h.VehicleId == vehicleId);

    /// <summary>
    /// Uses the feed's trip id when it exists in GTFS; otherwise falls back to
    /// geometric shape matching. The flag matters for the ETA: on a matched
    /// (not exact) trip the timetable times belong to a different run, so only
    /// the distance-based estimate is meaningful.
    /// </summary>
    private static (Trip? Trip, bool IsExact) ResolveTrip(
        GtfsData data, string routeId, VehiclePosition vehicle, DateOnly serviceDate)
    {
        if (vehicle.TripId is { } tripId &&
            data.FindTrip(tripId) is { } exact &&
            exact.RouteId == routeId)
        {
            return (exact, true);
        }

        return TripMatcher.Match(data, vehicle, routeId, serviceDate) is { } match
            ? (match.Trip, false)
            : (null, false);
    }

    /// <summary>
    /// Finds the scheduled call for a stop. Loop routes visit the same stop id
    /// twice, so the stored sequence is used to disambiguate, with a fallback to
    /// the first matching call inside the allowed range.
    /// </summary>
    private static StopTime? FindCall(
        IReadOnlyList<StopTime> stopTimes, string stopId, int preferredSequence, int? maxSequence = null)
    {
        var candidates = stopTimes
            .Where(st => st.StopId == stopId && (maxSequence is null || st.Sequence <= maxSequence))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(st => Math.Abs(st.Sequence - preferredSequence))
            .ThenBy(st => st.Sequence)
            .First();
    }

    private static string BuildMessage(
        VehiclePosition vehicle,
        WatchRule watch,
        Trip trip,
        WatchAlertStop alertStop,
        EtaResult boardingEta,
        EtaResult? alertEta)
    {
        var bus = string.IsNullOrEmpty(vehicle.VehicleId) ? "The bus" : $"Bus {vehicle.VehicleId}";
        var position = alertEta?.IsAtStop == true
            ? $"is at {alertStop.StopName}"
            : $"just passed {alertStop.StopName}";
        var towards = string.IsNullOrWhiteSpace(trip.Headsign)
            ? string.Empty
            : $" towards {trip.Headsign}";

        return $"{bus} on line {watch.RouteShortName}{towards} {position}. " +
               $"{boardingEta.RoundedMinutes} min to {watch.BoardingStopName}.";
    }
}
