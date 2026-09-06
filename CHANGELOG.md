# Changelog

All notable changes to FsModManager are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/): **MAJOR** for breaking changes to how the
app stores/reads user data (savegame backups, manifests, settings files, etc. - anything that
could break compatibility with a previous version's saved state), **MINOR** for new features,
**PATCH** for bug fixes.

## [Unreleased]

### Releasing a new version

See [RELEASING.md](RELEASING.md) for the full walkthrough. Short version:

1. Bump `<Version>` in `src/FsModManager.App/FsModManager.App.csproj`.
2. Move this "Unreleased" section's entries into a new `## [x.y.z] - yyyy-MM-dd` section above.
3. Commit, then tag with `v<version>` (e.g. `v1.2.0`) and push the tag.
4. The [`.github/workflows/release.yml`](.github/workflows/release.yml) workflow takes it from
   there: builds, tests, packages with Velopack (`vpk`), and publishes a GitHub Release that the
   app's self-updater checks against.

## [1.1.2] - 2026-09-06

### Added

- A Farming Simulator-themed application icon, used consistently by the Windows executable,
  main window, and Velopack installer package.

## [1.1.1] - 2026-09-06

### Fixed

- Hardened the release workflow by passing its GitHub token through the upload step environment
  instead of expanding the secret in a shell command, granted it release-publishing permission,
  and added the Windows global .NET tools folder to `PATH` so `vpk` can run in later Bash steps.
- Await application-exit log flushing and asynchronous mod-archive edits so background I/O can
  finish without blocking asynchronous operations.
- Simplified duplicate-mod keeper exclusion to use the model's value equality consistently.
- Applied bounded one-second timeouts to regular expressions that process log, mod-source, and
  community-editable error-pattern input, preventing pathological input from blocking analysis.
- Use Explorer's absolute system path for opening backup and mod folders instead of resolving an
  executable through `PATH`.
- Removed unused Clean Test and Settings view-model dependencies/placeholders, and consolidated
  the mod editor's shared default edit reason.
- Simplified Clean Test, conflict-summary, and Diagnostics refresh control flow while preserving
  existing user-facing states and recovery behavior.
- Split log-analysis, mod-file edit, and mod descriptor parsing work into focused helpers while
  retaining existing transaction safeguards, metadata values, and malformed-input warnings.
- Renamed the internal Win32 rectangle interop type to follow .NET naming conventions while
  preserving its native layout.
- Grouped adjacent multiplayer manifest overloads for clearer maintenance.

## [1.1.0] - 2026-09-06

### Added

- Self-updates via Velopack, sourced from this repository's GitHub Releases:
  - Once-per-session background update check on startup; nothing ever downloads or installs
    without an explicit "Update now" confirmation.
  - Non-intrusive actionable notification banner (not a blocking dialog) showing the new version
    and a short changelog summary - taken from the published GitHub release notes when present,
    falling back to the bundled changelog.
  - Download progress (0-100%) shown live in the banner, then apply-and-restart on confirmation.
  - Manual "Check for updates now" action in Settings -> About.
  - Update checks no-op entirely on non-installed builds (dotnet run / raw publish output), so
    development runs never try to self-update.
  - Applying an update is deferred while a Clean Test session is active or a mod-file edit is in
    progress, so a restart can never interrupt a risky operation.
  - Failed update checks (offline, GitHub rate limit) are swallowed and never block or crash the app.

## [1.0.0] - 2026-09-06

Initial feature-complete release. The project's git history doesn't have granular per-feature
commits to reconstruct an accurate retroactive version timeline from, so everything built so far
is grouped into this single starting release.

### Added

- Game installation detection (Steam, Epic, GOG, manual folder browse) and load-order/version reading.
- Mod list scanning of the mods folder with search, category filters, multiplayer/crossplay
  indicators, and per-mod content expansion (vehicles/placeables/specs).
- Conflict detection: duplicate internal names, duplicate store items, duplicate custom types,
  duplicate specialization names, Lua global function collisions, and shared-data-file overrides -
  plus a dedicated Conflicts overview tab.
- Custom application shell: borderless window chrome, dark theme, sidebar navigation, dismissible
  notification banners, per-monitor DPI-aware resizing/maximizing.
- Load order management per savegame, with drag-and-drop reordering and a running-game guard
  before saving.
- Log Analyzer: parses the game's log.txt, attributes errors/warnings to mods, matches known error
  patterns, and flags entries that correlate with detected conflicts.
- Diagnostics / health checks: OneDrive sync detection, mod filename validation, descVersion
  compatibility heuristic, and duplicate/stale mod version detection with a guided keeper recommendation.
- Savegame backups: automatic and manual snapshots before load-order changes, with configurable
  retention and one-click restore.
- Multiplayer manifest sync: export a signed mod-list manifest and compare it against a friend's
  to spot missing/extra/mismatched mods.
- Clean Test Mode: move mods out (and optionally clear the shader cache) to test a clean setup,
  plus a guided bisection wizard to narrow down a problem mod.
- Conflict and log auto-fixing: automatic storeItem rename resolution and shared-data-file merge
  fixes for supported conflict types.
- Versioning and changelog system (this file, `changelog.json`, and an in-app About/changelog viewer).
