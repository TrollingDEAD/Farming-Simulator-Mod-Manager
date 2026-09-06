# FsModManager

**A Windows desktop mod manager for Farming Simulator 25** — built to catch the problems that usually only surface after a crash, a broken savegame, or a confused multiplayer session.

Most FS25 mod troubleshooting today boils down to "post your `log.txt` on the forum and wait." FsModManager tries to close that gap: it detects your installation automatically, understands what's actually inside your mods, finds conflicts *before* they cause a crash, and reads your log file for you when something still goes wrong.

> All features below are implemented. If you spot something in the code that's actually still a stub or partial, it's worth flagging in an issue — the list reflects intended behavior as designed, not a line-by-line code audit.

---

## Table of Contents

- [Why this exists](#why-this-exists)
- [Features](#features)
- [Tech stack](#tech-stack)
- [Getting started](#getting-started)
- [Solution layout](#solution-layout)
- [Known limitations & honest caveats](#known-limitations--honest-caveats)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [License](#license)

---

## Why this exists

Farming Simulator's mod ecosystem has a few structural quirks that cause real, recurring pain for players:

- Mods load in **alphabetical order by zip filename**, and when two mods both modify a shared base-game file (like `fillTypes.xml`), the last-loaded one silently wins — with no error message.
- There's no standardized dependency system, so incompatibilities are usually discovered by trial and error, or by decoding a cryptic `log.txt` entry.
- Multiplayer requires every player to have matching mods and versions, which is entirely manual to verify today.
- A "clean test with no mods" is the standard first troubleshooting step Giants' own forum moderators recommend — and it's tedious and risky to do by hand.

FsModManager is built directly around these documented pain points rather than being a generic file browser for your mods folder.

---

## Features

### 🔍 Detection & setup
- ✅ Multi-launcher install detection — Steam, Epic Games, GOG, with manual-path fallback
- ✅ Launcher-agnostic game version detection (reads it straight from the executable)
- ✅ Mods folder resolution, including respecting a user-configured `modsDirectoryOverride`

### 📦 Mod library
- ✅ Mod icon, real display title (not the zip filename), and version shown as distinct fields
- ✅ Live-filtering search box (title, internal name, author)
- ✅ Category filters derived dynamically from your actual mod library, not a hardcoded list
- ✅ Right-click actions: show in folder, open `modDesc.xml`, copy internal mod name, delete (with an active-in-savegame warning first)
- ✅ Expandable per-mod detail panel: description, file size, last-modified date, descVersion, mod-kind classification (map / vehicle pack / placeable pack / script-only), and any parsing diagnostics
- ✅ Per-object content inspection — every vehicle, implement, and placeable a mod adds, with its shop image, price, brand, mass, and specs (capacities, working width, required power where determinable)
- ✅ Multiplayer-support and crossplay-status indicators (crossplay honestly shown as *Unknown* unless a real, verified data source is found — this app does not guess)

### ⚠️ Conflict detection
- ✅ Duplicate internal mod name (silent overwrite)
- ✅ Duplicate `storeItem` file references (silent shop-slot overwrite)
- ✅ Duplicate custom fill/fruit/vehicle type declarations
- ✅ Duplicate **vehicle specialization names** — schema-based detection straight from `modDesc.xml`, catching the exact class of error that produces FS25's "variable already exists" crash
- ✅ Shared base-game data file overrides (`fillTypes.xml`, `densityHeights.xml`) — reports **which mod actually wins**, based on FS25's real alphabetical load order, not just that a conflict exists
- ✅ Object-level conflict attribution with mod-level highlight rollup, so a problem is visible on the mod row before you ever expand it
- ✅ Lua global-function-collision heuristic (explicitly labeled low-confidence — pattern matching, not a real parser)
- ✅ Library-wide **Conflicts overview** — every issue in one severity-sorted place, not buried per-mod
- ✅ One-click auto-fix for mechanically-resolvable conflicts (storeItem rename, non-overlapping shared-file merge), with a full backup-and-revalidate pipeline behind every edit
- ✅ Duplicate-mod resolution with a scored keep/remove recommendation and visible reasoning — never a silent delete

### 🪵 Log Analyzer
- ✅ Parses FS25's `log.txt` and classifies every warning/error
- ✅ Attributes log entries back to the specific mod that caused them
- ✅ Recognizes and plain-language-explains known patterns: specialization collisions, missing/broken file references, texture and fillType issues, duplicate or missing localization keys, text-parameter mismatches, and — importantly — flags GPU/VRAM and map-geometry errors as **not mod-related**, so you don't waste time uninstalling mods to fix a driver issue
- ✅ One-click fixes for safely-automatable log issues, clear "why this can't be auto-fixed" explanations for the rest
- ✅ One-click "copy report" export formatted for pasting into a forum post or Discord

### 🩺 Diagnostics & environment health
- ✅ OneDrive / cloud-sync detection — flags the documented cause of "my mods folder disappeared" and savegame sync issues
- ✅ Mod filename validation (spaces/special characters are a real, common cause of mods silently failing to load)
- ✅ descVersion-vs-library compatibility heuristic (relative comparison, explicitly not a hardcoded version table that can't be verified)
- ✅ Duplicate/stale mod version detection across the mods folder

### 🎮 Load order & launch
- ✅ Per-savegame load order viewer/editor with drag-and-drop reordering and active/inactive toggles
- ✅ Launch the game directly from the manager
- ✅ Game-running guard on every operation that touches game files — nothing writes while FS25 is open

### 💾 Savegame safety
- ✅ Automatic snapshot before any risky operation (e.g. before rewriting `mods.xml`)
- ✅ Manual on-demand backups, with restore (which itself snapshots the current state first — restoring is never a one-way action)
- ✅ Configurable retention/pruning so backups don't grow unbounded

### 🌐 Multiplayer sync
- ✅ Export a shareable manifest of your active mods (name, version, SHA-256 content hash)
- ✅ Compare your manifest against a friend's or a server's to find exactly what's missing, extra, or mismatched before you try to play together

### 🧪 Troubleshoot: Clean Test
- ✅ Guided wizard that automates the standard "test with zero mods" troubleshooting procedure: mods are **moved, never deleted**, shader cache is optionally cleared, and you're guided to test on a fresh save
- ✅ Binary-search bisection to narrow "which one of 300 mods is causing this" down to roughly a dozen test rounds instead of a linear slog
- ✅ Crash-safe: an interrupted clean test is detected on next launch and you're prompted to restore before doing anything else

### 🔄 Self-updates
- ✅ Startup update check against this repo's GitHub Releases — once per app session, never nagging
- ✅ Non-intrusive banner prompt with the new version and a short changelog summary (from the published release notes when present, bundled changelog as fallback) — **you are always asked before anything downloads or installs**, never a blocking dialog, never silent
- ✅ Download progress shown live in the banner, then apply-and-restart on confirmation only
- ✅ Manual "Check for updates now" in Settings → About
- ✅ Apply-and-restart is deferred while a Clean Test session or a mod-file edit is in progress, so an update never interrupts a risky operation
- ✅ Update checks cleanly no-op on non-installed builds (`dotnet run`, raw publish output), so development runs never try to self-update

### 🎨 Interface
- ✅ Fully custom, borderless WPF shell — no third-party UI library, hand-built dark theme
- ✅ Pixel-based smooth scrolling (not the default WPF per-item jump)
- ✅ Consistent severity-tinted highlighting with tooltips across mod rows, object rows, and the conflicts overview

---

## Tech stack

| Layer | Choice |
|---|---|
| Language / runtime | C# on .NET 8 |
| UI framework | WPF (custom chrome, hand-built styles — no third-party control library) |
| MVVM | CommunityToolkit.Mvvm |
| Persistence | SQLite (mod/version tracking), plain JSON (manifests, pattern libraries) |
| HTTP / resilience | `HttpClient` + Polly *(reserved for future download/update features — currently unused, see [Roadmap](#roadmap))* |
| XML / archives | `System.Xml.Linq`, `System.IO.Compression` |
| Texture decoding | Pfim (for reading DDS-format mod icons and shop images) |
| HTML parsing | HtmlAgilityPack / AngleSharp *(reserved, currently unused — see Roadmap)* |
| Logging | Serilog |
| Testing | xUnit |
| Self-updates | Velopack (checks/downloads/applies updates from this repo's GitHub Releases) |

Core logic lives entirely in `FsModManager.Core`, a plain .NET class library with no UI dependency — the WPF shell is intentionally a thin layer on top, so a different front end could be swapped in later without touching the underlying logic.

---

## Getting started

### Option A — Just want to run the app? Download a release

Head to the [Releases page](https://github.com/TrollingDEAD/Farming-Simulator-Mod-Manager/releases), grab the latest installer/zip for Windows, and run it. No .NET SDK, no build tools needed — this is the recommended path for most users. Once installed, the app checks for its own updates on startup and offers to install them — always with your confirmation first (see [Versioning & updates](#versioning--updates)).

### Option B — Build from source

For developers, or if you'd rather build it yourself:

**Requirements:**
- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (not just the runtime — you need the SDK to build)
- Git (or just download the repo as a ZIP via the green "Code" button on GitHub if you don't have Git installed)

```bash
git clone https://github.com/TrollingDEAD/Farming-Simulator-Mod-Manager.git
cd Farming-Simulator-Mod-Manager

dotnet restore
dotnet build FsModManager.sln
dotnet test FsModManager.sln
```

Run the desktop app directly from source:

```bash
dotnet run --project src/FsModManager.App
```

Or produce a standalone executable you can copy elsewhere:

```bash
dotnet publish src/FsModManager.App -c Release -r win-x64 --self-contained true -o publish
```

The built app will be in the `publish` folder — run `FsModManager.App.exe` from there.

On first launch (either way), the app will try to auto-detect your FS25 installation. If none is found (or you have an unusual setup), you can point it at your install folder manually.

---

## Versioning & updates

This project follows [Semantic Versioning](https://semver.org/) (`MAJOR.MINOR.PATCH`). The version number lives in one place — `src/FsModManager.App/FsModManager.App.csproj`'s `<Version>` tag — and is used both for the assembly version shown in the app and for the release tag published to GitHub.

- **Current version** is shown in the app's title bar / About section.
- **Changelog:** every release's changes are documented in [`CHANGELOG.md`](CHANGELOG.md), following the [Keep a Changelog](https://keepachangelog.com/) format.
- **Auto-updates:** on startup, the app checks this repository's [Releases](https://github.com/TrollingDEAD/Farming-Simulator-Mod-Manager/releases) for a newer version (at most once per app session). If one is found, a banner tells you the new version and what changed, and waits for your "Update now" — updates are never downloaded or applied silently without confirmation. Once confirmed, the app downloads the new version (with live progress in the banner), applies it, and restarts itself. There's also a manual **"Check for updates now"** under Settings → About, and when running from source (`dotnet run` / raw `dotnet publish` output) update checks are skipped entirely — self-update only works in Velopack-installed builds.
- Releases are built and published automatically via GitHub Actions whenever a version tag (`v*`) is pushed — see [`.github/workflows/release.yml`](.github/workflows/release.yml) for the pipeline, or [`RELEASING.md`](RELEASING.md) for the maintainer walkthrough of cutting a release.

---

## Solution layout

```
FsModManager.sln
README.md
src/
  FsModManager.App/
    App.xaml(.cs)
    MainWindow.xaml
    Controls/
    Themes/
    ViewModels/
    Views/
  FsModManager.Core/
    Detection/        # install + launcher detection, version reading, mods-folder resolution
    ModScanning/       # modDesc.xml parsing, per-object content scanning, conflict detectors
    Savegames/         # savegame discovery, mods.xml load-order read/write
    Launching/         # game process launch + running-game guard
    Backups/           # savegame snapshot/restore
    Diagnostics/       # log analyzer, environment/mod-hygiene checks, clean test mode
    Editing/           # safe mod-file editing pipeline (backup → edit → validate → atomic swap)
    Multiplayer/        # mod manifest generation/comparison
    Models/
    Data/              # bundled JSON reference data (shared-file definitions, log patterns)
tests/
  FsModManager.Core.Tests/
```

*(Reflects the actual `FsModManager.Core` folder structure — adjust only if you've renamed/reorganized anything since.)*

---

## Known limitations & honest caveats

Being upfront about these matters more than making the feature list look complete:

- **No ModHub download/update automation.** There is no official public API for Giants' ModHub, and its terms of service haven't been fully cleared for automated scraping. Downloading and update-checking are intentionally out of scope for now — this is a *manager*, not a downloader.
- **Whether `mods.xml` entry order actually changes in-game override behavior**, versus just display order, hasn't been conclusively confirmed against real game behavior. Treat the load-order editor's gameplay effect as unverified until tested directly.
- **Crossplay compatibility has no confirmed, documented per-mod data source.** The app shows "Unknown" rather than guessing, and this is expected to stay "Unknown" for most mods until a real signal is found.
- **The Lua global-function-collision detector is a heuristic**, not a real parser — it can produce false positives/negatives and is explicitly labeled "possible," not "confirmed."
- **Shared-data-file auto-merging** (`fillTypes.xml` etc.) only ever merges non-overlapping additions automatically. If two mods define the *same* entry differently, the app refuses to guess which value is correct and requires a manual decision.
- **descVersion compatibility checking is a relative heuristic** (compared against the rest of your library), not a verified game-version-to-descVersion mapping table, since no such official table could be confirmed.

---

## Roadmap

- Reintroduce mod downloading/update-tracking once ModHub's terms are clarified (the `IModSource` abstraction and a `ManualModSource` stub already exist for this)
- Expand the log-analyzer pattern library as more real-world log examples are collected
- Investigate whether specific vehicle configuration data (colors, wheels) can be enumerated reliably enough to show in the object detail view
- Cross-platform shell (Avalonia) if/when Linux/Proton support becomes a priority — `FsModManager.Core` was kept UI-agnostic specifically to make this feasible later

---

## Contributing

Issues and PRs are welcome. If you're adding a new conflict detector or log pattern, please include the real-world log line or `modDesc.xml` excerpt that motivated it — this project tries hard to avoid guessing at Giants' XML schema without verifying against actual mod files first, and new contributions should hold the same bar.

---

## License

MIT — see [LICENSE](LICENSE).