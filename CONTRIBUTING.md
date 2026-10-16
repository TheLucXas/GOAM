# Contributing to GOAM

Thanks for helping! GOAM is a small project; the goal is to keep it simple, safe and easy to maintain.

## Ground rules

- GOAM is an **unofficial** fan tool. Do not add anything that touches game memory, bypasses protections,
  or sends data anywhere. No telemetry, ever.
- Windows only. .NET 8 (LTS).
- Never commit songs, binaries, credentials or personal paths. The repository history is public.

## Getting started

```powershell
git clone https://github.com/TheLucXas/GOAM.git
cd GOAM
dotnet build GOAM.sln -c Release
dotnet test tests/GOAM.Core.Tests -c Release
```

Run the app against throw-away folders so it never touches your real settings or game:

```powershell
$env:GOAM_DATA_DIR  = "$PWD\.sandbox\data"
$env:GOAM_TOOLS_DIR = "$PWD\.sandbox\tools"
$env:GOAM_STEAM_DIR = "$PWD\.sandbox\steam"   # optional: pretend Steam lives here
dotnet run --project src/GOAM.App -c Release
```

With `GOAM_DATA_DIR` set, "Start with Windows" writes a text file instead of the registry Run key.

## Layout

| Path | Purpose |
|---|---|
| `src/GOAM.Core` | Everything that is not UI: audio, config, relay, cfg generation, yt-dlp, i18n |
| `src/GOAM.App` | Windows Forms UI, tray, single instance |
| `tests/GOAM.Core.Tests` | xUnit tests (`tests/assets` holds tiny synthetic tone files) |
| `tests/ui` | PowerShell + UI Automation smoke tests that drive the real window (manual, see its README) |
| `docs/` | Architecture, feature parity with 1.x, performance measurements |
| `tools/bench` | Scripts used to produce `docs/PERFORMANCE.md` |

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before making structural changes.

## Pull requests

1. Branch from `develop`.
2. Keep changes focused; small commits with a message that says *what* and *why*.
3. Add or update tests for behaviour changes in `GOAM.Core`; for UI changes run the scripts in `tests/ui`.
4. `dotnet build` must be warning-free (CI treats warnings as errors) and `dotnet test` must pass.
5. Update `CHANGELOG.md` under **Unreleased** for anything a user would notice.
6. Do not bump the version in a feature PR; releases are cut by the maintainer (`Directory.Build.props`).

### Code style

`.editorconfig` is authoritative (file-scoped namespaces, braces, 4 spaces, LF line endings).
Prefer clear names over comments; comment the *why* when a rule comes from the game or Windows.
Everything that shells out uses `ProcessStartInfo.ArgumentList`, never string concatenation, and every
file operation on a user-supplied name goes through `SongNames.ResolveInside`.

## Translations

Strings live in `src/GOAM.Core/Localization/Localizer.cs` (`es`, `en`). To add a language:

1. Copy the `en` table, translate the values (keep `{0}` placeholders and `\n`).
2. Keep keys that start with `cfg_` ASCII-only: they are written into CS:GO `.cfg` files.
3. Register the code in `Localizer.Normalize` and add it to `SupportedLanguages`.
4. The `EveryEnglishStringExistsInSpanishAndViceVersa` test shows what is missing.

Or open a *Translation* issue and paste your translation; we will wire it up.

## Releasing (maintainers)

1. Update `Version` in `Directory.Build.props` and move **Unreleased** items in `CHANGELOG.md`.
2. Merge `develop` into `main`.
3. Tag `vX.Y.Z` and push the tag. The *Release* workflow builds, tests, and publishes
   `GOAM.exe` + `SHA256SUMS.txt` to a GitHub Release.

## Reporting bugs

Use the *Bug report* template and paste **Options → Copy report**. For security issues see
[SECURITY.md](SECURITY.md).
