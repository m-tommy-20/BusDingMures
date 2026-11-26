namespace BusPing.Services.Interfaces;

public interface IGtfsService
{
    /// <summary>The parsed feed, or null before the first successful load.</summary>
    GtfsData? Current { get; }

    bool IsLoading { get; }

    string? LastError { get; }

    /// <summary>Raised on the main thread after a successful load.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Loads the feed from the local cache, downloading it first when missing or
    /// stale. Safe to call concurrently; only one download runs at a time.
    /// </summary>
    Task<GtfsData> LoadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
