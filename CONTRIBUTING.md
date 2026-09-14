# Contributing to Couchtop

Thanks for helping! Couchtop is in a very early beta, so bug reports from real hardware and setups are especially valuable.

## Build and test

Requirements: Windows 10 1809+ and the .NET 10 SDK.

```bash
dotnet build Couchtop.slnx
```

```bash
dotnet test tests/Couchtop.Tests/Couchtop.Tests.csproj
```

Useful switches for `Couchtop.exe`:

| Switch | Purpose |
|---|---|
| `--render-snapshot DIR --size 1920x1080` | Render every screen to PNG files without showing a window |
| `--smoke-test SECONDS` | Start normally, log startup metrics, exit cleanly |
| `--export-audio DIR` | Write the synthesized sounds and music to WAV files |

Set `COUCHTOP_DATA_DIR` to a scratch folder to keep your own channels and settings untouched while developing.

## Ground rules

1. **Never leave a user at a black screen.** Any change to the Guardian, shell mode, Setup or recovery must keep every fallback path working, and needs tests.
2. **Never write to `HKLM`.** Shell mode is strictly per-user. Tests must only use `HKCU\Software\Couchtop\SafetyTest` sandboxes, and must never touch the real `Winlogon\Shell` value.
3. **Test shell mode in a VM.** Do not enable shell mode on your daily machine while developing.
4. **Original assets only.** Don't add artwork, sounds, music, fonts or names from Nintendo or any other console maker. Couchtop is inspired by couch-console menus but is not affiliated with any of them.
5. **No telemetry.** Couchtop never phones home.

## Releases

Versions stay on `0.x` until the first stable release, e.g. `0.1.0-beta.1`.
To publish, bump `VersionPrefix`/`VersionSuffix` in `Directory.Build.props`, update `CHANGELOG.md`, then push a matching tag (`v0.1.0-beta.2`).
The release workflow builds, tests, packages and creates a GitHub pre-release.

## Pull requests

Keep PRs focused, describe how you tested them, and fill in the checklist in the PR template.
