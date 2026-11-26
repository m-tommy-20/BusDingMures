# BusPing — Project Plan (Android / .NET MAUI)

> Working title. A personalized bus watcher/alerts app for the Târgu Mureș local
> bus network, built on the public data behind `live.transportlocal.ro`.
>
> **Chosen platform: .NET MAUI (Android), MVVM.** The point of the app is a
> notification on the phone, so background execution is a first-class feature,
> not an afterthought.

---

## 1. Context and constraints

The repository contains `NET_Projektfeladat_Egyeni_alkalmazasfejlesztes.pdf`.
That brief requires:

- One of: WPF (MVVM), **.NET MAUI (MVVM)**, Blazor Hybrid, or ASP.NET Core MVC.
- Persistent storage that survives restart.
- 5+ meaningful LINQ queries, including `GroupBy` and an aggregate.
- Dark/light theme switchable at runtime.
- A settings screen with 3+ persisted settings.
- 4+ screens with real navigation, 2+ related entities, CRUD, validation,
  and a non-trivial domain calculation.
- 8 mandatory points + max 1 optional point.
- Defense/demo: **15 December 2026**. Half-state demo: 1 December 2026.
- Topic approval is expected **before** development starts.

The course explicitly warns that the MAUI emulator is slow, so plan extra time
and prefer a physical Android phone for development and the demo.

---

## 2. What the data actually looks like (verified)

No HTML scraping is needed for the core feature. The site is a public SPA that
already consumes machine-readable data.

| Purpose | URL | Format | Size / cadence | Notes |
|---|---|---|---|---|
| Static schedule | `https://live.transportlocal.ro/gtfs.zip` | ZIP of GTFS CSV files | ~595 KB, changes rarely | Routes, trips, stops, stop times, shapes, calendar |
| Live vehicles | `https://live.transportlocal.ro/journeys.json` | JSON | ~123 KB, site polls every 3 s | GPS, route/trip assignment, next-stop prediction |
| Service alerts | `https://live.transportlocal.ro/alerts.json` | JSON | occasional | Optional disruption banner |
| Human timetable page | `https://new.transportlocal.ro/linia/{line}` | HTML + embedded `timetableData` JSON | static | Fallback/validation only |

### 2.1 Live JSON (`journeys.json`)

Each element of `journeys[]` includes:

- `vehicle_id` (e.g. `MS431`), `route` (short name, e.g. `2B`), `route_id` (e.g. `202`)
- `trip_id`, `headsign`, `direction_id`, `service_id`, `shape_id`
- `lat`, `lon`, `speed`, `direction` (bearing), `gps_timestamp`
- `position_source` (`thoreb` = live GPS, `legacy` = fallback)
- `timestamp_source` (`gps` / `generated`)
- `assignment_status` (`gtfs-confirmed`, `unverified`, `unavailable`)
- `schedule` — the operator's current next-stop prediction:
  - `stop_id`, `stop_name`, `event_type`, `time` (epoch seconds)
  - `reported_delay_seconds`, `state`, `reference_quality`, `reason`

Some vehicles have `trip_id: null` and `assignment_status: "unavailable"`.
The app must degrade gracefully to schedule-only or hide them.

### 2.2 GTFS (`gtfs.zip`)

Current feed facts:

- 35 routes, 237 stops, 4,078 trips, 61,120 stop times, 16,441 shape points.
- UTF-8 CSV.
- Service IDs: `3` = weekdays, `4` = Saturday, `5` = Sunday; exceptions in
  `calendar_dates.txt`.
- `shapes.txt` is present, so route geometry and along-route distance are possible.
- No `frequencies.txt`; every trip is explicit.
- Feed validity: 2025-01-01 to 2027-03-24.

Route ID vs short name mismatches to handle explicitly:

| Live `route` / short name | GTFS `route_id` |
|---|---|
| `2B` | `202` |
| `10B` | `102` |
| `19L` | `192` |
| `23B` | `232` |
| `30B` | `302` |

