# AGENTS.md

This file provides guidance to coding agents (Claude Code, Codex, Copilot and others) working with code in this repository.

## What this is

Mission Planner is the ArduPilot Ground Control Station (GCS): a large C# / .NET Framework
Windows Forms desktop application for configuring, flashing, planning missions for, and
monitoring ArduPilot/PX4 vehicles over the MAVLink protocol. The same source also targets
Android (Xamarin) and macOS/iOS via a cross-platform library build.

## Build & run

Building is **Windows + Visual Studio 2022 + MSBuild only**. There is no supported build on
Linux/macOS (those platforms only *run* the prebuilt binaries via Mono). Do not expect to
compile this repo in a Linux container.

```bat
git submodule update --init          :: required once; pulls ExtLibs/mono and others
.nuget\nuget.exe restore MissionPlanner.sln
msbuild -v:m -restore -t:Build -p:Configuration=Release MissionPlanner.sln
```

- The main app is built from **`MissionPlanner.sln`** → `MissionPlanner.csproj` (`net472`, `WinExe`).
- Output goes to **`bin\Release\net461\`** (or `bin\Debug\net461\`) — the `net461` folder name is
  intentional (`AppendTargetFrameworkToOutputPath=false`), it is *not* a separate target.
- `build.bat`, `build - debug.bat`, `build - Clean.bat` are the maintainer's local build scripts
  (they also do appx packaging/signing/rsync — not needed for plain builds).
- To import the exact VS workload/component set, use `vs2022.vsconfig` (or `vs2019`/`vs2026`).

### Cross-platform library build (Android / Mac)

`MissionPlannerLib.csproj` (`netstandard2.0`, `library`) recompiles the same sources with
`DefineConstants=...;LIB`. Android packaging is done via `build - Lib.ps1` and
`ExtLibs/Xamarin/Xamarin.Android`. See the `LIB` conditional-compilation note below — it is the
single most important thing to understand before touching shared code.

### CI

GitHub Actions (`.github/workflows/main.yml` = "DotNet Build", plus `android.yml`, `linux.yml`,
`mac.yml`). `main.yml` builds the solution on Windows, runs the `Sysid32Tests`, packages an MSI
and `TitanPlanner.zip` with `checksums.txt` / `version.txt`, and publishes a `development-build`
prerelease on pushes to `development` and a `TitanPlanner-<version>` release on pushes to
`master`; the in-app updater reads those. Its `android`, `linux` and `mac` jobs are copies of the
build steps in upstream's `android.yml`, `linux.yml` and `mac.yml` (which stay as upstream has them,
disabled in this repo), and add an APK, a Linux archive and a macOS `.dmg` to the development
build; the `publish` job waits for them but needs only the Windows build to succeed. When upstream
changes those workflows' build steps, mirror the change into `main.yml`. Release notes (body and the `ChangeLog.txt` asset) come
from `.github/scripts/release-notes.sh`: every commit since the channel's previous release, fork
commits separated from upstream's. The Linux/macOS workflows build but only publish
releases when running in the upstream ArduPilot repository.

## Tests

`MissionPlannerTests/` is an MSTest project (`MSTest.TestFramework` 2.2.10, `net472`) that
references the main projects. Tests use `[TestClass]` / `[TestMethod]` and `Assert`.

```bat
:: build then run via the VS Test Explorer, or:
vstest.console.exe bin\Release\net461\MissionPlannerTests.dll
:: single test:
vstest.console.exe MissionPlannerTests.dll /Tests:DetectBoardTest
```

Coverage is light and focused on pure logic (board detection, firmware parsing, downloads). CI
runs only the MAVLink `Sysid32Tests`; run the rest locally. When adding tests, mirror the existing
folder layout (`MissionPlannerTests/Utilities`, `/GCSViews`, ...).

## Architecture (the big picture)

The dependency flow is: **transport → protocol → vehicle state → UI**.

- **`Program.cs`** — process entry point and crash/exception handling.
- **`MainV2.cs`** — the main `Form` and central hub. It owns the active connection as the static
  `MainV2.comPort` (a `MAVLinkInterface`), hosts the menu, and swaps the `GCSViews` screens in and
  out of the main window. Most global state hangs off `MainV2`. This file is ~190 KB; navigate by
  symbol, don't read top-to-bottom.
- **`GCSViews/`** — the top-level screens the menu switches between: `FlightData` (HUD/telemetry/
  map), `FlightPlanner` (waypoint/mission editor), `InitialSetup`, `ConfigurationView` (Config/
  Tuning + the many `Config*.cs` sub-panels), `SITL`, `Help`.

### Connection & telemetry stack (in `ExtLibs/`)

- **`ExtLibs/Comms/`** — transport layer implementing `ICommsSerial`: `CommsSerialPort`,
  `CommsTCP`, `CommsUDP`, `CommsBLE`, `CommsNTRIP`, etc. Pick a transport, hand it to the interface.
- **`ExtLibs/Mavlink/` (`MAVLink.csproj`)** — the MAVLink protocol: generated message structs,
  `MavlinkParse` (frame parse/serialize), CRC. Largely machine-generated.
- **`ExtLibs/ArduPilot/Mavlink/`** — the live link logic:
  - `MAVLinkInterface` (`: MAVLink, IMAVLinkInterface`) — reads/writes packets over a transport,
    exposes high-level operations (get/set params, mission up/download, command_long, etc.).
  - `MAVState` — represents a single connected vehicle (sysid/compid) and holds `cs`, its
    `CurrentState`. `MAVList` tracks multiple vehicles on one link.
  - `CurrentState` (`ExtLibs/ArduPilot/CurrentState.cs`) — the decoded, UI-facing telemetry model
    (attitude, position, battery, mode...). Forms/controls bind to `MainV2.comPort.MAV.cs`.
- **`ExtLibs/ArduPilot/`** — the vehicle/domain layer beyond telemetry: firmware
  (`APFirmware`, `Firmwares`), geofences, `BoardDetect`, mission packing.

### Other notable `ExtLibs/` (each is its own csproj; there are ~90)

`Controls/` (custom WinForms controls), `Core/` + `Interfaces/` (shared primitives, the
`ServiceLocator` DI registry, `ICommsSerial`), `Maps/` + `GMap.NET.*` (mapping), `GDAL`/`ProjNet`/
`SharpKml` (geo/projection/KML), `DroneCAN`/`UAVCANFlasher`, `Comms`, plus many vendored DLLs.
The solution has ~93 projects; treat `ExtLibs/` as the bulk of the real functionality.

### Plugins & scripting

- **`Plugin/`** (`Plugin` base class + `PluginLoader`) and **`Plugins/`** (built-in plugins plus
  `exampleNN-*.cs` templates) — plugins are loaded at runtime to extend the UI/behavior.
- **`Script.cs` + `Scripts/`** — IronPython scripting host exposed to users (`cs`, `Script.*` API).

## Conventions & gotchas

- **`LIB` conditional compilation is the #1 trap.** Shared code is compiled both into the desktop
  `Exe` (full `System.Drawing` / `System.Windows.Forms`) and into the netstandard `LIB` build for
  Android/Mac. You will see `extern alias Drawing;` guarded by `#if !LIB`, and `ZZZLibShims.cs`
  provides cross-platform shims (e.g. `MissionPlanner.Drawing`) for the `LIB` build. When adding
  code that touches drawing, WinForms, P/Invoke, or other desktop-only APIs, guard it with
  `#if !LIB` or it will break the Android/Mac build.
