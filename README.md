# BusPing

Android app (.NET MAUI, MVVM) that watches Târgu Mureș local buses and sends a
notification **before** the bus reaches your stop.

You pick a line, a direction, your boarding stop and one or more stops earlier in
the trip. When you board at (or near) the start of the line, the three stops
before the terminus of the opposite run can be selected too — that is the bus
that will arrive and then become yours. BusPing downloads the operator's GTFS
schedule, polls the live GPS feed every few seconds, computes an ETA along the
remaining stop sequence, and fires a high-priority notification when the bus
reaches a selected stop or gets close to yours.

## Screens

| Tab | What it does |
|---|---|
| **Dashboard** | Schedule/live status, vehicle count, your watches, start/stop background watching |
| **Routes** | Search lines, switch direction, inspect the stop timeline |
| **Watches** | List, create, edit, delete and enable/disable watch rules |
| **Alerts** | Fired alerts with filters/summary: per-day counts, average ETA |
| **Settings** | Theme, poll interval, default lead time, sound, delete confirmation, GTFS refresh |

## Data sources

| Purpose | URL |
|---|---|
| Static schedule (GTFS zip) | `https://live.transportlocal.ro/gtfs.zip` |
| Live vehicles (JSON, polled) | `https://live.transportlocal.ro/journeys.json` |

The live feed has no per-stop ETA beyond the *next* stop, so BusPing computes the
rest itself from the vehicle position, the GTFS stop sequence and the reported
delay. Live data is cached locally, so a short network outage shows the last
known positions instead of an empty screen.

## Build and run

Prerequisites:

- .NET SDK 10 with `android` + MAUI workloads
- Android SDK (platform 35/36, build-tools)
- A JDK 17+ (`JAVA_HOME`), e.g. the one bundled with Visual Studio at
  `C:\Program Files\Android\openjdk\jdk-21.0.8`
- A physical Android phone (recommended) with USB debugging, or an emulator

```powershell
$env:JAVA_HOME = 'C:\Program Files\Android\openjdk\jdk-21.0.8'
$env:ANDROID_HOME = 'C:\Program Files (x86)\Android\android-sdk'

dotnet build BusPing\BusPing.csproj -f net10.0-android
# install on the connected device:
dotnet build BusPing\BusPing.csproj -f net10.0-android -t:Run
# or adb install -r BusPing\bin\Debug\net10.0-android\ro.busping.app-Signed.apk
```

Debug builds embed the assemblies (`EmbedAssembliesIntoApk`), so the produced
APK can be copied to the phone and installed on its own. Without that, a
sideloaded debug APK aborts with "No assemblies found".

To build a versioned APK (and optionally push it to a connected phone):

```powershell
pwsh tools\publish-apk.ps1 -Version v6 -Push
# -> artifacts\BusPing-v6.apk -> /sdcard/Download/BusPing-v6.apk
```

Android 8+ (API 26). Grant notification permission when starting the watcher;
Android also shows a persistent "BusPing is watching" notification while the
foreground service runs — that is required by the platform.

## Verifying the data pipeline without a device

```powershell
dotnet run --project tools\DataCheck
```

Downloads the live GTFS and vehicle feed, then checks CSV parsing, GTFS counts,
live vehicle parsing, route aliases and an end-to-end ETA calculation. It also
runs the alert engine against a synthetic trip to prove that an alert fires once,
is persisted, and is not repeated.

## Project layout

```
BusPing/
  Models/            GTFS, live-feed and user entities
  Services/          data feeds, ETA engine, alert engine, JSON storage
    Interfaces/      service contracts used for DI
  ViewModels/        MVVM view models (CommunityToolkit.Mvvm)
  Views/             XAML pages
  Platforms/Android/ foreground service, notification service, manifest
tools/DataCheck/     console harness for the pure data code
docs/                plan, Android background design, topic proposal
```

User data lives in the app's private data directory:
`watches.json`, `alert-history.json`; settings live in `Preferences`; the GTFS
cache is in the app cache directory.

## Known limitations

- The watcher must be started from the app; exact-time window scheduling
  (`AlarmManager`) is the next step. A force-stopped app cannot alert until it is
  opened again.
- Aggressive OEM battery managers (Xiaomi, Samsung, Huawei) may still kill the
  service; allow BusPing to run in the background.
- Android 15 limits `dataSync` foreground services to 6 hours per 24 hours.
- Positions reported with `position_source: legacy` are not real GPS and are not
  used for triggering.
- No iOS/Windows targets: the goal is a phone notification on Android.