Stops can repeat in a loop (e.g. `Autogara Transport Local` appears twice on
line 1). **Never identify a stop by name alone**; use `stop_id` plus the
occurrence/sequence inside the selected trip.

### 2.3 Timetable pages

`https://new.transportlocal.ro/linia/1` embeds a `timetableData` JSON object with
directions, ordered stations, and departure times. Those times are mostly only
at the terminals, so GTFS `stop_times.txt` is the better source. Keep the page
as a fallback/validation path, not the primary data source.

### 2.4 Networking constraints

- The live endpoints do **not** send `Access-Control-Allow-Origin`; this is not
  a problem for a native MAUI app.
- The server advertises `Vary: Accept-Encoding`; enable gzip/deflate in
  `HttpClient` to cut the 123 KB payload dramatically.
- `journeys.json` timestamps are UTC (`Z`); GTFS times are local Europe/Bucharest.
  Convert explicitly and handle trips after midnight (`>24:00:00`).

---

## 3. Product scope

### 3.1 User story

> I know which bus I take and where I board. I want my phone to tell me when that
> bus is approaching, so I can leave at the right time. I also want it to start
> watching automatically for the 07:16 departure and ping me when the bus reaches
> earlier stops on the route.

### 3.2 Core flow

1. Pick a **route** (e.g. line 1).
2. Pick a **direction** (destination/headsign).
3. Pick the **boarding stop**.
4. Pick one or more **alert stops earlier in the same trip** than the boarding stop.
5. Choose **when** the watch is active:
   - a time window (e.g. weekdays 07:00–08:00), or
   - specific scheduled departures (e.g. 07:16, 07:46), or
   - always.
6. Choose **trigger style**:
   - when the bus reaches/passes a selected alert stop,
   - N minutes before it reaches the boarding stop,
   - N stops before the boarding stop,
   - or a combination.
7. The app schedules the watch window, starts a foreground service, and polls
   live data.
8. When a matching trip triggers, the phone shows a high-priority notification
   with route, vehicle, ETA, and boarding stop.
9. Alert history and simple statistics are available later.

### 3.3 MVP (must be finished by 15 Dec)

- GTFS download, cache, and parse.
- Live vehicle polling with gzip, timeouts, backoff, and stale-data handling.
- Route → direction → stop browser.
- Create/edit/delete watch rules (boarding + multiple alert stops + schedule).
- Trip matching and ETA calculation.
- Android foreground service that keeps watching while the app is backgrounded.
- Android notification channels + local notifications.
- AlarmManager-based start/stop of watch windows (best effort).
- Persist watches, alert history, and settings.
- 5 screens, theme switch, settings screen, validation, empty/loading/error states.
- README + 2–3 page documentation with screenshots.
- 5 LINQ queries incl. `GroupBy` and aggregate.
- Optional module: external API (core) + CSV/JSON export.

### 3.4 Explicitly out of scope for the first version

- Live map rendering (the official site already has a map).
- User accounts/authentication.
- iOS/Windows targets (Android is the chosen target; MAUI remains cross-platform
  in structure, but only Android is tested).
- Multi-user server, chat, social features.
- HTML scraping of the map/timetable pages.
- Machine learning.
- Server-side push (FCM) backend. This is the robust long-term solution, but it
  is too much for this deadline.

### 3.5 Stretch ideas (only if the MVP is done)

- Server-side evaluator + FCM push (best battery/reliability, but a second system).
- Map view with `WebView` + Leaflet.
- Punctuality history per route/trip.
- Export/import of watch rules.
- Romanian/Hungarian/English UI.
- Home-screen widget showing the next bus.

---

## 4. Platform and environment

### 4.1 Why MAUI Android

- The entire goal is a notification on the phone; a desktop app would miss the
  real use case.
- MAUI gives native Android notifications and a foreground service while keeping
  the UI in C#/XAML and the course's MVVM requirement.
- The course's MonkeyFinder lab is a MAUI reference, so the patterns are familiar.

### 4.2 Tradeoff to accept

