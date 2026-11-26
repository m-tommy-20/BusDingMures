# TÉMAJAVASLAT

**Név:** ........................................
**Csoport:** ........................................

## 1. Az alkalmazás neve és egy mondatos leírása

**BusPing** — személyre szabott buszfigyelő és értesítő alkalmazás a
marosvásárhelyi helyi járatokhoz, amely a valós idejű GPS-adatok és a GTFS
menetrend alapján szól, amikor a kiválasztott járat a felszállási megállóhoz
közeledik, illetve eléri a korábban kijelölt megállókat.

## 2. Platform és architektúra

- [ ] WPF (MVVM)
- [x] **.NET MAUI (MVVM) — Android**
- [ ] Blazor Hybrid (MVVM elve)
- [ ] ASP.NET Core MVC (MVC)

Indoklás: az alkalmazás lényege, hogy a figyelmeztetés a telefonra érkezzen,
ezért natív Android-értesítésre és háttérben futó figyelésre van szükség. A MAUI
MVVM-mintát és a Shell-navigációt használja; a háttérfigyelést Android
foreground service végzi, folyamatos, csendes értesítéssel.

## 3. Adattárolás

- [x] **JSON fájl** (`FileSystem.AppDataDirectory`: `watches.json`, `alert-history.json`)
- [ ] SQLite
- [ ] MSSQL
- [ ] MongoDB
- [ ] egyéb: **Preferences** (téma, lekérdezési gyakoriság, alapértesítési idő)

A GTFS-adatok külön helyi gyorsítótárba kerülnek. A felhasználói adatok írása
atomi módon történik (ideiglenes fájl + csere), így áramszünetnél sem sérülnek.
A beállítások MAUI `Preferences`-ben tárolódnak, így újraindítás után is megmaradnak.

## 4. Entitások és kapcsolataik

Felhasználói entitások:

- **WatchRule** (1) — (N) **WatchAlertStop** (N) — (1) **Stop**
- **WatchRule** (N) — (1) **Route**
- **WatchRule** (1) — (N) **AlertEvent**

GTFS-entitások (csak olvasható, gyorsítótárazott):

- **Route** (1) — (N) **Trip**
- **Trip** (1) — (N) **StopTime**
- **Stop** (1) — (N) **StopTime**

A `WatchRule` a fő CRUD-entitás: járat, irány, felszállási megálló, riasztási
megállók, aktív időablak, trigger-típus és előzetes értesítési idő.

Hurokjáratoknál ugyanaz a megállónév többször szerepelhet; ezért a megállókat
`stop_id` + a tripbeli sorozatszám azonosítja, nem a név.

## 5. Képernyők (legalább négy)

A navigáció MAUI `Shell`-lal, alsó tabokkal történik.

1. **Dashboard** — aktív figyelők, következő járatok, kapcsolat állapota,
   háttérfigyelés indítása/leállítása, hiányzó engedélyek figyelmeztetése.
2. **Járatok / megállók** — járat keresése, irány kiválasztása, megállók
   idővonala, felszállási és riasztási megállók kijelölése.
3. **Figyelők** — figyelőszabályok listázása, létrehozása, módosítása,
   törlése megerősítéssel, ki-/bekapcsolása.
4. **Riasztások / előzmények** — kiváltott értesítések, szűrés, statisztika.
5. **Beállítások** — téma, nyelv, lekérdezési gyakoriság, alapértelmezett
   előzetes értesítés, hang, törlés megerősítése, akkumulátor-optimalizálás
   súgó, GTFS-frissítés.

## 6. A saját logika — mit számol ki az alkalmazás?

> Az alkalmazásom a kiválasztott járat valós idejű GPS-pozíciója, a GTFS
> megállósorrendje és a menetrend alapján kiszámolja, hogy a járat hány perc
> múlva ér a felszállási megállóhoz, illetve melyik korábbi megállót érte már el,
> és ez alapján a beállított megállóknál vagy X perccel a felszállás előtt
> értesítést küld.

Részlépések: járat–menetrend párosítás, megálló-távolság számítása, menetidő
becslése, késés figyelembevétele, riasztás kioldása és duplikációszűrés.

## 7. Az öt LINQ-lekérdezés, amit tervezek

1. Járatkeresés szám vagy megállónév alapján:
   `routes.Where(r => r.ShortName.Contains(q) || r.Stops.Any(s => s.Name.Contains(q)))`
2. Egy trip megállóinak sorrendezése:
   `stopTimes.Where(st => st.TripId == id).OrderBy(st => st.Sequence)`
3. Figyelők csoportosítása járat szerint:
   `watches.GroupBy(w => w.RouteId).Select(g => new { Route = g.Key, Count = g.Count(), Active = g.Count(w => w.Enabled) })` — **GroupBy + Count**
4. Riasztások napi bontásban:
   `alerts.GroupBy(a => a.FiredAt.Date).Select(g => new { Day = g.Key, Count = g.Count() })` — **GroupBy + Count**
5. Átlagos ETA / összes riasztás:
   `alerts.Average(a => a.EtaMinutes)` és `alerts.Count()` — **aggregáció**

(Az ötödik mellett a statisztika képernyőn megjelenik egy „top megállók”
lekérdezés is.)

## 8. Választható modul, amit tervezek

- **Külső API használata (0,75 pont):** a `journeys.json` és a `gtfs.zip`
  valódi HTTP-hívása, hibakezeléssel, timeouttal és betöltési állapottal.
- **Exportálás (0,5 pont):** figyelők és riasztási előzmények mentése CSV/JSON
  fájlba. A kettő együtt a maximális 1 pontot adja.

## 9. Ami bizonytalan, vagy amiben segítség kell

- A háttérfigyelés megbízhatósága egyes gyártók (Xiaomi, Samsung, Huawei)
  agresszív akkumulátor-kezelése miatt; a felhasználónak whitelistelnie kell az
  alkalmazást.
- Az Android 12+ pontos ébresztés (exact alarm) engedélye; ha nem kapható meg,
  pontatlan ébresztéssel indul a figyelés, és figyelmeztetjük a felhasználót.
- Fizikai telefon vagy emulátor használata a teszteléshez; a foreground service
  viselkedését valódi eszközön kell ellenőrizni.
- Android 13+ értesítési engedély megtagadása esetén a figyelmeztetés nem
  jelenik meg; ilyenkor in-app figyelmeztetést és beállítás-megnyitót adunk.
- Az élő adatok hiányosságai: néhány járműnek nincs `trip_id`-ja vagy GPS-je;
  ilyenkor menetrend-alapú becslésre váltunk.
