# Android Background Watching & Notifications

This is the design note for the riskiest part of BusPing: getting a warning onto
the phone while the app is not in the foreground.

## 1. Decision: foreground service

Use an Android **foreground service** with an ongoing notification while at
least one watch is active.

Why:

- The app needs to poll `journeys.json` every 5–15 seconds and evaluate rules.
- `WorkManager`'s minimum periodic interval is **15 minutes** — far too coarse
  for "the bus is 3 stops away".
- `AlarmManager` alone can wake the app at a time, but cannot continuously track
  a moving bus.
- A foreground service is the standard Android mechanism for this, and it is
  visible to the user, which is honest about battery/data use.

The ongoing notification is not optional: Android requires it for a foreground
service. It can be low-importance and silent, e.g.:

> BusPing is watching line 1 · 07:16 departure · tap to open

A server-side evaluator + FCM push would avoid the persistent notification and
save battery, but it is a second system (backend, device tokens, deployment).
That is a stretch goal, not the MVP.

---

## 2. Lifecycle

```
User enables a watch
        |
        v
POST_NOTIFICATIONS granted? -- no --> prompt / in-app warning
        |
        yes
        v
Schedule watch window (AlarmManager)
        |
        v
Start BusWatchForegroundService
        |
        v
Foreground notification + poll loop
        |
        +--> match trip + compute ETA
        +--> fire high-priority alert notification
        |
        v
Window ends / user taps Stop / all watches disabled
        |
        v
Stop foreground service, release wake lock
```

For the MVP, also provide a manual **Start watching / Stop watching** button on
the Dashboard. It makes the demo reliable even if exact alarms are not granted.

---

## 3. Manifest and permissions

`Platforms/Android/AndroidManifest.xml`:

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_DATA_SYNC" />
<uses-permission android:name="android.permission.WAKE_LOCK" />
<uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED" />
<uses-permission android:name="android.permission.SCHEDULE_EXACT_ALARM" />
<uses-permission android:name="android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS" />

<application ...>
    <service
        android:name=".Services.BusWatchForegroundService"
        android:exported="false"
        android:stopWithTask="false"
        android:foregroundServiceType="dataSync" />

    <receiver
        android:name=".Services.WatchWindowReceiver"
        android:exported="false" />

    <receiver
        android:name=".Services.BootReceiver"
        android:enabled="true"
        android:exported="false">
        <intent-filter>
            <action android:name="android.intent.action.BOOT_COMPLETED" />
            <action android:name="android.intent.action.MY_PACKAGE_REPLACED" />
        </intent-filter>
    </receiver>
</application>
```

Notes:

- `FOREGROUND_SERVICE_DATA_SYNC` is required on Android 14+ for a `dataSync`
  foreground service.
- `POST_NOTIFICATIONS` is a runtime permission on Android 13+.
- `SCHEDULE_EXACT_ALARM` is a special app access on Android 12+; the user grants
  it in system settings. If it is denied, fall back to inexact alarms and warn.
- `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` is only for the convenience prompt.
  A Play Store release may need a different policy-compliant flow; this is a
  course/sideload app.

---

## 4. Notification channels

Create channels at app/service start (idempotent):

```csharp
var manager = (NotificationManager)GetSystemService(NotificationService)!;

var alerts = new NotificationChannel(
    "bus_alerts",
    "Bus alerts",
    NotificationImportance.High)
{
    Description = "When your bus is approaching"
};
alerts.EnableVibration(true);
manager.CreateNotificationChannel(alerts);

