using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using BusPing.Models;
using BusPing.Services.Interfaces;

// The AndroidX bindings annotate these fluent methods as nullable, so the
// compiler warns on every chained call even though the builder is non-null.
#pragma warning disable CS8602, CS8603

namespace BusPing.Services.Android;

/// <summary>
/// Creates the two notification channels and posts the alerts. Bus alerts are
/// high importance (heads-up + sound); the foreground-service notification is
/// low importance and silent.
/// </summary>
public sealed class AndroidNotificationService : INotificationService
{
    public const string AlertChannelId = "bus_alerts";
    public const string ServiceChannelId = "watch_service";
    public const int ServiceNotificationId = 1000;

    private readonly ISettingsService _settings;

    public AndroidNotificationService(ISettingsService settings)
    {
        _settings = settings;
    }

    public static void EnsureChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var context = global::Android.App.Application.Context;
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null)
        {
            return;
        }

        var alerts = new NotificationChannel(
            AlertChannelId,
            "Bus alerts",
            NotificationImportance.High)
        {
            Description = "When your bus is approaching your stop",
        };
        alerts.EnableVibration(true);
        manager.CreateNotificationChannel(alerts);

        var service = new NotificationChannel(
            ServiceChannelId,
            "Background watching",
            NotificationImportance.Low)
        {
            Description = "Keeps BusPing watching while a watch is active",
        };
        service.SetShowBadge(false);
        manager.CreateNotificationChannel(service);
    }

    public Task NotifyAlertAsync(
        WatchRule watch, AlertEvent alertEvent, CancellationToken cancellationToken = default)
    {
        EnsureChannels();

        var context = global::Android.App.Application.Context;
        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            ContextCompat.CheckSelfPermission(context, "android.permission.POST_NOTIFICATIONS")
                != Permission.Granted)
        {
            // The user denied notifications; the alert is still in history and
            // the dashboard shows an in-app banner.
            return Task.CompletedTask;
        }

        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        intent.PutExtra("watchId", watch.Id.ToString());

        var pending = PendingIntent.GetActivity(
            context,
            alertEvent.Id.GetHashCode(),
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var title = $"Line {watch.RouteShortName} · {alertEvent.TriggerStopName}";
        var builder = new NotificationCompat.Builder(context, AlertChannelId)
            .SetSmallIcon(Resource.Drawable.ic_bus_notification)
            .SetContentTitle(title)
            .SetContentText(alertEvent.Message)
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(alertEvent.Message))
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetCategory(NotificationCompat.CategoryAlarm)
            .SetAutoCancel(true)
            .SetContentIntent(pending);

        if (_settings.Current.SoundEnabled)
        {
            builder.SetDefaults((int)NotificationDefaults.Sound | (int)NotificationDefaults.Vibrate);
        }
        else
        {
            builder.SetSilent(true);
        }

        NotificationManagerCompat.From(context).Notify(alertEvent.Id.GetHashCode(), builder.Build());
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(string text, CancellationToken cancellationToken = default)
    {
        var context = global::Android.App.Application.Context;
        var notification = BuildServiceNotification(context, text);
        NotificationManagerCompat.From(context).Notify(ServiceNotificationId, notification);
        return Task.CompletedTask;
    }

    public static Notification BuildServiceNotification(Context context, string text)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var pending = PendingIntent.GetActivity(
            context, 0, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var stopIntent = new Intent(context, typeof(BusWatchForegroundService));
        stopIntent.SetAction(BusWatchForegroundService.ActionStop);
        var stopPending = PendingIntent.GetService(
            context, 1, stopIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new NotificationCompat.Builder(context, ServiceChannelId)
            .SetSmallIcon(Resource.Drawable.ic_bus_notification)
            .SetContentTitle("BusPing is watching")
            .SetContentText(text)
            .SetOngoing(true)
            .SetSilent(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .SetContentIntent(pending)
            .AddAction(0, "Stop", stopPending)
            .Build();
    }
}
