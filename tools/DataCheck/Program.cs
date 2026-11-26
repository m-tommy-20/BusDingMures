using System.IO.Compression;
using System.Text;
using BusPing.Models;
using BusPing.Services;
using DataCheck;

Console.OutputEncoding = Encoding.UTF8;

var failures = 0;

void Check(string name, bool condition, string detail = "")
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? ": " + detail : "")}");
    if (!condition)
    {
        failures++;
    }
}

// --- CSV parser -----------------------------------------------------------
var fields = Csv.SplitLine("a,\"b,with comma\",\"quote \"\"inside\"\"\",c");
Check("CSV quoted field with comma", fields[1] == "b,with comma", fields[1]);
Check("CSV escaped quote", fields[2] == "quote \"inside\"", fields[2]);
Check("CSV field count", fields.Length == 4, fields.Length.ToString());

// --- GTFS -----------------------------------------------------------------
var baseDir = Path.Combine(Path.GetTempPath(), "busping-datacheck");
var gtfsDir = Path.Combine(baseDir, "gtfs");
Directory.CreateDirectory(gtfsDir);

var zipPath = Path.Combine(baseDir, "gtfs.zip");
if (!File.Exists(zipPath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(zipPath) > TimeSpan.FromDays(1))
{
    Console.WriteLine("Downloading GTFS…");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    var bytes = await http.GetByteArrayAsync("https://live.transportlocal.ro/gtfs.zip");
    await File.WriteAllBytesAsync(zipPath, bytes);
}

using (var archive = ZipFile.OpenRead(zipPath))
{
    foreach (var entry in archive.Entries)
    {
        if (string.IsNullOrEmpty(entry.Name))
        {
            continue;
        }

        var destination = Path.Combine(gtfsDir, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        entry.ExtractToFile(destination, overwrite: true);
    }
}

var data = GtfsParser.Parse(gtfsDir);
Check("GTFS routes parsed", data.Routes.Count >= 30, data.Routes.Count.ToString());
Check("GTFS stops parsed", data.Stops.Count >= 200, data.Stops.Count.ToString());
Check("GTFS trips parsed", data.Trips.Count > 3000, data.Trips.Count.ToString());
Check("GTFS stop times parsed",
    data.StopTimesByTrip.Values.Sum(v => v.Count) > 50_000,
    data.StopTimesByTrip.Values.Sum(v => v.Count).ToString());
Check("GTFS calendars parsed", data.Calendars.Count > 0, data.Calendars.Count.ToString());

var line1 = data.Routes.Values.FirstOrDefault(r => r.ShortName == "1");
Check("Line 1 exists", line1 is not null);

if (line1 is not null)
{
    var trips = data.Trips.Values.Where(t => t.RouteId == line1.Id).ToList();
    var firstTrip = trips.First();
    var calls = data.StopTimes(firstTrip.Id);
    Check("Line 1 has trips", trips.Count > 0, trips.Count.ToString());
    Check("Line 1 trip has stops", calls.Count > 3, calls.Count.ToString());

    if (calls.Count > 6)
    {
        var boarding = calls[5];
        var stop = data.FindStop(boarding.StopId);
        Console.WriteLine($"       example: trip {firstTrip.Id} boards at {stop?.Name} " +
                          $"at {boarding.Arrival}");
    }
}

// --- Live feed ------------------------------------------------------------
Console.WriteLine("Downloading live vehicles…");
IReadOnlyList<VehiclePosition> vehicles;
try
{
    Console.WriteLine("Downloading live vehicles…");
    string liveJson;
    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
    {
        // Same decompression the app uses; without it the payload is ~120 KB.
        liveJson = await http.GetStringAsync("https://live.transportlocal.ro/journeys.json");
    }

    vehicles = LiveFeedParser.Parse(liveJson);
}
catch (Exception ex)
{
    Console.WriteLine($"       live feed unavailable ({ex.Message}); skipping live checks");
    vehicles = [];
}

if (vehicles.Count == 0)
{
    Console.WriteLine("SKIP  live feed checks (no vehicles returned)");
}
else
{
    Check("Live vehicles parsed", vehicles.Count > 0, vehicles.Count.ToString());
    var confirmed = vehicles.Count(v => v.IsReliable);
    Check("Some vehicles are gtfs-confirmed", confirmed > 0, confirmed.ToString());
    Check("Every vehicle has an id", vehicles.All(v => v.VehicleId.Length > 0));
}

var sample = vehicles.FirstOrDefault(v => v.IsReliable && v.TripId is not null &&
                                          data.FindTrip(v.TripId) is not null);
if (sample is null)
{
    Console.WriteLine("       no usable vehicle for an ETA demo right now");
}
else
{
    var trip = data.FindTrip(sample.TripId!)!;
    var calls = data.StopTimes(trip.Id);

    // Prefer a stop a few calls ahead of where the vehicle is right now.
    var nextIndex = 0;
    if (sample.Schedule?.StopId is { } nextStopId)
    {
        var index = calls.ToList().FindIndex(st => st.StopId == nextStopId);
        if (index >= 0)
        {
            nextIndex = index;
        }
    }

    var targetIndex = Math.Min(calls.Count - 1, nextIndex + 3);
    var target = calls[targetIndex];
    var eta = new EtaService().Compute(data, sample, trip, target, DateTimeOffset.Now);
    var targetStop = data.FindStop(target.StopId);

    Check("ETA computed for a live vehicle", eta is not null);
    if (eta is not null)
    {
        Console.WriteLine($"       {sample.VehicleId} line {sample.Route} → {targetStop?.Name}: " +
                          $"{eta.Minutes:0.0} min, {eta.StopsAway} stop(s) away, " +
                          $"{eta.DistanceMeters:0} m, confidence {eta.Confidence}");
    }
}

// --- Route aliases --------------------------------------------------------
Check("Alias 202 matches 2B",
    RouteMatcher.Matches("2B", "202", "202", "202"));
Check("Alias 2B vs 30B",
    RouteMatcher.Matches("30B", "302", "2B", "202") == false);

// --- Live direction matching ----------------------------------------------
var route26 = vehicles.Where(v => v.Route == "26").ToList();
if (route26.Count > 0)
{
    Console.WriteLine($"       live route 26 vehicles ({route26.Count}):");
    foreach (var vehicle in route26)
    {
        var match = TripMatcher.Match(data, vehicle, "26", DateOnly.FromDateTime(DateTime.Now));
        Console.WriteLine(
            $"       {vehicle.VehicleId} bearing {vehicle.Bearing:0}° -> " +
            (match is null
                ? "no geometric match"
                : $"direction {match.Trip.DirectionId} ({match.Trip.Headsign}), " +
                  $"{match.DistanceMeters:0} m off shape, Δbearing {match.BearingDelta:0}°"));
    }
}

// --- Alert engine ---------------------------------------------------------
failures += await AlertCheck.RunAsync();

// --- Shape matching -------------------------------------------------------
failures += MatchCheck.Run();

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