Android does not allow reliable 5-second background polling without a visible
foreground service. The app will show a persistent "BusPing is watching line 1"
notification while a watch is active. That is the price of reliable alerts.
WorkManager is not suitable: its minimum periodic interval is 15 minutes.
A server + FCM backend is the only way to avoid the persistent notification;
that is out of scope for this deadline.

### 4.3 Local toolchain status

Checked on this machine:

- .NET SDK **10.0.401** installed.
- Workloads: `android`, `ios`, `maccatalyst`, `maui-windows`.
- **Missing: `maui-android`.** Install it before creating the project:
  `dotnet workload install maui-android`.
- Android SDK at `C:\Program Files (x86)\Android\android-sdk` with platforms
  35/36 and build-tools 36.0.0; platform-tools present.
- `adb.exe` exists but is not on `PATH`.
- **No JDK found** in the common locations; install Microsoft OpenJDK 17 and set
  `JAVA_HOME`.
- **No Android emulator installed.** Use a physical phone over USB debugging,
  or install an emulator + system image. A physical device is strongly recommended
  for testing foreground-service behavior.

Setup checklist:

```powershell
dotnet workload install maui-android
$env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'
$env:JAVA_HOME = 'C:\Program Files\Microsoft\jdk-17...'   # after installing
$env:PATH += ";$env:ANDROID_HOME\platform-tools"
& "$env:ANDROID_HOME\cmdline-tools\latest\bin\sdkmanager.bat" --licenses
adb devices
```

Then verify with a throwaway template:

```powershell
dotnet new maui -n MauiSmokeTest
cd MauiSmokeTest
dotnet build -f net10.0-android
```

---

## 5. Architecture (MAUI/MVVM)

```
BusPing/
  App.xaml / App.xaml.cs
  AppShell.xaml / AppShell.xaml.cs        Shell navigation
  MauiProgram.cs                          DI composition root
  Models/                                 GTFS + user entities
  Services/
    Interfaces/                           IGtfsService, ILiveVehicleService, ...
    GtfsService.cs                        download/cache/parse GTFS
    LiveVehicleService.cs                 poll journeys.json, gzip, backoff
    WatchService.cs                       CRUD for watch rules
    EtaService.cs                         trip matching + ETA (domain logic)
    AlertEngine.cs                        evaluate rules, dedupe, fire
    SettingsService.cs                    Preferences + JSON
    JsonStore.cs                          atomic JSON read/write
    NotificationService.cs                abstraction over Android notifications
  ViewModels/
    DashboardViewModel.cs
    RoutesViewModel.cs
    WatchEditorViewModel.cs
    WatchesViewModel.cs
    AlertsViewModel.cs
    SettingsViewModel.cs
  Views/
    DashboardPage.xaml
    RoutesPage.xaml
    WatchEditorPage.xaml
    WatchesPage.xaml
    AlertsPage.xaml
    SettingsPage.xaml
  Platforms/Android/
    MainActivity.cs
    MainApplication.cs
    AndroidManifest.xml
    Services/BusWatchForegroundService.cs
    Services/WatchWindowReceiver.cs
    Services/BootReceiver.cs
    Notifications/AndroidNotificationService.cs
    Resources/drawable/ic_bus_notification.xml
  Resources/
    Styles/Colors.xaml, Styles.xaml
    Images/
```

Rules:

- `CommunityToolkit.Mvvm` for `ObservableObject` and `RelayCommand`.
- View code-behind contains only `InitializeComponent()` and truly view-only
  behavior. Everything else is bindings + commands.
- Services are interfaces, registered in `MauiProgram`, injected into ViewModels
  and the Android service.
- Polling runs off the UI thread with a `CancellationToken`; UI updates go
  through `MainThread.BeginInvokeOnMainThread`.
- All network/file operations are `async` with timeouts and `try/catch`.
  No `.Result` / `.Wait()`.

### 5.1 Navigation

Use `Shell` with a bottom `TabBar`:

- Dashboard
- Routes
- Watches
- Alerts
- Settings

