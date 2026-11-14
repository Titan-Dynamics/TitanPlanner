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
- **Waypoint and wind arrows** on the HUD under the speed tape: the waypoint / home distance with an arrow pointing at it, and the wind speed with an arrow the way it blows, both relative to the nose. HUD Items (right-click the HUD) can hide either line
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

With several vehicles connected, every one is drawn with its own model, the ones not selected at half opacity as on the 2D map, each with a locating beacon (rings that grow out to the size of the waypoint markers and fade) and its system ID just above it, so they stand out from far off; selecting another puts the camera straight behind it, with its trail already recorded.

## Terrain Streaming Engine
Terrain streams Google Earth style:
- **Screen-space level of detail.** A tile quadtree is refined until each imagery texel is about a pixel on screen, up to zoom 24 within 100 m of the ground, 22 up to 500 m and 20 above, so detail follows what the camera actually sees; there is no zoom setting to manage.
- **Coarse first, centre outwards.** Load requests are ranked by distance relative to tile size and by angle from the view centre, re-ranked every frame, and dropped when no longer wanted. Tiles out of view are prefetched coarser so turning never shows holes.
- **No holes or overlaps.** A tile is replaced by its children only once all of them are loaded; skirts hide cracks between levels.
- **Off the UI thread.** Eight workers fetch imagery, sample elevation once per tile and build meshes; the render thread only uploads finished tiles within a per-frame time budget, with BGRA uploads, mipmaps and anisotropic filtering. A bounded GPU cache keeps recently seen terrain.
- **Shared imagery cache.** Imagery goes through the 2D map's tile cache, so tiles seen on the 2D map and imported `.mappack` files load instantly and offline mode works.
- **Elevation that always arrives.** Missing SRTM cells are downloaded instead of leaving gaps; tiles start from their parent's heights and are re-meshed when the data lands.
- **No reloads while flying.** Tiles keep their own UTM origins, so moving the scene origin never discards loaded terrain.
- **Light when hidden, quick to show.** Hiding the 3D map frees its terrain and elevation data so RAM drops back, while the view itself stays ready, so showing it again is near-instant. Elevation lookups never block the UI, and the vehicle model loads in the background.
- **Idle rendering.** Full frame rate only while something changes (connected vehicle, terrain streaming, markers, ADS-B, input, the 2D map moving, or the stats panel open); otherwise one frame a second.
- **Stats For Nerds** (3D map Settings, off by default): a colour-coded panel with FPS and a frame-time graph, terrain streaming internals (tiles, per-zoom histogram, GPU memory, queue, load rate, failures, elevation status), the camera view and GPU info.

## Camera & Visual Effects
- **FPV mode** with smooth camera movement and pitch-matched heading lines; Alt-F swaps between FPV and the chase view
- **HUD Overlay** setting: the DATA tab's HUD, with its items and plugin drawings, over the 3D view without its sky and ground
- **Clouds**: three drifting layers (cumulus, patchy mid-level cloud and high streaks) at least a kilometre above the vehicle, with cloudy and clear stretches, shaded from below, hidden behind high terrain and fading into the horizon; a **Cloud Density** setting from 0 (none) to 100, default 50, sets how much of the sky they cover
- **Orbital camera** mode
- **Distance haze** that fades terrain into the sky's horizon colour before the edge of the loaded terrain, at every altitude
- **Sky gradient** that follows the true horizon through pitch and roll
- **Heading indicators** and dynamic render distance
- **Trailing flight path** with Kalman-smoothed lines on all axes, curving into the vehicle along its velocity; render gated on stable telemetry
- **Smooth Movement** setting (on by default): the vehicle's position, altitude and attitude, its trail and ADS-B traffic are carried along between telemetry updates and filtered; off, everything is drawn from the raw telemetry. Disarmed, or when the position jumps more than 100 m, the vehicle and the view go straight to it instead of gliding across the map
- **High-altitude rendering** improvements

## ADS-B in 3D
ADS-B traffic is rendered with proper depth testing and pass ordering so it correctly occludes against terrain and the vehicle.