- **Localization.** Each Form has an English `Name.resx` (the source of truth) plus per-culture
  siblings like `FlightData.ko-KR.resx`, `MainV2.zh-Hans.resx`. App strings go through `Strings.resx`
  / the `L10N` class (`Strings.Culture` is set from the `language` setting). Translations are
  managed via Crowdin (`crowdin.bat`) — **do not hand-edit the non-English `.resx`**; edit the
  English resource and let translation flow back.
- **WinForms file triplets.** A screen is `Foo.cs` (logic) + `Foo.Designer.cs` (generated layout) +
  `Foo.resx` (embedded resources). Edit the `Designer.cs` through intent, not by hand where possible.
- **`createproj.bat`** regenerates the `<Compile>`/`<EmbeddedResource>` item lists for the csproj by
  scanning the tree — useful when files are added/removed but kept out of sync with the project.
- **`ServiceLocator`** (`ExtLibs/Interfaces/ServiceLocator.cs`) is the lightweight DI used to inject
  platform-specific implementations (resolve with `ServiceLocator.Get<T>()`).
- **`.editorconfig`** intentionally downgrades many `CAxxxx` analyzer rules to suggestion/silent —
  don't treat those as actionable warnings.
- **`Resources/`** holds icons/images; themes are `*.mpsystheme` files driven by `ThemeManager.cs`.