Watch creation/editing is a modal page pushed with:
`Shell.Current.GoToAsync(nameof(WatchEditorPage))`.
This satisfies "4+ screens with real navigation" and is natural on Android.

---

## 6. Data model

### 6.1 GTFS (read-only, cached)

- `Route` — id, short name, long name, color.
- `Trip` — id, route, service, headsign, direction, shape.
- `Stop` — id, name, lat/lon.
- `StopTime` — trip, stop, sequence, arrival, departure.
- `ShapePoint` — shape id, sequence, lat/lon (optional; can stay in memory).
- `Calendar` / `CalendarDate` — service availability.

### 6.2 User data (persisted)

- `WatchRule` (main CRUD entity):
  - `Id`, `Name`, `RouteId`, `DirectionId`, `BoardingStopId`
  - `Enabled`
  - schedule: weekdays bitmask / date range / time window, or explicit trip starts
  - trigger: minutes-before, stops-before, at-stop flags
  - `LeadMinutes`, `CooldownMinutes`
- `WatchAlertStop`:
  - `WatchRuleId`, `StopId`, `StopSequence`, `TriggerMode`
  - relationship: WatchRule (1) — (N) WatchAlertStop (N) — (1) Stop
- `AlertEvent`:
  - `WatchRuleId`, `TripId`, `ServiceDate`, `TriggerStopId`, `FiredAt`,
    `VehicleId`, `EtaMinutes`, `Message`
- `AppSettings`:
  - theme, language, poll interval, default lead time, notification sound,
    confirm-before-delete, battery-optimization prompt shown, last GTFS refresh.

Relationships visible in UI:

- A watch shows its route, boarding stop, and ordered alert stops.
- A route/stop detail shows which watches use it.
- Alert history groups by route/day.

### 6.3 Persistence

- JSON files under `FileSystem.AppDataDirectory` (`watches.json`,
  `alert-history.json`, `gtfs-meta.json`), written atomically
  (temp file + replace).
- `Preferences` for simple settings (theme, poll interval, lead time, sound,
  language).
- GTFS cached as files under `FileSystem.CacheDirectory/gtfs/`.
- This satisfies B1/E2 and avoids EF Core migration overhead. If there is time,
  SQLite + EF Core can replace the JSON repository behind `IWatchRepository`.

---

## 7. The domain logic: trip matching + ETA + alerting

This is the `C4` "own domain logic" item. It is also what makes the app more than
a CRUD template.

### 7.1 Trip matching

1. If the live vehicle has a `trip_id` and it exists in GTFS and matches the
   watched route, use it directly.
2. If it does not (the common case: `assignment_status: unverified` with an
   internal trip id such as `2217`, no headsign and no direction), match it
   geometrically:
   - project the GPS fix onto every shape of the route;
   - keep the shape within 200 m;
   - compare the vehicle's bearing with the shape segment's bearing to pick the
     direction (the two directions share the same street);
   - pick a representative trip that runs on the current service day.
3. When matched this way, timetable times belong to a different run, so the ETA
   uses distance only (no schedule blending), and the alert is deduplicated per
   vehicle, not only per trip.
4. If no shape is close, the vehicle is ignored rather than guessed.

### 7.2 ETA to a stop

For the matched trip:

- Get the ordered `StopTime` list.
- Locate the vehicle's next stop (nearest upcoming stop, or the live
  `schedule.stop_id` when available).
- Compute remaining distance:
  - `dist(vehicle, nextStop) + sum(dist(stop[i], stop[i+1]))` along the trip;
  - optionally project onto the GTFS shape for better accuracy.
- Estimate travel time:
  - `distance / effectiveSpeed` where effective speed blends live speed with a
    conservative city average (clamp roughly 12–40 km/h);
  - add dwell time (~15–25 s) per intermediate stop.
- Blend with the schedule:
  - `scheduled + reported_delay_seconds - now` when the operator prediction is
    fresh;
  - weight distance-based and schedule-based estimates by confidence.
