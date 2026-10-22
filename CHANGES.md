# Mission Planner - Titan Dynamics Edition - Features

What this fork adds on top of upstream [ArduPilot/MissionPlanner](https://github.com/ArduPilot/MissionPlanner), by area. It describes how the fork behaves today, not the history of how it got there; for that, use the git log. (`ChangeLog.txt` is upstream's own log.)

---

# Branding & Theming

## Titan Dynamics System Theme
A new bundled theme (`titandynamics.mpsystheme`) is set as the default for new installs. Theming also covers controls upstream leaves unthemed — `ListBox`, `PropertyGrid`, `MessagesList`, status tab, menu-bar icons, the Splash form, and toast notifications.

## Dark Windows Title Bars
Native Windows title bars now follow the application theme rather than always being light.

## New Application Icon, Splash, and Tab Icons
A new app icon (Titan Dynamics branding), redesigned splash screen, and a fresh icon set for the main top-level tabs in both dark and light variants.

## Plane & Copter Default Icons
Tornado and Proton aircraft are the default rendered icons for plane and copter vehicle classes. Users can also drop in a custom aircraft icon image to override per-vehicle defaults entirely.

---

# HUD Overhaul

## Layout & Typography
Major reflow of the HUD. Larger, optimized fonts; centering uses cached per-character metrics for stability; left/right scroller arrows scale with font size; mode display has a redrawn trapezoid background; heading scroller shows an `H` indicator for home heading; a degree symbol (`°`) instead of the literal `deg`; `Yaw` is labelled `Heading`.

## New Telemetry Readouts
- **AOA / SSA / critical AOA gauge** for fixed-wing
- **Low airspeed warning** (red) and **rate-of-change of airspeed** (blue bar)
- **Low ground speed warning**
- **Dual GPS** support on the top bar
- **EKF / VIBE / GPS1 / GPS2** indicators repositioned to the right-most edge
- **ESC telemetry** in tlog graphs
- **Dual battery** display in the bottom-left

## Plugin-Overridable Icon Slots
Plugins can override the HUD's icon slots for custom telemetry and iconography.

## Pre-Arm & Flight Mode Rendering
Cleaner, more reliable display of pre-arm status and the active flight mode on the HUD.

## Leaner HUD
No Russian-localized HUD variant and no time readout; the CPU message and link stats sit out of the way of the flight instruments.

---

# 3D Map (Map3D)

## Real-Time Vehicle Rendering
The connected vehicle renders in 3D with default STL models per MAV type (plane, quadplane, copter, rover, boat, sub, tricopter, etc.) in correct colours, over terrain drawn out to just past the horizon (30–150 km depending on altitude). The model is uploaded to the GPU once and lit in a shader.

## Terrain Streaming Engine
Terrain streams Google Earth style:
- **Screen-space level of detail.** A tile quadtree is refined until each imagery texel is about a pixel on screen, up to zoom 20, so detail follows what the camera actually sees; there is no zoom setting to manage.
- **Coarse first, centre outwards.** Load requests are ranked by distance relative to tile size and by angle from the view centre, re-ranked every frame, and dropped when no longer wanted. Tiles out of view are prefetched coarser so turning never shows holes.
- **No holes or overlaps.** A tile is replaced by its children only once all of them are loaded; skirts hide cracks between levels.
- **Off the UI thread.** Eight workers fetch imagery, sample elevation once per tile and build meshes; the render thread only uploads finished tiles within a per-frame time budget, with BGRA uploads, mipmaps and anisotropic filtering. A bounded GPU cache keeps recently seen terrain.
- **Shared imagery cache.** Imagery goes through the 2D map's tile cache, so tiles seen on the 2D map and imported `.mappack` files load instantly and offline mode works.
- **Elevation that always arrives.** Missing SRTM cells are downloaded instead of leaving gaps; tiles start from their parent's heights and are re-meshed when the data lands.
- **No reloads while flying.** Tiles keep their own UTM origins, so moving the scene origin never discards loaded terrain.
- **Light when hidden, quick to show.** Hiding the 3D map frees its terrain and elevation data so RAM drops back, while the view itself stays ready, so showing it again is near-instant. Elevation lookups never block the UI, and the vehicle model loads in the background.
- **Idle rendering.** Full frame rate only while something changes (connected vehicle, terrain streaming, markers, ADS-B, input, the 2D map moving, or the stats panel open); otherwise one frame a second.
- **Stats for nerds** (3D map Settings, off by default): a colour-coded panel with FPS and a frame-time graph, terrain streaming internals (tiles, per-zoom histogram, GPU memory, queue, load rate, failures, elevation status), the camera view and GPU info.

## Camera & Visual Effects
- **FPV mode** with smooth camera movement and pitch-matched heading lines
- **Orbital camera** mode
- **Distance haze** that fades terrain into the sky's horizon colour before the edge of the loaded terrain, at every altitude
- **Sky gradient** that follows the true horizon through pitch and roll
- **Heading indicators** and dynamic render distance
- **Trailing flight path** with Kalman-smoothed lines on all axes; render gated on stable telemetry
- **High-altitude rendering** improvements

## ADS-B in 3D
ADS-B traffic is rendered with proper depth testing and pass ordering so it correctly occludes against terrain and the vehicle.

## Persistence & UX
- Camera distance, height, and angle saved on close
- Settings dialog overhaul with persistence and out-of-range guards, opened from a translucent gear icon in the top-left corner
- Configurable marker size
- Reset button defaults aligned with settings defaults
- 2D ↔ 3D map waypoint and position synchronization
- Required-valid-GPS gate before vehicle tracking begins
- 3D Map cleanly stops on disconnect and tears down before app close

## GPU Acceleration
GPU acceleration is enabled application-wide, with a registry-level Windows GPU-preference hint to prefer the discrete GPU on hybrid systems.

---

# Tab & Layout Restructuring

## New Themed Tab Strip
Replaced the dated FlightData tab bar with a modern themed strip and refreshed icons.

## New / Re-purposed Tabs
- **Params tab** in FlightData and a Params panel co-located on the map
- **Tuning tab** in FlightData (vehicle-aware: plane/copter/rover)
- **Video tab** with USB camera and GStreamer support, an RTSP field that prefills a low-latency GStreamer pipeline, plus immediate HUD-overlay toggle
- **Inspector tab** (MAVLink Inspector exposed without `Ctrl+F`)
- **Logs tab** unifying former Dataflash and Telemetry tabs
- Working **Status tab** in FlightData
- **Help tab** hidden by default; Gauges tab hidden by default

## Splits, Sizing & Defaults
Split percentages on every divider in the main GUI persist across launches, with faster swap and resize behavior. New installs default to maximized window and ~50% HUD width. HUD vertically centers when swapped with the map.

---

# Parameters

## Async Loading
Parameter documentation and ranges (~6000 entries) load asynchronously, eliminating the multi-second freeze on the Full Parameter List screen.

## Search & Autocomplete
Themed search bar with suggest-only autocomplete; search resets when navigating away. No warning dialog or obsolete presets on the param page.

## Other
- No confirmation dialogs after param writes (reboot-required and save-failed are still shown)
- Toast notification on successful param write
- Tree collapsed by default in tab embeddings to save space
- User params can be set on the Flight Modes page

---

# Setup & Configuration Pages

## Betaflight-Style Motor Setup
**Motor Setup** (upstream's "Motor Test") is a Betaflight-style page in the Mandatory Hardware section, shown only for copter/quadplane vehicles, with live frame and class type images. The frame diagrams ship with the app, so they show on air-gapped setups, and are refreshed from ardupilot.org when online.

## Servo Output Tab
A revamped Servo Output panel with click-or-drag sliders for trim, an "Equidistant" min/max helper, and proper min/max-from-trim handling. The trim slider stays in sync with the combobox.

## Radio & Calibration
Visual-stick-controls Radio Calibration UI with upstream's reversing logic; polished accel calibration.

## Other Setup Pages
GUI cleanups across Compass, Failsafe, Flight Modes, Initial Setup Serial; Battery Monitor pages merged into one unified screen; voltage/current calibration enabled regardless of selected sensor.

## Configuration Profile Management
Save and load named configuration profiles for the Mission Planner application itself.

---

# Auto-Connect & Devices

## USB Auto-Connect
A dedicated USB auto-connect path with VID coverage for all common flight controllers (including Cube). Toast notification fires when a device auto-connects. Auto-connect is disabled while flashing firmware to avoid races.

## ELRS
ELRS airport support added.

---

# Mode Selector & Vehicle State Toolbar

## Top-Bar Mode Selector
A compact mode selector lives in the top menu bar. Users pin the modes they want as toolbar buttons (persisted in `ModeSelectorPinned`). The dropdown shows the vehicle's configured `FLTMODE1`–`FLTMODE6` (`MODE1`–`MODE6` on Rover) in a "Configured modes" section on top, then the full "Flight modes" list with favorite stars; it refreshes on connect and whenever a flight-mode parameter is written rather than polling. The selector auto-sizes based on available space and hides when not connected.

## Vehicle State Toolbar
Arm/disarm indicator and a clickable GPS status popup with detailed real-time telemetry, with values that update live.

---

# ADS-B

- Built-in **ADS-B HTTP endpoint**
- Aircraft type & category fetched from the API
- Sprites updated to **tar1090** style
- Distance ranges adjusted to 1, 5, 10 km
- Enhanced tooltip with altitude delta and smart distance formatting
- Speech callouts of proximate aircraft
- Emitter category support; squawk fixes
- Live traffic from **[adsb.fi](https://adsb.fi)** by default, about once a second (personal, non-commercial use); adsb.lol or any other ADSBExchange-style API can be used instead
- **Smooth traffic motion:** aircraft glide along their track between feed updates on the 2D and 3D maps instead of jumping

---

# Dashboard / QuickView

## Editing Workflow
- Drag-and-drop reordering of items
- Right-click → Edit option for individual items
- Overhauled edit dialog with categorization and search
- The Quick tab pops out into its own window

## Display Modes
- **Gauge display mode** for any dashboard item
- 270° gauges with blinking on out-of-range values
- Minimum 25% panel sizes enforced
- Battery `Wh` used and `Wh/km` available as Quick tab metrics

## Defaults & Persistence
Sensible default Quick tab options on first run; reset and reset-all per-item; loiter radius increments by 10 instead of 1.

---

# Mission Planning

## Upstream Mission Features Included
The WPOverlay2 rendering pipeline with its MissionStyleEditor, `MissionSegmentizer` arcs, `GMapRoute` arrow modes, `GMapMarkerWP` marker types with centred text, the plane return-path command, and correct `DO_LAND_START` Z units.

## Fork Additions
- **Center to Vehicle** button
- **Clear all** in PLAN tab; clear-path from FlightData
- 2D ↔ 3D map waypoint sync
- Improved mission table UI
- "Load waypoints on connect" loads them without forcing a switch to the PLAN tab
- No "New Mission Overlay" announcement when the PLAN tab opens; Edit Style and Use Legacy Overlay live in the map menu

---

# Map (2D) UX

- **Double-click "Fly to Here"** for one-click GUIDED-mode entry, with a persistent enable toggle
- **Tools button** on map for the former "temp" / `Ctrl+F` window
- **Propagation Settings** accessible from the map (`Ctrl+W`)
- **Find Location** added to the PLAN tab
- **Offline Maps** button on the PLAN tab (see [Offline Maps](#offline-maps) below)
- Custom SITL locations; Woodley default
- Larger fonts on Sats/HDOP labels with new VDOP readout
- Zoom buttons and scale bar on map
- Clean pixel-perfect bottom-bar layout
- Auto-pan on parameter button swap

---

# Offline Maps

Built for air-gapped use. Opened from the **Offline Maps** button next to Find Location on the PLAN tab.

## The Window
- Lists every map area that has been downloaded into the tile cache: name, map source, zoom range, tiles on disk against tiles expected (with coverage %), and size on disk. Counting runs in the background.
- Selecting an area shows a **preview composited from its own cached tiles** at the highest zoom that fits a 4×4 tile grid, cropped to the area. Tiles missing from disk appear hatched, so coverage gaps are visible at a glance. Below the preview: size in km, tile counts, a coverage bar, size on disk, date added and where the area came from.
- Modeless, so the map stays usable: pan, or hold ALT and drag to select an exact area, then click **Download…**.
- All actions on one row: Download…, Show on Map, Rename, Export…, Import…, Delete (optionally deleting the tiles too), Refresh, Open Folder.

## Downloads
- **Download…** fetches the current map view, or the ALT-selected area if there is one, through the existing zoom-range prompt and tile prefetcher, then records the area.
- Right-click → **Prefetch** downloads are recorded as areas as well, so the existing workflow shows up in the list.
- Areas are kept in `gmapcache\offlinemaps.json` next to the tiles.

## `.mappack` Files
- A `.mappack` is a zip holding `manifest.json` (the area entries) plus `tiles/{provider}/{zoom}/{y}/{x}.jpg`, the same layout the tile cache uses on disk. Importing copies tiles into the cache and keeps any already present. The file can also be unpacked by hand into `gmapcache\TileDBv3\en\` if the app is unavailable.
- Tiles are stored uncompressed (JPEGs don't shrink), so export is fast; overlapping areas share tiles within one pack.
- Packs without a manifest import too; their areas are derived from the tile extents.
- Entry paths are validated on import, so a crafted pack cannot write outside the cache folder. Tiles for a map source that isn't installed are copied with a warning.

---

# Reliability, Performance & Threading

- `SuspendLayout`/`ResumeLayout` and async loading on heavy tabs (Tuning, Params, Dashboard) to eliminate UI stalls
- Faster, smoother docking/undocking of flight modes panel
- Increased debouncing on 2D→3D map sync
- Robust GStreamer start/stop handling
- Resilient QuickView state under churn

---

# Reliability Fixes Over Upstream

Behaviour that differs from upstream because the fork fixed it:

- The 3D map doesn't hit the intermittent `Destination array was not long enough` crash
- SITL starts with home at the ground height of the chosen point, so terrain readings match ([#28](https://github.com/Titan-Dynamics/TitanPlanner/issues/28))
- The Messages tab keeps updating on long flights, keeps your place when scrolled up, and has a right-click **Clear Messages** ([#23](https://github.com/Titan-Dynamics/TitanPlanner/issues/23))
- Battery monitor calibration handles DroneCAN and other digital monitors: voltage Calc explains they are calibrated on the device, and current Calc scales `BATTx_CURR_MULT` ([#27](https://github.com/Titan-Dynamics/TitanPlanner/issues/27))
- The EKF status window keeps working on long sessions instead of failing with a GDI+ error ([#31](https://github.com/Titan-Dynamics/TitanPlanner/issues/31))
- Mission, fence and rally uploads don't send the vehicle an extra MISSION_ACK (upstream PR [#3705](https://github.com/ArduPilot/MissionPlanner/pull/3705))
- SSH (terminal, SCP/SFTP log download, Solo tools) uses SSH.NET 2026, with current key exchanges and ciphers and the `ScpClient` security fixes; SkiaSharp packages aligned at 2.88.8 (upstream PR [#3729](https://github.com/ArduPilot/MissionPlanner/pull/3729))
- Link quality counts reordered MAVLink packets (UDP over LTE, Starlink, VPNs) as received, so the HUD shows the real loss (upstream PR [#3807](https://github.com/ArduPilot/MissionPlanner/pull/3807))
- The RF Map outline (Ctrl+W) shows the real line-of-sight range at the aircraft's current height (upstream PR [#3825](https://github.com/ArduPilot/MissionPlanner/pull/3825))
- ADS-B from adsb.lol works (it requires a User-Agent) and stays within its rate limit
- Elevation (SRTM) lookups are thread-safe and never block the UI behind a slow load
- The Full Parameter List opens without crashing, and Flight Modes populates its `RCx_OPTION` lists on load
- Starting a camera that another program holds open fails gracefully instead of crashing
- Dashboard items repaint after context-menu edits, the panel never outgrows its tab, and the default layout has no duplicate "distance to home"
- The 3D map's nav-bearing line points at the actual waypoint, and the PLAN and DATA maps stay in sync
- Aborting a script when none is running is a no-op rather than a crash
- Servo Output's function dropdown shows the real value for REAL32-stored integer params such as `SERVOn_FUNCTION`; upstream truncates and can show the option below (Gripper for LandingGear)
- Firmware flashing and the installer/release zip work on current .NET builds
- Builds cleanly in Visual Studio 2022 and with `dotnet build` / `dotnet test`

---

# Auto-Update Pipeline

## CI Workflow
GitHub Actions builds Release and Debug, packages an MSI (`TitanPlanner-Installer.msi`) and zip, computes `checksums.txt` and `version.txt`, and publishes:
- A **`development-build`** prerelease on every push to `development`
- A **`TitanPlanner-<version>`** stable release on every push to `master`
- Each release's notes list every commit since the previous release on its channel (the previous `development-build`, or the previous `TitanPlanner-*` tag), with the fork's commits first and the commits brought in from upstream in a collapsed section. The same notes are published as the release's `ChangeLog.txt`. `.github/scripts/release-notes.sh <development|master>` previews them locally
- Upstream's Linux and macOS workflows build here too, but their `linux-latest` / `osxlatest` prereleases are only published from the ArduPilot repository, so this fork's Releases page stays limited to the two channels above

## In-App Updater
Reads `version.txt` from the channel URL (stable or beta) and compares as `System.Version`. When a newer build exists it streams only the files that differ straight out of the release zip (`TitanPlanner.zip`) with HTTP range requests, checks each file's MD5 against `checksums.txt`, then hands off to `Updater.exe` for the in-place swap. Both channels use the same path. The "update found" prompt's ChangeLog link opens that release's notes. The splash screen and title bar show the single build-stamped version (e.g. `1.3.9615.29424`) that the updater compares against.

---

# Installation & Data Folders

## Its Own Product
The MSI installs **TitanPlanner** as a product of its own: `Program Files\TitanPlanner`, a TitanPlanner Start-menu folder and uninstall entry, and its own Windows upgrade code. Log file associations use `TitanPlanner.*` ProgIds, and the window title reads "TitanPlanner".

Older TitanPlanner installers used the identity of stock ArduPilot Mission Planner. The TitanPlanner MSI removes such an install only when it is TitanPlanner, recognised by `TitanDynamics.mpsystheme` in `Program Files (x86)\Mission Planner`, so upgrading from an older TitanPlanner needs no manual uninstall while a stock Mission Planner install is left alone. An older TitanPlanner installed to a custom folder is not detected and stays installed alongside. Nothing removes TitanPlanner except its own uninstaller: stock Mission Planner's installer and the NextVision edition install alongside it.

## Its Own Data Folders
Settings, logs, map tiles, offline maps, elevation data and caches live in `ProgramData\TitanPlanner` and `Documents\TitanPlanner` instead of the `Mission Planner` folders shared with stock Mission Planner. On first launch TitanPlanner copies everything over from the `Mission Planner` folders: settings and configuration profiles before startup continues, and the rest (which can be gigabytes of map tiles) on a low-priority background thread that resumes on the next launch if interrupted. Nothing is moved or deleted, files already in the new folders are never overwritten, and if the disk lacks space for the copy it says so and retries on a later launch.

---

# Misc UX

- **Reboot Vehicle** button in the Actions tab; global **`Ctrl+R`** shortcut to reboot autopilot
- "Don't show again" option on the param-missing dialog, info dialogs, the failsafe-mode-change warning, and the ground-station-log dialog
- Param write toast notifications
- Actions tab laid out as a flow layout with the payload controls alongside
- Revamped Telemetry Log tab
- Logs tab combining Dataflash + Telemetry views
- Custom upgrade-path logic so existing original Mission Planner users transition cleanly to the Titan Dynamics edition (theme, default tab visibility, settings migration)
- No console window behind the main exe

---

# Upstream Base

The fork tracks `upstream/master` and currently includes upstream as of 2026-10-16. Where the two conflict, the fork keeps its own UI (HUD layout, Actions tab, Motor Setup, Battery Monitor, Radio Calibration, themed tabs) and takes upstream's fixes. Upstream capabilities in this build that users may notice:

- 32-bit MAVLink system IDs everywhere (GCS id, vehicle lists, map markers)
- Plugins can add actions to the Actions tab dropdown and draw on the HUD (`OnCustomPaint`)
- MAVFtp: recursive folder delete, file modification dates, and a Dokan driver check before mounting
- A hardened built-in HTTP server: connection limits, no path traversal, guided-mode endpoint limited to localhost; `[link;...]` markup only opens http(s) targets
- NTRIP GGA sent from the live vehicle position
- Packaged Linux builds (`Linux/build.sh`, `run.sh`) and a touch-friendly main menu on Linux
- The WPOverlay2 mission pipeline, `MissionSegmentizer` arcs, and `ThemeManager` theming of ListBox/PropertyGrid
- Correct behaviour on Turkish-locale systems for `.BIN` log detection, KMZ export and mode changes by name; MSI requires .NET Framework 4.7.2
