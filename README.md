# FsModManager

FsModManager is a Windows desktop utility for Farming Simulator 25 that detects installed game copies, scans mods, surfaces conflicts, and allows users to review and reorder load order before launching the game.

## What is included

- WPF desktop shell with a custom borderless chrome window
- Dark-mode styling with hand-built WPF templates and no third-party UI library
- Install selection flow for Steam/Epic/GOG/manual paths
- Mod scanning and conflict analysis across the selected mods folder
- Savegame discovery and mod order editing for `mods.xml`
- Launch guard to prevent starting the game while it is already running
- Banner notifications for warnings and errors instead of blocking `MessageBox` popups

## Solution layout

```text
FsModManager.sln
README.md
src/
  FsModManager.App/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    Controls/
    Themes/
    ViewModels/
    Views/
  FsModManager.Core/
    Detection/
    Launching/
    ModScanning/
    Savegames/
    Models/
    Data/
tests/
  FsModManager.Core.Tests/
```

## App architecture

The application is split between a .NET 8 WPF front end and a reusable Core library.

### App shell

The WPF app wires together the main services through dependency injection and exposes a three-step user flow:

1. Install Selection
   - uses `CompositeGameInstallDetector` to find installed copies
   - supports browsing to a manual installation path
   - selects the game install and continues to the mod list

2. Mod List
   - scans the selected mods folder via `ModDirectoryScanner`
   - parses `modDesc.xml` contents through `ModDescParser`
   - runs the conflict pipeline to highlight critical, likely, and possible issues

3. Load Order
   - discovers savegames and lets the user pick one
   - loads the current load order from `mods.xml`
   - reorders entries with drag-and-drop and toggles active/inactive state
   - saves only when the game is not currently running
   - launches the game through `GameLauncher`

### Core responsibilities

- `Detection` locates game installs and resolves the effective mods folder
- `ModScanning` reads mod archives and extracts metadata
- `Conflicts` detects mod collisions and compatibility risks
- `Savegames` reads and writes load order information for specific saves
- `Launching` guards the game process and starts the executable safely

## Supported runtime

- Windows only
- .NET 8 SDK required
- WPF desktop app targeting `net8.0-windows`

## Development workflow

From the repository root:

```bash
dotnet restore
dotnet build FsModManager.sln
dotnet test FsModManager.sln
```

Run the desktop app locally:

```bash
dotnet run --project src/FsModManager.App
```

## Notes and caveats

- `mods.xml` handling is implemented as a savegame-level editor and writer, but the precise runtime effect of ordering within Farming Simulator 25 should be validated against real game behavior when testing with actual save data.
- `GameLauncher` prevents duplicate launches by checking whether the game process is already running.
- The app intentionally surfaces warnings and errors as in-app notifications rather than blocking modal dialogs.
- This project is intended as an application scaffold and assistant-driven workflow tool; it is not a replacement for a vendor-managed or official mod-management platform.
