using BusPing.Models;
using BusPing.Services;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataCheck;

/// <summary>
/// Deterministic check of the alert pipeline: a synthetic trip, a vehicle
/// standing at the warning stop, and a watch whose boarding stop comes later.
/// Verifies that the engine fires once, records history and does not repeat.
/// </summary>
public static class AlertCheck
{
    public static async Task<int> RunAsync()
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

        var alertStop = new Stop { Id = "A", Name = "Alert Stop", Latitude = 46.5, Longitude = 24.5 };
        var boardingStop = new Stop { Id = "B", Name = "Boarding Stop", Latitude = 46.52, Longitude = 24.52 };
        var trip = new Trip
        {
            Id = "T1",
            RouteId = "1",
            ServiceId = "3",
            DirectionId = "0",
            Headsign = "Test",
        };

        var data = new GtfsData
        {
            Routes = new Dictionary<string, Route>
            {
                ["1"] = new Route { Id = "1", ShortName = "1", LongName = "Test line" },
            },
            Stops = new Dictionary<string, Stop> { ["A"] = alertStop, ["B"] = boardingStop },
            Trips = new Dictionary<string, Trip> { ["T1"] = trip },
            StopTimesByTrip = new Dictionary<string, IReadOnlyList<StopTime>>
            {
                ["T1"] =
                [
                    new StopTime { TripId = "T1", StopId = "A", Sequence = 1, ArrivalSeconds = 8 * 3600, DepartureSeconds = 8 * 3600 },
                    new StopTime { TripId = "T1", StopId = "B", Sequence = 2, ArrivalSeconds = 8 * 600, DepartureSeconds = 8 * 600 },
                ],
            },
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
        };

        var watch = new WatchRule
        {
            Name = "Test watch",
            RouteId = "1",
            RouteShortName = "1",
            DirectionId = "0",
            BoardingStopId = "B",
            BoardingStopName = boardingStop.Name,
            BoardingSequence = 2,
            Enabled = true,
            Days = WeekdayFlags.EveryDay,
            WindowStartMinutes = 0,
            WindowEndMinutes = 24 * 60 - 1,
            CooldownMinutes = 10,
            AlertStops =
            [
                new WatchAlertStop
                {
                    StopId = "A",
                    StopName = alertStop.Name,
                    StopSequence = 1,
                    TriggerMode = TriggerMode.AtStop,
                },
            ],
        };