## Persistence & UX
- Camera distance, height, and angle saved on close
- Settings dialog overhaul with persistence and out-of-range guards, opened from a translucent gear icon in the top-left corner
- Configurable **Marker Size**, for the waypoints and the other vehicles' beacons
- Waypoint markers fade out as the vehicle comes within 61 m of them, gone at 15 m, rather than vanishing at once
- A yellow dot marks the centre of each numbered waypoint marker
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
- **Video tab** in place of the HUD's right-click Video menu: one source at a time (capture device, RTSP stream, MJPEG stream or GStreamer pipeline) with one Start/Stop button and a status indicator across the top (stopped, connecting, playing and its size, no video), remembered between launches, an RTSP URL that plays as entered with a HereLink preset, and boxes for the HUD overlay (Alt-H flips it) and OSD color, and for recording the HUD (with an Open Folder button)
- **Inspector tab** (MAVLink Inspector exposed without `Ctrl+F`), in the theme's colours with field values in the accent colour
- **Swarm tab** in FlightData for commands sent to several aircraft at once (see Multiple Aircraft)
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
- Tree collapsed by default in tab embeddings to save space, with a drawn arrow on its show/hide button
- Param lists that are showing, including the one docked under the DATA map, reload once the full list has arrived after connecting
- **Reset** (to defaults) sits apart at the toolbar's right edge as a plain button
- User params can be set on the Flight Modes page

---

# Setup & Configuration Pages

