using Android.App;
using Android.Content.PM;
using Android.OS;
using BusPing.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace BusPing.Services.Android;

/// <summary>
/// Keeps evaluating watches while the app is in the background. Android requires
/// a visible, ongoing notification for this; there is no way around it for
/// near-real-time polling.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class BusWatchForegroundService : Service
{
    public const string ActionStart = "ro.busping.action.START_WATCH";
    public const string ActionStop = "ro.busping.action.STOP_WATCH";

    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public static bool IsRunning { get; private set; }

    public override IBinder? OnBind(global::Android.Content.Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        global::Android.Content.Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        AndroidNotificationService.EnsureChannels();
        var notification = AndroidNotificationService.BuildServiceNotification(
            this, "Starting…");

        if (OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            StartForeground(
                AndroidNotificationService.ServiceNotificationId,
                notification,
                ForegroundService.TypeDataSync);
        }
        else
        {
            StartForeground(AndroidNotificationService.ServiceNotificationId, notification);
        }

        if (_cancellation is null)
        {
            _cancellation = new CancellationTokenSource();
            IsRunning = true;
            _loop = Task.Run(() => RunAsync(_cancellation.Token));
        }

        return StartCommandResult.Sticky;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var services = IPlatformApplication.Current?.Services;
        var engine = services?.GetService<IAlertEngine>();
        var settings = services?.GetService<ISettingsService>();
        var notifications = services?.GetService<INotificationService>();

        if (engine is null)
        {
            StopSelf();
            return;
        }

        var delay = TimeSpan.FromSeconds(settings?.Current.PollSeconds ?? 10);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await engine.TickAsync(cancellationToken);

                if (notifications is not null)
                {
                    await notifications.UpdateStatusAsync(
                        $"Last check {DateTime.Now:HH:mm:ss} · tap to open", cancellationToken);
                }

                delay = TimeSpan.FromSeconds(settings?.Current.PollSeconds ?? 10);
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                break;
            }
        }
    }

    public override void OnDestroy()
    {
        IsRunning = false;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        base.OnDestroy();
    }
}
