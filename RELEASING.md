# Releasing FsModManager

FsModManager ships self-updates via [Velopack](https://velopack.io), sourced from this repo's
GitHub Releases (see `VelopackUpdateGateway`). Pushing a version tag runs
[`.github/workflows/release.yml`](.github/workflows/release.yml), which builds, tests, packages,
and publishes the release automatically. The steps below are everything a human needs to do.

## Steps

1. **Bump the version.** Edit `<Version>` in
   [`src/FsModManager.App/FsModManager.App.csproj`](src/FsModManager.App/FsModManager.App.csproj)
   following semver (MAJOR = breaking change to stored user data, MINOR = new feature,
   PATCH = bug fix only).
2. **Update the changelog.** In [`CHANGELOG.md`](CHANGELOG.md), move the entries currently under
   `## [Unreleased]` into a new `## [x.y.z] - yyyy-MM-dd` section (dated today), leaving
   `[Unreleased]` empty (or "Nothing yet."). Also update
   [`src/FsModManager.App/changelog.json`](src/FsModManager.App/changelog.json) to match — the
   app reads that file at runtime for its in-app "What's new" dialog, it does not parse
   `CHANGELOG.md`.
3. **Commit** the version bump and changelog updates.
4. **Tag** the commit with `v<version>`, matching the csproj exactly (e.g. csproj `1.2.0` →
   tag `v1.2.0`). Use a `-suffix` (e.g. `v1.2.0-beta`) to mark a prerelease.
5. **Push the tag**: `git push origin v<version>`.

That's it — pushing the tag triggers the release workflow, which:

- Verifies the tag's version matches the csproj's `<Version>` (fails fast if you forgot step 1).
- Restores and runs the full test suite (`dotnet test FsModManager.sln`); a failing test suite
  blocks the release.
- Publishes a self-contained `win-x64` build.
- Packages it into a Velopack release with `vpk pack`.
- Uploads it to this repo's GitHub Releases with `vpk upload github`, using the automatically
  provided `GITHUB_TOKEN` (no extra secret to configure).
- Uses `src/FsModManager.App/Assets/FSModManager.ico` for both the Windows executable and the
  Velopack package. Keep that generated ICO in sync with the editable
  `src/FsModManager.App/Assets/FSModManagerIcon.png` artwork when replacing the app icon.
- Marks the release as a prerelease automatically if the tag name contains a `-` (e.g.
  `v1.2.0-beta`), otherwise publishes it as a full release.
- Prints the release URL in the workflow's job summary so you can quickly verify it.

## Verifying

Once the workflow finishes, open the printed release URL and confirm the release assets and
notes look right. Existing installs will pick up the update on their next check (or via
Settings → About → "Check for updates now").