- Return `{ Minutes, StopsAway, IsAtStop, Confidence }`.

### 7.3 Triggering

A rule fires when **all** of these hold:

- the watch is enabled and inside its schedule window;
- a matching vehicle/trip is found;
- at least one trigger condition is true:
  - **AtStop**: vehicle is within ~70 m of the alert stop, or its next stop is
    the alert stop, or it passed the stop less than ~250 m ago (covers a polling
    gap without waking you for a bus that left minutes ago);
  - **MinutesBefore**: ETA to the boarding stop ≤ `LeadMinutes`;
  - **StopsBefore**: stops away to the boarding stop ≤ N.
- the same `(watch, trip instance, trigger stop, trigger type)` has not already
  fired (dedupe). `trip instance = trip_id + service date`; geometrically
  matched vehicles are keyed by vehicle id too.
- the per-stop cooldown has elapsed. It is deliberately **not** per watch,
  otherwise the second and third warning stops of the same trip would be
  suppressed.

On fire: persist `AlertEvent`, show the notification, and keep the watch armed
for other alert stops if configured.

### 7.4 Pseudocode

```csharp
foreach (var watch in enabledWatches)
{
    if (!watch.IsActiveNow(now)) continue;

    var vehicle = await matcher.FindVehicleAsync(watch, vehicles, now);
    if (vehicle is null) continue;

    var eta = etaService.Compute(vehicle, watch, now);

    foreach (var trigger in watch.Triggers)
    {
        if (!trigger.IsSatisfied(eta, vehicle, now)) continue;
        if (history.AlreadyFired(watch.Id, vehicle.TripInstance, trigger)) continue;
        if (history.InCooldown(watch.Id, now)) continue;

        await notifier.NotifyAsync(watch, vehicle, eta, trigger);
        await history.AddAsync(AlertEvent.From(watch, vehicle, eta, trigger));
    }
}
```

---

## 8. Android background execution and notifications

This is the highest-risk part of the project. The full design is in
[`docs/ANDROID-BACKGROUND.md`](ANDROID-BACKGROUND.md); the essentials:

- **Foreground service** (`dataSync`) with a persistent low-importance
  notification while a watch is active.
- **Notification channel** `bus_alerts` at high importance for actual alerts.
- **Runtime permission** `POST_NOTIFICATIONS` on Android 13+.
- **AlarmManager** schedules watch-window start/end; exact alarms if the user
  grants "Alarms & reminders", otherwise inexact with a warning.
- **Adaptive polling**: ~10 s normally, ~5 s in the last 15 minutes before the
  boarding stop, ~30 s outside the active window; exponential backoff on errors.
- **gzip** on `journeys.json`; only poll while a watch is active.
- **Battery optimization** prompt for OEMs that kill background services.
- **Known limits**: force-stopped apps do not restart until reopened; Android 15
  limits `dataSync` foreground services to 6 h per 24 h (our windows are short);
  some OEMs still kill services despite everything.

---

## 9. Screens (mobile)

1. **Dashboard**
   - Active watches, next bus per watch, live vehicle count, connection status.
   - "Start watching" / "Stop watching" with foreground-service state.
   - Permission warnings (notifications, battery optimization).
   - Empty state: "No watches yet — add your first route."

2. **Routes**
   - Search/filter routes by number or stop name.
   - Direction tabs.
   - Horizontal stop timeline for the selected trip/direction.
   - Select boarding stop; only earlier stops are selectable as alert stops.
   - Shows repeated stops as separate sequence entries.

3. **Watches**
   - List of watch rules with route, boarding stop, alert stops, schedule.
   - Create/edit/delete (delete asks for confirmation).
   - Enable/disable toggle.
   - Shows whether the background service is currently running.

4. **Alerts / History**
   - Chronological alert history with filters (route, date, watch).
   - Statistics: alerts per route (`GroupBy` + `Count`), alerts per day,
     average lead time (`Average`), total alerts (`Count`).
   - Simple bar/column chart (can be hand-drawn or a lightweight chart control).