var service = new NotificationChannel(
    "watch_service",
    "Background watching",
    NotificationImportance.Low)
{
    Description = "Keeps BusPing watching while a watch is active"
};
manager.CreateNotificationChannel(service);
```

- `bus_alerts`: high importance, sound/vibration, heads-up. This is the actual
  "leave now" warning.
- `watch_service`: low importance, silent, ongoing. This is the foreground
  service requirement.

Alert notification content:

- Title: `Line 1 · 3 stops away`
- Body: `MS431 is at Piața Trandafirilor. ~4 min to your stop (Spitalul Județean).`
- Tap: open the watch detail.
- Actions: `Snooze 10 min`, `Stop watch` (optional).

Use `NotificationCompat.Builder` with a stable ID derived from
`watchId + tripId + triggerStopId` so the same event does not stack.

---

## 5. Service skeleton

```csharp
[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class BusWatchForegroundService : Service
{
    private const int ServiceNotificationId = 1000;
    private CancellationTokenSource? _cts;
    private PowerManager.WakeLock? _wakeLock;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(
        Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == Actions.Stop)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        StartForeground(
            ServiceNotificationId,
            BuildServiceNotification(),
            ForegroundService.TypeDataSync);

        AcquireWakeLock();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunLoopAsync(_cts.Token));
        return StartCommandResult.Sticky;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var services = IPlatformApplication.Current?.Services;
        var engine = services?.GetRequiredService<IAlertEngine>();
        if (engine is null) { StopSelf(); return; }

        var delay = TimeSpan.FromSeconds(10);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await engine.TickAsync(ct);      // poll + evaluate + notify
                delay = TimeSpan.FromSeconds(10);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 60));
            }

            await Task.Delay(delay, ct);
        }
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        ReleaseWakeLock();
        base.OnDestroy();
    }
}
```

Start it from the Activity/MAUI code (never from a random background context):

```csharp
var context = Android.App.Application.Context;
var intent = new Intent(context, typeof(BusWatchForegroundService));
if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
    ContextCompat.StartForegroundService(context, intent);
else
    context.StartService(intent);
```

Android 12+ throws `ForegroundServiceStartNotAllowedException` if the app tries
to start the service from the background without an exemption. The MVP starts it
from the visible app; exact-alarm broadcasts are the scheduled-start path.

---

## 6. Scheduling watch windows

A watch has a time window (e.g. weekdays 07:00–07:40). Schedule:

- **Start** at `windowStart - 10 min` (or the user's configured lead).
- **End** when the window ends; the service stops itself.

Use `AlarmManager` with a `PendingIntent` to `WatchWindowReceiver`:

```csharp
var alarm = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
var intent = new Intent(context, typeof(WatchWindowReceiver))
    .SetAction(Actions.StartWatch)
    .PutExtra("watchId", watch.Id);
