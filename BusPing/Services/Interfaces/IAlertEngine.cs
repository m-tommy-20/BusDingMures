using BusPing.Models;

namespace BusPing.Services.Interfaces;

public interface IAlertEngine
{
    DateTimeOffset? LastRun { get; }

    string? LastError { get; }

    /// <summary>Raised after an alert has been persisted and shown.</summary>
    event EventHandler<AlertEvent>? AlertFired;

    /// <summary>
    /// One full evaluation pass: load live vehicles, match them to active watch
    /// rules, compute ETAs, fire eligible alerts. Designed to be called every
    /// few seconds by the foreground service.
    /// </summary>
    Task TickAsync(CancellationToken cancellationToken = default);
}

public interface INotificationService
{
    /// <summary>Shows the high-priority "your bus is coming" notification.</summary>
    Task NotifyAlertAsync(WatchRule watch, AlertEvent alertEvent, CancellationToken cancellationToken = default);

    /// <summary>Updates the persistent "watching" notification, if any.</summary>
    Task UpdateStatusAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>Starts and stops the background watcher (a foreground service on Android).</summary>
public interface IWatchHost
{
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