## Betaflight-Style Motor Setup
**Motor Setup** (upstream's "Motor Test") is a Betaflight-style page in the Mandatory Hardware section, shown only for copter/quadplane vehicles, with live frame and class type images. The frame diagrams ship with the app, so they show on air-gapped setups, and are refreshed from ardupilot.org when online.

## Servo Output Tab
A revamped Servo Output panel with click-or-drag sliders for trim, an "Equidistant" min/max helper, and proper min/max-from-trim handling. The trim slider stays in sync with the combobox. Min, trim and max accept 500–2500 µs and step by 10. Edits are written in the background once you pause, so the page never waits on the vehicle.

## Radio & Calibration
Visual-stick-controls Radio Calibration UI with upstream's reversing logic; polished accel calibration.

## Other Setup Pages
GUI cleanups across Accelerometer Calibration, Compass, Failsafe, Flight Modes, Initial Setup Serial; Battery Monitor pages merged into one unified screen; voltage/current calibration enabled regardless of selected sensor. Flight Modes shows its modes and RC Options in side-by-side boxes and writes each change as it is made, with no Save button. The Planner settings page packs its boxes into equal-width columns across the whole window, with a Keyboard Shortcuts box across the bottom listing every shortcut by where it works.

## Joystick Configuration
The Joystick configuration window opens quickly and lays out the joystick's inputs, the RC channels and the button assignments in side-by-side boxes, under a top row with the joystick, its buttons and the Elevons, Manual Control and Send Rate options. The inputs box shows every axis, slider and hat the joystick has, by the joystick's own names and the axis to choose for each, with live values, and every button lit while it is pressed, enabled or not. Each Detect button reads "Detecting..." while it listens, with no prompts: a channel takes the axis that moved most in 3 seconds, a button row the next button pressed. The send rate (25 to 100 Hz, 50 by default) sets how often the joystick is read and sent to the vehicle, each reading going out as soon as it is taken, and how often the window shows it.

## Configuration Profile Management
Save and load named configuration profiles for the Mission Planner application itself.

---

# Auto-Connect & Devices

## USB Auto-Connect
A dedicated USB auto-connect path with VID coverage for all common flight controllers (including Cube). Toast notification fires when a device auto-connects. Auto-connect is disabled while flashing firmware to avoid races.

## ELRS
ELRS airport support added.

---

# Multiple Aircraft

Several aircraft on one link, told apart by their MAVLink system id, flown from one ground station.

## Switching Aircraft
The other aircraft on a link get their parameters in the background after the connect, so switching to one (the system id selector, or a click on its map icon) is immediate, and the one switched to goes first. An aircraft without MAVFTP gets the download dialog the first time it is selected. The Planner settings can turn the background downloads off for a slow link shared by several aircraft.

## Swarm Tab
The aircraft on every link in one list: system id, mode, altitude, the state of its parameters and the result of the last command sent to it. Planes are ticked to be commanded.

## Stacked Orbit
Sends the ticked planes to circle one point, one above the other: right-click the map and choose **Stacked Orbit Here**, then set the floor and the separation (in the altitude units chosen in the Planner settings) and whether the altitudes are relative, absolute or above terrain.
- The lowest aircraft takes the floor, the next lowest the slot above, and so on. The list shows each aircraft's slot before anything is sent, and the map marks the center with the widest of the planes' loiter circles.
- A confirmation lists every aircraft with its altitude now and its slot, and what is confirmed is what is sent: one aircraft at a time, each switched to GUIDED and given the point, stopping at the first that refuses or doesn't answer. **Retry** sends the rest to the same slots, unless one would pass a level the stack now holds.
- Refused: a separation under 10 m, a slot less than 30 m above the ground at the center (a Terrain floor under 30 m), an absolute floor under the field, and relative altitudes where the aircraft's homes differ enough to close the gaps.

The SIMULATION screen's **Plane Swarm - Single link** starts a swarm of planes on one link to try it with.

---

# Mode Selector & Vehicle State Toolbar

## Top-Bar Mode Selector
A compact mode selector lives in the top menu bar. Users pin the modes they want as toolbar buttons (persisted in `ModeSelectorPinned`); only the pins the connected vehicle has show, so a plane's FBWA stays off a copter's toolbar and comes back with the plane. The dropdown shows the vehicle's configured `FLTMODE1`–`FLTMODE6` (`MODE1`–`MODE6` on Rover) in a "Configured modes" section on top, whose remembered **Pin All** option puts them all first on the toolbar ahead of the user's own pins, then the full "Flight modes" list with favorite stars, sized to the list up to 640 px and scrolling where the window is shorter; it refreshes on connect and whenever a flight-mode parameter is written rather than polling. The selector auto-sizes based on available space and hides when not connected.

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
- **Terrain profile** along the foot of the PLAN map: the mission's altitude in the theme's accent over the ground under its route, with its length, peak and least clearance, places it goes below the terrain in red, and a readout and map marker for the point under the pointer; it folds to a slim bar, and takes the place of upstream's Map Tool → Elevation Graph dialog
- The terrain profile shows the rally points in purple, at the route's nearest point or, without a mission, along a line through them, and follows spline legs along the map's curve
- While connected, the terrain profile shows the vehicle's fence: the ceiling and floor (red when it will hold to them, grey when off or not in `FENCE_TYPE`) and where the route crosses the fence circle, which the PLAN map draws too; points outside the fence are ringed in red and named in its header (after upstream PR [#3813](https://github.com/ArduPilot/MissionPlanner/pull/3813))
- The terrain profile zooms with a dragged box or the mouse wheel and pans with Ctrl+drag, keeping the zoom while the plan is edited; it resizes from its top edge, and a double-click on its header makes it tall
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
- Smooth dragging on the DATA map, and an autopan that glides after the vehicle instead of jumping every few seconds
- Animated zoom on the maps: the mouse wheel, zoom slider, zoom box and zoom buttons ease to the new level, about the centre of the view
- An **(i)** button in the DATA map's corner toggles the legend of the vehicle's lines (Current Heading, Direction to WP, Target Heading, Ground Track)
- With several vehicles connected, the ones not selected are drawn at half opacity without their lines; Config > Planner's secondary display style can show them solid or hide them
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
- The DATA map only keeps the no-fly zones near the view, so zooming and panning stay fast with the worldwide zone files loaded
- Increased debouncing on 2D→3D map sync
- Robust video start/stop handling: stopped GStreamer and MJPEG video stays stopped, even when stopped while still connecting, and an unreachable MJPEG stream is retried once a second
- Resilient QuickView state under churn

---

# Reliability Fixes Over Upstream

Behaviour that differs from upstream because the fork fixed it:

- The 3D map doesn't hit the intermittent `Destination array was not long enough` crash
- Connecting or loading params doesn't crash when the progress dialog closes before it finishes
- HUD text always draws the right characters; upstream's OpenGL HUD can show "FAILSAFE" as "FAI AFE" or an altitude as "SS"
- SITL starts with home at the ground height of the chosen point, so terrain readings match ([#28](https://github.com/Titan-Dynamics/TitanPlanner/issues/28))
- The Messages tab keeps updating on long flights, keeps your place when scrolled up, and has a right-click **Clear Messages** ([#23](https://github.com/Titan-Dynamics/TitanPlanner/issues/23))
- Battery monitor calibration handles DroneCAN and other digital monitors: voltage Calc explains they are calibrated on the device, and current Calc scales `BATTx_CURR_MULT` ([#27](https://github.com/Titan-Dynamics/TitanPlanner/issues/27))
- The EKF status window keeps working on long sessions instead of failing with a GDI+ error ([#31](https://github.com/Titan-Dynamics/TitanPlanner/issues/31))
- SSH (terminal, SCP/SFTP log download, Solo tools) uses SSH.NET 2026, with current key exchanges and ciphers and the `ScpClient` security fixes (upstream PR [#3729](https://github.com/ArduPilot/MissionPlanner/pull/3729))
- The Actions tab's **Parachute_Release** releases the parachute; upstream's Do_Parachute sends it as Disable, which turns the parachute off until a reboot (upstream PR [#3854](https://github.com/ArduPilot/MissionPlanner/pull/3854))
- The Actions tab's commands send the reserved param3 as 0 (Preflight_Calibration still sets it); upstream sends 1, which an autopilot strict about reserved params refuses and Loiter_Unlim reads as a 1 m radius (upstream PR [#3853](https://github.com/ArduPilot/MissionPlanner/pull/3853))
- The initial parameters calculator's after-flight advice matches the vehicle: on Copter 4.7 `PSC_D_ACC_P` / `PSC_D_ACC_I` at 0.1 / 0.2 × `MOT_THST_HOVER` (upstream gives the older gains' values, ten times too high), and the `Q_` names on QuadPlanes (upstream PR [#3829](https://github.com/ArduPilot/MissionPlanner/pull/3829))
- ADS-B from adsb.lol works (it requires a User-Agent) and stays within its rate limit
- Elevation (SRTM) lookups are thread-safe and never block the UI behind a slow load
- The Full Parameter List opens without crashing, and Flight Modes populates its `RCx_OPTION` lists on load
- Starting a camera that another program holds open fails gracefully instead of crashing
- Dashboard items repaint after context-menu edits, the panel never outgrows its tab, and the default layout has no duplicate "distance to home"
- The 3D map's nav-bearing line points at the actual waypoint, and the PLAN and DATA maps stay in sync
- Survey (Grid) camera footprints follow the PLAN altitude mode: in Terrain they keep their size over rising ground, in Absolute they are taken from the altitude above sea level, and none is drawn where the camera would be at or under the ground; upstream always takes the altitude as relative to home
- Aborting a script when none is running is a no-op rather than a crash
- With Bluetooth on, the scan for Bluetooth telemetry radios doesn't crash the app as it finishes
- Servo Output's function dropdown shows the real value for REAL32-stored integer params such as `SERVOn_FUNCTION`; upstream truncates and can show the option below (Gripper for LandingGear)
- Firmware flashing and the installer/release zip work on current .NET builds
- Builds cleanly in Visual Studio 2022 and with `dotnet build` / `dotnet test`

---

# Auto-Update Pipeline

## CI Workflow
GitHub Actions builds Release and Debug, packages an MSI (`TitanPlanner-Installer.msi`) and zip, computes `checksums.txt` and `version.txt`, and publishes:
- A **`development-build`** prerelease on every push to `development`
- A **`TitanPlanner-<version>`** stable release on every push to `master`
- Each release's notes list every commit since the last `TitanPlanner-*` release (for the development build, everything it has that the newest release doesn't, so they read as the next release's would), with the fork's commits first and the commits brought in from upstream in a collapsed section. The same notes are published as the release's `ChangeLog.txt`. `.github/scripts/release-notes.sh <development|master>` previews them locally
- Upstream's Linux and macOS workflows build here too, but their `linux-latest` / `osxlatest` prereleases are only published from the ArduPilot repository, so this fork's Releases page stays limited to the two channels above

## In-App Updater
Reads `version.txt` from the channel URL (stable or beta) and compares as `System.Version`. When a newer build exists it streams only the files that differ straight out of the release zip (`TitanPlanner.zip`) with HTTP range requests, checks each file's MD5 against `checksums.txt`, then hands off to `Updater.exe` for the in-place swap, which waits for the planner to exit first. Both channels use the same path. A development build checks the development channel on every launch, so a new dev build is offered as soon as it's published; a release offers a newer release once a day. Turning on Beta Updates checks for a development build right away. The Updates box in Planner settings shows the running build, its date and how many releases behind the latest it is, and whether a newer development build is out. The "update found" prompt's ChangeLog link opens that release's notes. The splash screen and title bar show the single build-stamped version (e.g. `1.3.9615.29424`) that the updater compares against.

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
- The mouse wheel scrolls the page past a dropdown instead of changing it; an open dropdown's list still scrolls
- Actions tab laid out as a flow layout with the payload controls alongside, an **Aux Functions** box (four rows that each run a chosen aux function Low / Mid / High) and **Servos** and **Relays** boxes side by side below them, in place of upstream's Aux Function and Servo/Relay tabs
- Revamped Telemetry Log tab
- Logs tab combining Dataflash + Telemetry views
- **Open Log Folder** button in the Log Downloader
- **Copy** button on the error dialog: puts the error and the build version on the clipboard for pasting into a report
- Custom upgrade-path logic so existing original Mission Planner users transition cleanly to the Titan Dynamics edition (theme, default tab visibility, settings migration)
- No console window behind the main exe
- The top menu highlights the screen being shown, including after SITL starts and on connect

---

# Upstream Base

The fork tracks `upstream/master` and currently includes upstream as of 2026-10-25. Where the two conflict, the fork keeps its own UI (HUD layout, Actions tab, Motor Setup, Battery Monitor, Radio Calibration, themed tabs) and takes upstream's fixes. Upstream capabilities in this build that users may notice:

- 32-bit MAVLink system IDs everywhere (GCS id, vehicle lists, map markers)
- Plugins can add actions to the Actions tab dropdown and draw on the HUD (`OnCustomPaint`)
- MAVFtp: recursive folder delete, file modification dates, and a Dokan driver check before mounting
- A hardened built-in HTTP server: connection limits, no path traversal, guided-mode endpoint limited to localhost; `[link;...]` markup only opens http(s) targets
- NTRIP GGA sent from the live vehicle position
- Packaged Linux builds (`Linux/build.sh`, `run.sh`) and a touch-friendly main menu on Linux
- The WPOverlay2 mission pipeline, `MissionSegmentizer` arcs, and `ThemeManager` theming of ListBox/PropertyGrid
- Correct behaviour on Turkish-locale systems for `.BIN` log detection, KMZ export and mode changes by name; MSI requires .NET Framework 4.7.2
- Link quality counts reordered MAVLink packets (UDP over LTE, Starlink, VPNs) as received, so the HUD shows the real loss
- The RF Map outline (Ctrl+W) shows the real line-of-sight range at the aircraft's current height
- Mission, fence and rally uploads don't send the vehicle an extra MISSION_ACK
- A WinForms firmware picker with filters for platform, vehicle type, release and version
- MAVLink mirrors (MAVLink Output) can be added and stopped safely while connected, and a TCP listener accepts a new client without crashing
- Saved POIs load once, without duplicates, and are never overwritten by a failed read
- tlog playback selects the vehicle rather than the recording GCS; the PLAN home box shows altitude in display units
- COM port lists survive misbehaving Bluetooth drivers; signed driver for Pixhawk 6C
- SkiaSharp 3 on the desktop and faster drawing on Linux/Mono; Altitude Angel is removed