var pending = PendingIntent.GetBroadcast(
    context, requestCode, intent,
    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

if (Build.VERSION.SdkInt >= BuildVersionCodes.S &&
    !alarm.CanScheduleExactAlarms())
{
    // Ask the user to grant "Alarms & reminders", or use an inexact alarm.
    alarm.SetAndAllowWhileIdle(AlarmManager.RtcWakeup, triggerAtMillis, pending);
}
else
{
    alarm.SetExactAndAllowWhileIdle(AlarmManager.RtcWakeup, triggerAtMillis, pending);
}
```

`WatchWindowReceiver` starts the foreground service. Re-schedule on:

- app start,
- watch create/edit/delete,
- boot / package replace,
- timezone change (optional).

Caveats:

- Exact alarms are a special access on Android 12+; if denied, the start can be
  delayed by minutes. Show this in the settings screen.
- If the app is force-stopped, alarms are cancelled and the service will not
  start until the user opens the app again.
- Some OEMs ignore wakeups unless the app is whitelisted.

---

## 7. Polling strategy

| Situation | Interval | Why |
|---|---|---|
| Watch window active, far from boarding | 10–15 s | Enough accuracy, lower data/battery |
| Last 15 min before boarding | 5 s | Higher precision when it matters |
| No active window | stop service | No background cost |
| Network error | 10 → 20 → 40 → 60 s | Backoff; avoid hammering |
| Stale GPS (> 5 min) | 30 s + stale UI | Do not fire "at stop" on old data |

Enable gzip/deflate:

```csharp
var handler = new SocketsHttpHandler
{
    AutomaticDecompression =
        DecompressionMethods.GZip | DecompressionMethods.Deflate
};
var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("BusPing/1.0 (student project)");
```

`journeys.json` is ~123 KB uncompressed. With gzip it is typically far smaller.
Polling every 10 seconds is roughly 6 requests/minute; only during a 20–40 minute
window. Stop the service as soon as the trip has passed the boarding stop.

Do **not** poll all day. Do **not** poll faster than 5 seconds.

---

## 8. Battery and OEM behavior

- Acquire a **partial wake lock** only while the poll loop is active, with a
  timeout (e.g. 30 minutes), and release it in `OnDestroy`.
- Ask the user to allow BusPing to ignore battery optimizations if alerts are
  missed:
  - `PowerManager.IsIgnoringBatteryOptimizations(packageName)`
  - `Settings.ActionRequestIgnoreBatteryOptimizations` with
    `package:<packageName>` (requires the manifest permission), or open the
    general battery-optimization settings page.
- Add a short in-app help page for Xiaomi/MIUI, Samsung, Huawei, OnePlus, etc.
  ("Autostart", "Allow background activity", "No battery restrictions").
- Test with the screen off, not just while the app is open.

Android 15 note: `dataSync` foreground services have a 6-hour-per-24-hour limit.
Our watch windows are short, so this is acceptable. Handle `OnTimeout` by
stopping gracefully and showing a notification if the limit is hit.

---

## 9. Permissions flow in MAUI

On first launch / first watch:

1. **Notifications** (Android 13+):
   ```csharp
   var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
   if (status != PermissionStatus.Granted)
       status = await Permissions.RequestAsync<Permissions.PostNotifications>();
   ```
   If denied, show an in-app warning and a button to open app notification
   settings.

2. **Exact alarms** (Android 12+):
   ```csharp
   var alarm = (AlarmManager)Platform.AppContext.GetSystemService(Context.AlarmService)!;
   if (!alarm.CanScheduleExactAlarms())
   {
       var intent = new Intent(Settings.ActionRequestScheduleExactAlarm);
       intent.SetData(Android.Net.Uri.Parse($"package:{Platform.AppContext.PackageName}"));
       Platform.CurrentActivity?.StartActivity(intent);
   }
   ```

3. **Battery optimization** (optional but recommended):
   open the system page; do not silently rely on it.

Never block the app if a permission is denied. Fall back to in-app alerts and a
clear explanation.

---

## 10. Testing checklist

- [ ] Fresh install, grant notifications, receive a test notification.
- [ ] Deny notifications; app explains why alerts will not appear.
- [ ] App in foreground; alert fires and UI updates.
- [ ] App in background; alert fires.
- [ ] Screen off for 10+ minutes; alert fires.
- [ ] Battery saver / Doze; note behavior.
- [ ] Reboot with a scheduled watch; alarm re-registers (if implemented).
- [ ] Force stop; confirm behavior and document it.
- [ ] No network; backoff, stale indicator, recovery.
- [ ] Exact alarm granted and denied; behavior documented.
- [ ] Multiple watches; no duplicate notifications.
- [ ] Notification tap opens the right watch.
- [ ] Stop action stops the service and the ongoing notification.
- [ ] Portrait and landscape; small and large screens.
- [ ] Device timezone not Europe/Bucharest; ETA still correct.

---

## 11. Known limitations (state these in the documentation)

- A force-stopped app cannot receive alerts until it is opened again.
- OEM battery managers may kill even a foreground service; the user must
  whitelist BusPing.
- Android does not guarantee exact polling under Doze; a foreground service is
  best effort, not a real-time OS guarantee.
- The persistent notification is required by Android; it cannot be hidden.
- The most reliable long-term architecture is a server-side evaluator that sends
  FCM push messages. That is a stretch goal for this project.