        var now = DateTimeOffset.Now;
        var vehicle = new VehiclePosition
        {
            VehicleId = "TESTBUS",
            Route = "1",
            RouteId = "1",
            TripId = "T1",
            DirectionId = "0",
            Latitude = alertStop.Latitude,
            Longitude = alertStop.Longitude,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "A" },
        };

        var notifier = new FakeNotifier();
        var history = new FakeHistory();
        var engine = new AlertEngine(
            new FakeGtfs(data),
            new FakeLive(vehicle),
            new FakeWatches(watch),
            history,
            new EtaService(),
            notifier,
            NullLogger<AlertEngine>.Instance);

        await engine.TickAsync();
        Check("Alert fired at the warning stop", notifier.Count == 1, notifier.Count.ToString());
        Check("Alert persisted to history", history.Events.Count == 1);
        Check("Alert message names the bus and stop",
            notifier.LastMessage?.Contains("TESTBUS") == true &&
            notifier.LastMessage.Contains(alertStop.Name),
            notifier.LastMessage ?? "(none)");

        await engine.TickAsync();
        Check("Same trip does not alert twice", notifier.Count == 1, notifier.Count.ToString());

        // Move the bus away from the stop: it must not fire again either.
        vehicle = new VehiclePosition
        {
            VehicleId = "TESTBUS",
            Route = "1",
            RouteId = "1",
            TripId = "T1",
            DirectionId = "0",
            Latitude = 46.0,
            Longitude = 24.0,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "A" },
        };
        var engine2 = new AlertEngine(
            new FakeGtfs(data),
            new FakeLive(vehicle),
            new FakeWatches(watch),
            history,
            new EtaService(),
            notifier,
            NullLogger<AlertEngine>.Instance);
        await engine2.TickAsync();
        Check("No alert while the bus is far away", notifier.Count == 1, notifier.Count.ToString());

        // The warning stop is behind the bus by more than a kilometre: that is
        // not a "just missed it" case and must stay silent.
        var pastVehicle = new VehiclePosition
        {
            VehicleId = "TESTBUS2",
            Route = "1",
            RouteId = "1",
            TripId = "T1",
            DirectionId = "0",
            Latitude = 46.51,
            Longitude = 24.51,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "B" },
        };
        var pastNotifier = new FakeNotifier();
        var engine3 = new AlertEngine(
            new FakeGtfs(data),
            new FakeLive(pastVehicle),
            new FakeWatches(watch),
            new FakeHistory(),
            new EtaService(),
            pastNotifier,
            NullLogger<AlertEngine>.Instance);
        await engine3.TickAsync();
        Check("Stop passed long ago does not fire", pastNotifier.Count == 0, pastNotifier.Count.ToString());

        // The feed predicting this stop as "next" is not the same as the bus
        // being there: a vehicle 13 km away must stay silent.
        var farVehicle = new VehiclePosition
        {
            VehicleId = "TESTBUS3",
            Route = "1",
            RouteId = "1",
            TripId = "T1",
            DirectionId = "0",
            Latitude = 46.4,
            Longitude = 24.4,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "A" },
        };
        var farNotifier = new FakeNotifier();
        var engine4 = new AlertEngine(
            new FakeGtfs(data),
            new FakeLive(farVehicle),
            new FakeWatches(watch),
            new FakeHistory(),
            new EtaService(),
            farNotifier,
            NullLogger<AlertEngine>.Instance);
        await engine4.TickAsync();
        Check("Predicted next stop does not mean the bus is at the stop",
            farNotifier.Count == 0, farNotifier.Count.ToString());

        // --- Incoming-run warnings ----------------------------------------
        var terminus = new Stop { Id = "T", Name = "Sapientia", Latitude = 46.54, Longitude = 24.55 };
        var approach = new Stop { Id = "C", Name = "Hotel Business", Latitude = 46.545, Longitude = 24.555 };
        var nextOutbound = new Stop { Id = "M", Name = "Dedeman", Latitude = 46.55, Longitude = 24.56 };

        var inboundTrip = new Trip
        {
            Id = "IN", RouteId = "1", ServiceId = "3", DirectionId = "1", Headsign = "Sapientia",
        };
        var outboundTrip = new Trip
        {
            Id = "OUT", RouteId = "1", ServiceId = "3", DirectionId = "0", Headsign = "Aleea Carpati",
        };

        var incomingData = new GtfsData
        {
            Routes = new Dictionary<string, Route> { ["1"] = new Route { Id = "1", ShortName = "1" } },
            Stops = new Dictionary<string, Stop> { ["T"] = terminus, ["C"] = approach, ["M"] = nextOutbound },
            Trips = new Dictionary<string, Trip> { ["IN"] = inboundTrip, ["OUT"] = outboundTrip },
            StopTimesByTrip = new Dictionary<string, IReadOnlyList<StopTime>>
            {
                ["IN"] =
                [
                    new StopTime { TripId = "IN", StopId = "C", Sequence = 1, ArrivalSeconds = 9 * 3600, DepartureSeconds = 9 * 3600 },
                    new StopTime { TripId = "IN", StopId = "T", Sequence = 2, ArrivalSeconds = 9 * 600, DepartureSeconds = 9 * 600 },
                ],
                ["OUT"] =
                [
                    new StopTime { TripId = "OUT", StopId = "T", Sequence = 1, ArrivalSeconds = 9 * 600, DepartureSeconds = 9 * 600 },
                    new StopTime { TripId = "OUT", StopId = "M", Sequence = 2, ArrivalSeconds = 9 * 660, DepartureSeconds = 9 * 660 },
                ],
            },
            Calendars = data.Calendars,
            Exceptions = [],
        };

        var incomingWatch = new WatchRule
        {
            Name = "Boarding at the terminus",
            RouteId = "1",
            RouteShortName = "1",
            DirectionId = "0",
            BoardingStopId = "T",
            BoardingStopName = terminus.Name,
            BoardingSequence = 1,
            Enabled = true,
            Days = WeekdayFlags.EveryDay,
            WindowStartMinutes = 0,
            WindowEndMinutes = 24 * 60 - 1,
            AlertStops =
            [
                new WatchAlertStop
                {
                    StopId = "C", StopName = approach.Name, StopSequence = 1,
                    TriggerMode = TriggerMode.AtStop, IsIncoming = true,
                },
            ],
        };

        var inboundVehicle = new VehiclePosition
        {
            VehicleId = "INBUS",
            Route = "1",
            RouteId = "1",
            TripId = "IN",
            DirectionId = "1",
            Latitude = approach.Latitude,
            Longitude = approach.Longitude,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "C" },
        };

        var incomingNotifier = new FakeNotifier();
        var incomingEngine = new AlertEngine(
            new FakeGtfs(incomingData),
            new FakeLive(inboundVehicle),
            new FakeWatches(incomingWatch),
            new FakeHistory(),
            new EtaService(),
            incomingNotifier,
            NullLogger<AlertEngine>.Instance);

        await incomingEngine.TickAsync();
        Check("Incoming-run warning fires for the bus approaching the terminus",
            incomingNotifier.Count == 1, incomingNotifier.Count.ToString());
        Check("Incoming message counts down to the terminus",
            incomingNotifier.LastMessage?.Contains("min to Sapientia") == true,
            incomingNotifier.LastMessage ?? "(none)");

        // The same watch must stay silent for a bus on the outbound trip.
        var outboundVehicle = new VehiclePosition
        {
            VehicleId = "OUTBUS",
            Route = "1",
            RouteId = "1",
            TripId = "OUT",
            DirectionId = "0",
            Latitude = terminus.Latitude,
            Longitude = terminus.Longitude,
            PositionSource = "thoreb",
            AssignmentStatus = "gtfs-confirmed",
            GpsTimestamp = now,
            Schedule = new LiveSchedule { StopId = "T" },
        };
        var forwardNotifier = new FakeNotifier();
        var forwardEngine = new AlertEngine(
            new FakeGtfs(incomingData),
            new FakeLive(outboundVehicle),
            new FakeWatches(incomingWatch),
            new FakeHistory(),
            new EtaService(),
            forwardNotifier,
            NullLogger<AlertEngine>.Instance);

        await forwardEngine.TickAsync();
        Check("Incoming warnings never fire for the outbound bus", forwardNotifier.Count == 0,
            forwardNotifier.Count.ToString());

        return failures;
    }

    private sealed class FakeGtfs(GtfsData data) : IGtfsService
    {
        public GtfsData? Current => data;
        public bool IsLoading => false;
        public string? LastError => null;
        public event EventHandler? Changed { add { } remove { } }

        public Task<GtfsData> LoadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(data);
    }

    private sealed class FakeLive(VehiclePosition vehicle) : ILiveVehicleService
    {
        public DateTimeOffset? LastUpdated => DateTimeOffset.Now;
        public string? LastError => null;
        public bool IsStale => false;

        public Task<IReadOnlyList<VehiclePosition>> GetVehiclesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VehiclePosition>>([vehicle]);
    }

    private sealed class FakeWatches(WatchRule watch) : IWatchRepository
    {
        public Task<IReadOnlyList<WatchRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WatchRule>>([watch]);

        public Task<WatchRule?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<WatchRule?>(watch.Id == id ? watch : null);

        public Task UpsertAsync(WatchRule rule, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeHistory : IAlertHistoryRepository
    {
        public List<AlertEvent> Events { get; } = [];

        public Task<IReadOnlyList<AlertEvent>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlertEvent>>(Events.ToArray());

        public Task AddAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(alertEvent);
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Events.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : INotificationService
    {
        public int Count { get; private set; }
        public string? LastMessage { get; private set; }

        public Task NotifyAlertAsync(
            WatchRule watch, AlertEvent alertEvent, CancellationToken cancellationToken = default)
        {
            Count++;
            LastMessage = alertEvent.Message;
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