5. **Settings**
   - Theme (light/dark/system).
   - Language (if multilingual module is added).
   - Poll interval (5/10/15 s).
   - Default lead time.
   - Notification sound on/off.
   - Confirm before delete.
   - Battery optimization help / open system settings.
   - Refresh GTFS / clear cache.
   - About + data source attribution.

---

## 10. Assignment compliance matrix

| Item | How the app satisfies it |
|---|---|
| A1 Layering | `Models`, `Services`, `ViewModels`, `Views`, `Platforms`, `Resources` folders and namespaces. |
| A2 MVVM | XAML bindings + `CommunityToolkit.Mvvm`; code-behind stays near-empty. |
| A3 DI behind interfaces | `IGtfsService`, `ILiveVehicleService`, `IWatchRepository`, `IEtaService` registered in `MauiProgram` and injected. |
| A4 Code quality | Async/await, cancellation, timeouts, retries, no duplicated logic. |
| B1 Persistence | JSON files in `FileSystem.AppDataDirectory` + `Preferences`; survives restart. |
| B2 CRUD | Create/read/update/delete `WatchRule`; delete confirmation; validation. |
| B3 5 LINQ queries | Search, ordering, `GroupBy` route/day, `Count`, `Average`, top stops. |
| C1 Two related entities | `WatchRule` ↔ `WatchAlertStop` ↔ `Stop`; `WatchRule` → `AlertEvent`; visible in UI. |
| C2 4+ screens | Dashboard, Routes, Watches, Alerts, Settings via Shell. |
| C3 Validation | Required route/stop, alert stops must precede boarding stop, lead time range, schedule required. |
| C4 Domain logic | Trip matching + ETA + alert scheduling (the core calculation). |
| D1 Visual identity | Shared color/spacing/typography resources; route colors from GTFS. |
| D2 Theme | `AppThemeBinding` + `Application.Current.UserAppTheme` at runtime. |
| D3 States | Empty, loading, error + retry on GTFS/live data. |
| D4 Responsive | Grid with star sizing, `CollectionView`, portrait/landscape tested. |
| E1 Settings | Theme, poll interval, lead time, sound, confirm-delete, language. |
| E2 Persisted settings | `Preferences` loaded at startup. |
| F1 README | What it does, prerequisites, `dotnet build/run`, device setup, data location. |
| F2 2–3 page doc | Description, topic rationale, features by rubric block, tech, data model, screenshots, limitations. |
| Optional (1 pt) | External API (0.75) + CSV/JSON export (0.5) = capped 1.0. |

---

## 11. Milestones

Today: **23 November 2026**. Defense: **15 December 2026**.

| Dates | Deliverable |
|---|---|
| Nov 23–24 | Finalize topic proposal; install `maui-android` + JDK 17; build/run a smoke-test app on a phone; repo skeleton, models, interfaces. |
| Nov 25–29 | GTFS download/parse/cache; route/direction/stop browser; live polling with gzip/backoff. |
| Nov 30–Dec 1 | Watch CRUD + JSON/Preferences persistence; half-state demo on Dec 1. |
| Dec 2–6 | ETA engine, trip matching, foreground service, notification channels, AlarmManager windows. |
| Dec 7–10 | Dashboard, alert history, statistics, theme, settings, validation. |
| Dec 11–13 | Polish: empty/loading/error states, portrait/landscape, battery/permission flows, README, docs, screenshots. |
| Dec 14 | Fresh-clone build, APK, final review. |
| Dec 15 | Defense/demo. |

If time slips, cut in this order: export, statistics chart, multilingual, map.
Never cut: persistence, CRUD, 5 LINQ queries, theme, settings, 4+ screens,
validation, a working ETA/notification demo.

---

## 12. Risks and mitigations

