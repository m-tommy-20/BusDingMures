using System.IO.Compression;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusPing.Services;

/// <summary>
/// Downloads and caches the operator's GTFS feed, then hands the files to
/// <see cref="GtfsParser"/>. The feed is the primary schedule source: unlike the
/// timetable pages it has per-stop times, shapes and service calendars.
/// </summary>
public sealed class GtfsService : IGtfsService
{
    private const string FeedUrl = "https://live.transportlocal.ro/gtfs.zip";
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(7);

    private static readonly string[] RequiredFiles =
    [
        "routes.txt", "stops.txt", "trips.txt", "stop_times.txt", "calendar.txt", "shapes.txt",
    ];

    private readonly ILogger<GtfsService> _logger;
    private readonly HttpClient _http = HttpClients.Create(TimeSpan.FromMinutes(2));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GtfsService(ILogger<GtfsService> logger)
    {
        _logger = logger;
    }

    public GtfsData? Current { get; private set; }

    public bool IsLoading { get; private set; }

    public string? LastError { get; private set; }

    public event EventHandler? Changed;

    private static string CacheDirectory => Path.Combine(FileSystem.CacheDirectory, "gtfs");

    public async Task<GtfsData> LoadAsync(
        bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (Current is not null && !forceRefresh)
        {
            return Current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Current is not null && !forceRefresh)
            {
                return Current;
            }

            IsLoading = true;
            LastError = null;

            var directory = await EnsureFilesAsync(forceRefresh, cancellationToken);
            var data = await Task.Run(() => GtfsParser.Parse(directory), cancellationToken);

            Current = data;
            _logger.LogInformation(
                "GTFS loaded: {Routes} routes, {Stops} stops, {Trips} trips",
                data.Routes.Count, data.Stops.Count, data.Trips.Count);

            MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
            return data;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            _logger.LogError(ex, "GTFS load failed");
            throw;
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    private async Task<string> EnsureFilesAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var directory = CacheDirectory;
        Directory.CreateDirectory(directory);

        var stamp = Path.Combine(directory, "gtfs.zip");
        var hasFiles = RequiredFiles.All(f => File.Exists(Path.Combine(directory, f)));
        var isFresh = File.Exists(stamp) &&
                      DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp) < RefreshAfter;

        if (hasFiles && isFresh && !forceRefresh)
        {
            return directory;
        }

        _logger.LogInformation("Downloading GTFS feed from {Url}", FeedUrl);
        var tempZip = stamp + ".download";

        await using (var source = await _http.GetStreamAsync(FeedUrl, cancellationToken))
        await using (var target = File.Create(tempZip))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        // Extract over the existing files so a failed download never leaves an
        // empty cache directory behind.
        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(tempZip);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var destination = Path.Combine(directory, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
            }
        }, cancellationToken);

        File.Move(tempZip, stamp, overwrite: true);
        File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow);
        return directory;
    }
}