## TitanPlanner fork notes

This repository is the Titan Dynamics fork of Mission Planner. `CHANGES.md` is the feature-level
summary of everything the fork adds over upstream; update it (and `README.md` if user-facing)
whenever a change lands. It is not a changelog: it describes what the fork does differently today,
in short user-facing lines, so a reader can quickly see the major features and fixes. Leave out root
causes, timings, implementation details and history (no "no longer", "used to", "was removed");
the git log has those. `ChangeLog.txt` is upstream's and is not maintained here.

- **Upstream sync.** `upstream` = `ArduPilot/MissionPlanner`. Merge `upstream/master` into
  `development`, resolve conflicts in favour of the fork's UI (HUD layout, `FlightDataActions`,
  Motor Setup, Battery Monitor, Radio Calibration, themed tabs) while taking upstream's fixes,
  then update the "Upstream Base" section of `CHANGES.md` (the date, and upstream capabilities users
  may notice). Upstream's Servo/Relay and Aux Function tabs are boxes in `FlightDataActions` here, and the
  HUD's right-click Video menu is the Video tab (`FlightPlannerVideoOptions`), so upstream changes to
  them belong there, not back in `FlightData.Designer.cs`. Expect upstream's mechanical changes (e.g. the `byte` → `uint` sysid widening) to also
  apply to fork-only code. Upstream PRs taken before they merge get a line in "Reliability Fixes Over
  Upstream" naming the PR.
- **Line endings.** The repo stores a mix of CRLF and LF blobs and `core.autocrlf=true` is in use.
  Before staging, compare `git diff --stat` with `--ignore-cr-at-eol`; a whole-file rewrite means
  the line endings changed, not the code. Files whose HEAD blob is CRLF must be staged as CRLF.