| Risk | Mitigation |
|---|---|
| `maui-android` workload/JDK not installed | Install on day 1; verify with a smoke-test app before writing real code. |
| No emulator | Use a physical Android phone; `adb` is already present in the SDK. |
| Background service killed by OEM | Foreground service + battery-optimization prompt + test on the target phone. |
| Android 13 notification permission denied | Request at the right moment; show in-app warning and a settings shortcut. |
| Android 12+ background-start restrictions | Start the service from the app; use `AlarmManager` exact alarms for windows where permitted. |
| Android 15 `dataSync` 6 h/24 h limit | Only run during short watch windows; stop the service when the window ends. |
| Data/battery usage | gzip, adaptive polling, only poll during active windows, stop after the trip. |
| Live vehicles missing GPS / `trip_id` null | Show "unavailable"; fall back to schedule-only; never crash. |
| Route alias mismatches (`2B` vs `202`) | Central alias map + tests. |
| Loop routes / repeated stop names | Use trip-specific `stop_id` + sequence, never names. |
| Time zone / after-midnight trips | Store UTC, convert to Europe/Bucharest, support `>24:00` times. |
| API changes or downtime | Cache last good data; backoff; clear error state; retry button. |
| Scope creep | Map, auth, FCM are stretch; MVP rubric first. |
| Secrets in source | No API keys. If an export/share integration is added, keep tokens out of git. |
| Unofficial API / rate limits | Poll at 5–15 s, identify the app in User-Agent, cache, backoff. |

---

## 13. Open decisions

1. **Physical phone or emulator?** Physical phone is recommended for reliable
   foreground-service and notification testing.
2. **Scheduled start**: exact `AlarmManager` alarms for watch windows (extra
   permission + testing) or "open the app and tap Start watching" for the MVP?
   Recommendation: implement manual start first, then add exact alarms.
3. **Minimum Android version**: API 26 (Android 8) is a practical floor for
   notification channels and foreground services.
4. **Optional module**: external API + export (recommended), or external API +
   multilingual, or unit tests.
5. **Language(s)**: Romanian + Hungarian is natural for Târgu Mureș; English is
   optional.

---

## 14. Implementation status (26 Nov 2026)

Working and building (`net10.0-android`):

- 5 tabs via Shell + DI-resolved pages, CommunityToolkit.Mvvm view models.
- GTFS download/cache/parse (`GtfsParser`), live polling with gzip, timeouts and
  a last-known-good cache (`LiveFeedParser`).
- Watch editor: line → direction → stop timeline, boarding stop, warning stops
  with "must be before boarding" validation, days/window/lead settings.
- Incoming-run warnings: when boarding within the first three stops, the last
  three stops of the opposite run (the bus that becomes yours at the terminus)
  can be selected as warnings too.
- Watch CRUD + enable/disable + delete confirmation, JSON persistence.
- `EtaService`: distance along remaining stop sequence, clamped effective speed,
  dwell time, blending with the operator's reported delay.
- `AlertEngine`: route alias matching, trip/service-day validation, dedupe per
  trip instance and vehicle, cooldown, stale-GPS guard.
- Geometric fallback matching for the majority of vehicles the feed publishes as
  `unverified` (shape projection + bearing), so they still trigger alerts.
- Android foreground service (`dataSync`) + two notification channels; start/stop
  from the dashboard.
- Settings (theme applied at runtime, poll interval, lead time, sound,
  confirm-delete) persisted in `Preferences`.
- Alert history with `GroupBy` + `Average` statistics.
- `tools/DataCheck` verifies the whole data pipeline against the live feeds,
  and runs the alert engine against a synthetic trip (fires once, dedupes,
  persists history).

Verified on a physical Android 14 device (POCO/Redmi): dashboard loads 35 lines
and 100+ live vehicles, the `dataSync` foreground service runs with its ongoing
notification and ticks every 10 s, and `watches.json` survives restarts.

Next, in priority order:

1. Dashboard "next bus per watch" countdown + in-app alert banner.
2. Exact `AlarmManager` scheduling so watching starts before the configured
   window; permission and battery-optimization guidance screens.
3. Optional module: CSV/JSON export of watches and history.
4. Unit tests for ETA blending, alias matching and dedupe.
5. Screenshots + the 2–3 page written documentation.

