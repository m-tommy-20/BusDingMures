using System.Text.Json;
using BusPing.Models;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BusPing.Services;

/// <summary>
/// Polls the operator's live vehicle feed. The feed omits CORS headers, which is
/// fine for a native client, and is served with gzip.
/// </summary>
public sealed class LiveVehicleService : ILiveVehicleService
{
    private const string FeedUrl = "https://live.transportlocal.ro/journeys.json";

    private readonly ILogger<LiveVehicleService> _logger;
    private readonly HttpClient _http = HttpClients.Create(TimeSpan.FromSeconds(15));
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LiveVehicleService(ILogger<LiveVehicleService> logger)
    {
        _logger = logger;
    }

    public DateTimeOffset? LastUpdated { get; private set; }

    public string? LastError { get; private set; }

    public bool IsStale { get; private set; }

    private static string CachePath =>
        Path.Combine(FileSystem.CacheDirectory, "live", "journeys-last.json");

    public async Task<IReadOnlyList<VehiclePosition>> GetVehiclesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var json = await _http.GetStringAsync(FeedUrl, cancellationToken);
            var vehicles = LiveFeedParser.Parse(json);

            await CacheAsync(json, cancellationToken);
            LastUpdated = DateTimeOffset.Now;
            LastError = null;
            IsStale = false;
            return vehicles;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "Live vehicle poll failed; using cached positions");

            var cached = await ReadCacheAsync(cancellationToken);
            IsStale = cached.Count > 0;
            return cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task CacheAsync(string json, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        await File.WriteAllTextAsync(CachePath, json, cancellationToken);
    }

    private async Task<IReadOnlyList<VehiclePosition>> ReadCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return [];
            }

            var json = await File.ReadAllTextAsync(CachePath, cancellationToken);
            return LiveFeedParser.Parse(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Cached live data unreadable");
            return [];
        }
    }
}