- **Android/macOS/Linux builds.** Android and macOS compile `MissionPlannerLib.csproj` (`LIB`) against the
  `ExtLibs/mono` WinForms port, whose submodule commit has to match the upstream `ZZZLibShims.cs` it was
  merged with (take upstream's submodule pointer in a merge). The `LIB` build and the Xamarin projects stay on
  upstream's SkiaSharp 2.80.2: 2.88 fails the Android and macOS restores (a package downgrade against the Xamarin
  projects' 2.80.2), and upstream reverted its own move to it for breaking those builds; the desktop app has its own
  SkiaSharp 3. Windows-only controls such as WebView2 get a `ZZZLibShims.cs` stand-in, and Windows-only P/Invokes
  in desktop code that Linux runs (user32, gdi32) need an OS check, or Mono throws `EntryPointNotFoundException`. The
  Linux build compiles the desktop app with `Linux/Build.targets`, which collapses each project to its desktop
  target except `GMap.NET.Drawing`, kept netstandard too for `MissionPlanner.Utilities`. Check a `LIB` build
  locally with Visual Studio's MSBuild (`msbuild -restore -p:Configuration=Release MissionPlannerLib.csproj`;
  `dotnet build` can't build the mono submodule). `ExtLibs/Xamarin/Xamarin/Xamarin.csproj` also compiles some
  desktop files (e.g. `ConfigRawParams.cs`) against its own `HUD` and the `ThemeManager` stub in
  `Controls/Extensions.cs`, so a new `ThemeManager` or `HUD` member used there needs a stub; build that project the
  same way. `Linux/SmokeTest.cs` (run by the Linux job) launches the app and checks the fork's DATA screen
  (`ThemedTabStrip`, `FlightDataActions`), so it changes with that layout.
- **Build gotchas.** `MissionPlannerLib.csproj` builds into `obj2\`; `MissionPlanner.csproj` must
  keep its `obj\` / `obj2\` excludes or the Xamarin-generated `FirmwareSelection.xaml.g.cs` is
  compiled twice. Keep `GenerateResourceUsePreserializedResources` on, or `dotnet build`/`dotnet test`
  fail. Both `plugins\` and `Plugins\` exist on disk; the csproj excludes both.
- **Product identity and data folders.** The app is "TitanPlanner": its own MSI upgrade code and install
  folder (`wix/Program.cs`) and its own data folders via `Settings.AppConfigName`. On first launch
  `DataFolderMigration` copies the previous folders (`Settings.PreviousAppConfigNames`, "Mission
  Planner") into the new ones. The NextVision edition sets these to "TitanPlanner-NextVision" and
  `{ "TitanPlanner", "Mission Planner" }`, so those lines differ between the two repos by design.
- **3D map.** `Controls/Map3D/` is the 3D view (excluded from the LIB build). `Terrain/` streams satellite
  terrain: `TerrainQuadtree` picks tiles by screen-space error, up to a finest zoom set by the camera's height
  (`SelectionParams.MaxZoomFor`), `TileLoader` fetches imagery (through the shared GMap cache), samples SRTM
  (`srtm.GetCell`) and builds meshes on worker threads, and `TerrainEngine` uploads and draws them on the render
  thread. `Rendering/` holds the GL helpers and GLSL 1.10/ES 2.0 shaders, `Overlays/` the mission, vehicle and
  ADS-B drawing, `Scene/` the UTM frame, camera and pose smoothing. `VehicleTracker` dead-reckons and
  Kalman-filters the pose (MAVLink velocity is north, east, down); the trail and ADS-B prediction smooth too,
  and `Map3DSettings.SmoothMovement` turns all of it off for raw telemetry. `CloudRenderer` draws the layers
  `Scene/CloudField` defines (a tiling noise texture, heights kept above the vehicle, and the coverage
  thresholds for a cloud density, measured on the texture with the shader's sampling) as quads shaded in the
  fragment shader. Selection, loading and the cloud field are pure logic with tests in
  `MissionPlannerTests/Controls/`.
- **2D maps.** Setting `GMapControl.Position` or `Zoom` re-projects every overlay and repaints synchronously,
  and `GMapControl.Invalidate` is throttled to about 9 fps. The DATA map pans with `myGMAP.Pan` plus
  `((Control)map).Invalidate()` (dragging and the autopan timer in `FlightData`). Zoom from user input goes
  through `myGMAP.AnimateZoom` / `SetZoom`, and zoom controls sync to `ZoomTarget`, not `Zoom`, so the
  write-back doesn't cut the animation short. Hidden shapes are skipped by that re-projection, which is why
  `FlightData.UpdateOverlayVisibility` hides the no-fly shapes (`NoFly/*.kmz`, ~200k points worldwide) away
  from the view.
- **GMap fractional zoom.** A fractional zoom is drawn scaled (and rotated) about `Core.renderOffset`, which
  is the view centre only after a whole-level zoom or a `Position` set; a `Core.DragOffset` pan moves it, and
  `Core.FromLocalToLatLng` / `FromLatLngToLocal` leave the scale out. Pan with `myGMAP.Pan` (keeps
  `Position` on the centre), measure with `myGMAP.FromLatLngToMapLocal`, and zoom through `myGMAP`, which
  moves the pivot back to the centre first. A rotated map turns its tiles about that point but its overlays
  about the view centre, so there `Pan` moves `Position` itself (re-centring and re-projecting each step). The DATA update loop changes overlays on its own thread and GMap's
  collections don't lock, so UI-thread code that walks overlays has to survive a collection changing under it.
- **PLAN terrain profile.** `Controls/TerrainProfile/` (excluded from the LIB build): `MissionProfile` is the pure
  route logic (mission order from home, altitudes AMSL by each item's frame, terrain-frame legs following the
  ground, takeoff/landing/RTL) with tests in `MissionPlannerTests/Controls/TerrainProfileTests.cs`;
  `TerrainProfilePanel` draws it over `MainMap` from its Paint event and takes the mouse through
  `myGMAP.OverlayPanel`. `FlightPlanner.writeKML` hands it the mission; it rebuilds on a worker thread, taking
  the ground from a loaded GeoTIFF elevation model first (as `srtm.getAltitude` does), else `srtm.GetCell`, and
  retries while tiles download. It replaces upstream's `ElevationProfile` dialog.
- **Commits.** Messages carry no AI attribution trailers. Do not commit or push unless asked.
