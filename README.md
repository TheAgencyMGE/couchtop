<div align="center">

<img src="assets/Couchtop.png" width="128" alt="Couchtop icon" />

# Couchtop

**Turn your Windows PC into a couch console.**

A full-screen, controller-friendly front end for Windows that comes in three shells — a channel grid, a blade
dashboard and a media bar — with an optional (and very carefully guarded) shell mode.

[![CI](https://github.com/TheAgencyMGE/couchtop/actions/workflows/ci.yml/badge.svg)](https://github.com/TheAgencyMGE/couchtop/actions/workflows/ci.yml)
[![Pre-release](https://img.shields.io/github/v/release/TheAgencyMGE/couchtop?include_prereleases&label=beta&color=35B4E5)](https://github.com/TheAgencyMGE/couchtop/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)](#requirements)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

[**Download the beta**](https://github.com/TheAgencyMGE/couchtop/releases) · [**Watch the trailer**](https://theagencymge.github.io/couchtop/) · [The three shells](#the-three-shells) · [Screenshots](#screenshots) · [Recovery](#recovery-getting-back-to-explorer)

<a href="https://theagencymge.github.io/couchtop/">
  <img src="docs/media/couchtop-trailer.gif" width="860" alt="Couchtop trailer preview. Click to watch the full trailer with sound." />
</a>

<sub>Click the preview to watch the full trailer with sound.</sub>

</div>

> [!WARNING]
> **Couchtop is a very early beta (`0.7.0-beta.1`).** Expect rough edges and breaking changes.
> Launcher mode is safe to try on any PC. **Only try shell mode inside a virtual machine** until it has been tested on more hardware.

---

## Contents

- [The three shells](#the-three-shells)
- [Screenshots](#screenshots)
- [Features](#features)
- [Requirements](#requirements)
- [Install](#install)
- [Using Couchtop](#using-couchtop)
- [Shell mode (replacing Explorer)](#shell-mode-replacing-explorer)
- [Recovery: getting back to Explorer](#recovery-getting-back-to-explorer)
- [Testing in a virtual machine](#testing-in-a-virtual-machine)
- [Uninstall](#uninstall)
- [Build from source](#build-from-source)
- [Architecture](#architecture)
- [Automated tests](#automated-tests)
- [Performance](#performance)
- [Privacy](#privacy)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [License and trademarks](#license-and-trademarks)

---

## The three shells

Couchtop ships as three complete shells. A shell is not a skin: each one has its own look on **every** screen,
its own icons, its own sounds, its own pointer and its own way of moving around. Switch between them live in
**Settings › Display › Menu style** — no restart, and your apps, games, files and settings come with you.

| | | |
|---|---|---|
| ![Channels shell](docs/screenshots/01-menu.png) | ![Dashboard shell](docs/screenshots/20-dashboard-home.png) | ![Media Bar shell](docs/screenshots/24-mediabar-home.png) |
| **Channels** | **Dashboard** | **Media Bar** |
| Pages of glossy tiles, a tilting hand pointer, eight themes, menu music and [Pals](#features). | Graphite and green, angled blade tabs, a row of app tiles, a player card and dry mechanical clicks. | Deep blue, a row of categories crossed by a column of items, thin outlined icons and soft blips. |

Apps show their real Windows icons in the Dashboard and Media Bar; Couchtop's own screens get a flat geometric
mark drawn for each shell. Themes, the hand pointer, menu music and Pals belong to the Channels shell alone.
Both new designs are original — no console assets, artwork or names are used.

| | |
|---|---|
| ![Dashboard system blade](docs/screenshots/21-dashboard-system.png) **Dashboard**: blades group your apps, games, media and system | ![Dashboard settings](docs/screenshots/22-dashboard-settings.png) **Dashboard**: every screen follows the shell |
| ![Media Bar system](docs/screenshots/25-mediabar-system.png) **Media Bar**: categories crossed by items | ![Media Bar files](docs/screenshots/26-mediabar-files.png) **Media Bar**: the file manager in shell colours |
| ![Welcome tour](docs/screenshots/28-welcome-tour.png) **Welcome tour**: pick a shell on first run | ![Media Bar Quick Menu](docs/screenshots/27-mediabar-quick.png) **Quick Menu** in the Media Bar shell |

---

## Screenshots

The Channels shell, in its default theme.

| | |
|---|---|
| ![Channel menu](docs/screenshots/01-menu.png) **Channel menu** with a tilting hand pointer | ![Channel start screen](docs/screenshots/03-preview.png) **Channel start screen** before launching an app |
| ![Customize channels](docs/screenshots/02-customize.png) **Customize**: drag, rename, recolor, add and remove | ![Quick Menu](docs/screenshots/11-home-menu.png) **Quick Menu** over any running app |
| ![Night theme](docs/screenshots/12-night.png) **Night theme** | ![Settings](docs/screenshots/04-settings.png) **Settings** |
| ![Shell mode](docs/screenshots/05-shell-mode.png) **Shell mode** controls and status | ![Shell Safety Test](docs/screenshots/06-safety-test.png) **Shell Safety Test** before shell mode unlocks |
| ![Power](docs/screenshots/09-power.png) **Power** channel | ![Dialog](docs/screenshots/10-dialog.png) Console-style dialogs |
| ![Files](docs/screenshots/07-files.png) **Files** channel | ![Photos](docs/screenshots/08-photos.png) **Photos** channel |

### Themes (Channels shell)

| | |
|---|---|
| ![Sky Resort theme](docs/screenshots/13-theme-sky-resort.png) **Sky Resort**: glass, sky, sea and bubbles | ![Neon City theme](docs/screenshots/14-theme-neon-city.png) **Neon City**: black steel and neon HUD |
| ![Midnight theme](docs/screenshots/15-theme-midnight.png) **Midnight**: sleek and dark | ![Sakura theme](docs/screenshots/16-theme-sakura.png) **Sakura**: blossoms and falling petals |
| ![Sunset theme](docs/screenshots/17-theme-sunset.png) **Sunset**: golden hour over the water | ![Night theme](docs/screenshots/12-night.png) **Night**: the dim classic |

The Dashboard and Media Bar do not use themes: each has one fixed look of its own.

---

## Features

**First run**
- A **welcome tour** for new installs: what Couchtop is, a side-by-side pick of the three shells (choosing one switches the app there and then), what it already found on your PC, what is built in, how to drive it with a mouse, controller or Wii Remote, and how to get back to Windows. Take it again any time from Settings › About.

**The menu**
- **Three shells**, switched live in Settings › Display with no restart. Each one is a complete look, not a skin: its own colours on every screen, its own flat icon set, its own sounds, its own pointer and its own navigation.
  - **Channels** — the Couchtop grid: channel tiles, eight themes, the hand pointer, menu music and Pals.
  - **Dashboard** — graphite and green, angled blade tabs, a row of app tiles, dry mechanical clicks. No themes, no Pals, no music.
  - **Media Bar** — deep blue, a row of categories crossed by a column of items, thin outlined icons, soft sine blips, a background that shifts with the month and the hour. No themes, no Pals, no music.
- Your apps, games, files, channels and settings are shared by all three shells; nothing else is.
- Full-screen 4×3 channel grid with as many pages as you need, page arrows, mouse-wheel and keyboard paging.
- Glossy tiles with a blue hover glow, a gentle wobble, idle artwork animations and occasional shine sweeps.
- A tilting hand pointer that leans as you move it, plus a channel-name bubble.
- With the Couchtop Bar shown, Explorer's taskbar auto-hides so there are not two bars along the bottom, and is restored when the Couchtop Bar goes away (Settings › Desktop).
- Curved bottom bar with a big clock (blinking colon), the date, a menu button and a **Message Board**.
- A channel start screen with **Menu** and **Start** buttons, zoom transitions and a launch flash.
- Original synthesized sound effects, startup jingle and a music-box menu loop. The music plays only while the menu is in front.
- Eight themes that restyle everything, including the pointer and scenery: Classic, Night, Sky Resort, Neon City, Midnight, Sakura, Sunset and High Contrast. Plus reduced motion, larger text and a 12/24-hour clock.

**Channels**
- Automatically discovers Start menu programs, Microsoft Store apps, **Steam games** (all libraries, with header art), **Epic Games** titles and Windows tools.
- Filters out uninstallers, readmes and broken shortcuts; newly installed apps are added automatically.
- Channels launch the real apps: shortcuts, executables, packaged apps (AUMID), `steam://` and other protocols.
- If an app moved after an update, Couchtop re-discovers it automatically. If it's gone, you're offered **Locate…** or **Remove Channel**.
- **Customize** mode: drag channels between slots and pages, add (installed app, program, website, folder), rename, recolor, set a custom picture, run as administrator, or remove.
- Built-in channels: **Files**, **Photos** (with slideshow), **Web** (WebView2 browser), **Pals**, **Sports**, **Settings**, **Power** and **Customize**.

**Pals**
- Make a **Pal**, a 3D character of your own, in the Pals channel: body proportions, skin, face shape, eyes (style, color, size, spacing, height), brows, nose, mouth, cheeks, facial hair, 14 hairstyles with dyed tips, tops with prints, bottoms, shoes, hats, glasses, scarves, backpacks, earrings, a name and a personality.
- Your Pal lives on the home screen: it strolls along the bottom bar, watches the pointer, looks up at tiles you hover, sits down when things are quiet and naps if you leave it alone. Poke it, or pick it up and drop it somewhere else.
- It reacts to what you do (launching, closing and switching apps, coming back to Couchtop, themes, low battery) with gestures and over 160 handwritten lines, chosen by rules rather than generated. It waits by the Start button, sees you off on the Power screen, peeks in on other Couchtop screens and speaks up in the Couchtop Bar while you use other apps.
- **Take it out onto your desktop** (right-click your Pal › *Take me to the desktop*, or Settings › Pals): it walks along the taskbar, hops up onto your app windows, rides along when you drag one, and grumbles when you close the one it was standing on. Pick it up and drop it anywhere; everything around it clicks straight through to your apps.
- It never talks over full-screen games or videos, spaces out its remarks, and can be set to chatty, now and then, quiet or never talking (Settings › Pals).

**A desktop, not just a launcher**
- Turn on **Shell Mode** (Settings › Shell Mode, after the Safety Test) and Couchtop becomes the desktop: it replaces Explorer's desktop, taskbar and Start menu while Windows keeps running underneath. It is never turned on for you.
- **Couchtop Bar:** your open apps grouped per program, the Couchtop button, search, the notification area, volume/battery/Wi-Fi, clock and power. It keeps maximized windows clear of itself and hides for full-screen games.
- **Task switcher** (`Ctrl + Alt + W`), **search everything** (`Ctrl + Alt + Space`) across channels, apps, windows, settings and files, and **clear the screen** (`Ctrl + Alt + D`).
- **Status center** for volume, battery, Wi-Fi and notifications, with one click through to the Windows panels for Wi-Fi, Bluetooth, display, sound, power and accessibility.
- **Window management** from the bar: show, minimize, maximize, snap left/right, move to the next screen, close.
- **Background apps keep working**: sync clients, chat apps and driver utilities get a real notification area even with Explorer gone.
- You can also show the bar in launcher mode (Settings › Desktop › Couchtop Bar › Always).

**Files**
- Tabs and a split view, so you can drag a folder open on each side and copy between them.
- Search inside a folder and everything under it, sort, details or grid view, and hidden files.
- Copy, cut, paste, rename, new folder, delete to the Recycle Bin, zip and unzip, and a properties panel with size, dates, owner, attributes and shortcut targets.
- Drives with free space, USB sticks and discs, network folders, and the Recycle Bin.

**Your own music and sounds**
- Replace the menu music with any song you like: **Settings › Sound › Menu music › Choose File…**. It loops while the menu is in front.
- Replace any menu sound (startup, point, select, back, page turn, start, Quick Menu, tick, error) with your own audio.
- MP3, WAV, M4A, AAC, WMA and FLAC all work. Sounds longer than 5 seconds are cut short.
- Couchtop keeps its own copy of each file in its data folder, so moving or deleting the original is fine, and one button puts the built-in sounds back.

**Couchtop Sports (coming soon)**
- Five quick 3D sports built into Couchtop: **Tennis**, **Baseball**, **Bowling**, **Golf** and **Boxing**, with easy controls, training challenges and local multiplayer.
- The Sports channel is on the menu as a sneak peek. It is **still being made and isn't playable yet**.

**Everywhere**
- **Quick Menu** overlay on top of any running app: back to the menu, close the app, switch windows, volume, controller status, Settings, Power.
- Input: mouse, keyboard, Xbox-compatible controllers (XInput), and Wii Remote controllers over Bluetooth HID (IR pointer with a sensor bar).
- Multi-monitor: choose the menu screen; other screens get a calm backdrop. Per-monitor DPI v2, and the layout re-applies after display, DPI and resolution changes and after sleep/wake.
- Low idle usage: animations, music and controller polling back off whenever another app is in front.

---

## Requirements

- Windows 10 1809 (build 17763) or newer, x64. Windows 11 is recommended. Windows in S mode is not supported.
- The release package is self-contained, so no .NET install is needed.
- Web channel: the Microsoft Edge WebView2 Runtime (preinstalled on Windows 11).

---

## Install

There are two downloads on [Releases](https://github.com/TheAgencyMGE/couchtop/releases). Both are self-contained.

| Download | Use it when |
|---|---|
| `Couchtop-<version>-win-x64.zip` | You want Start menu shortcuts, start at sign-in, or shell mode. Run `Couchtop.Setup.exe`. |
| `Couchtop-<version>-win-x64-portable.zip` | You just want to try it, or carry it on a USB drive. Nothing is installed. |

### Portable

Extract the portable zip anywhere and run **`Couchtop.exe`**. The `portable.txt` file next to it tells Couchtop to keep
your channels, settings, cache and logs in a `Data` folder beside the exe, so nothing is written to `%LOCALAPPDATA%`
and no shortcuts or registry entries are created. Shell mode is blocked in portable copies. To remove it, close Couchtop
and delete the folder. To update, copy the new files over the old ones and keep your `Data` folder.

### Installed

1. Download `Couchtop-<version>-win-x64.zip` and extract it anywhere.
2. Run **`Couchtop.Setup.exe`** and click **Install**.

Setup installs **per user, without administrator rights**:

| What | Where |
|---|---|
| Program files | `%LOCALAPPDATA%\Programs\Couchtop` |
| Start menu shortcuts | `Couchtop`, `Couchtop Recovery`, `Uninstall Couchtop` |
| Apps & features entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Couchtop` |
| Your channels, settings and logs | `%LOCALAPPDATA%\Couchtop` |

Options: desktop shortcut, *start at sign-in* (launcher mode on top of Explorer), and *open when finished*.
Setup **never** turns on shell mode.

> [!NOTE]
> **"Windows protected your PC"?** The beta isn't code-signed yet, so SmartScreen may warn you the first time you run Setup. Click **More info → Run anyway**.
> Some antivirus tools are also cautious about apps that can replace the Windows shell. Couchtop is open source; you can read every line or [build it yourself](#build-from-source).

Silent install: `Couchtop.Setup.exe --install --quiet [--dir PATH] [--no-desktop-shortcut] [--start-at-sign-in] [--no-launch]`

Updating: run the new package's Setup. It closes Couchtop, swaps the files atomically (rolling back on
failure), and restarts it. Updating changes the file hashes, so the Shell Safety Test must be passed again
before shell mode can be re-enabled.

---

## Using Couchtop

| Action | Mouse | Keyboard | Controller | Wii Remote |
|---|---|---|---|---|
| Point / move | move | arrow keys | left stick | point at sensor bar / D-pad |
| Select | click | Enter | A | A |
| Back | right-click / X1 | Esc | B | B or 2 |
| Change page (Channels) | arrows / wheel | Page Up / Down | LB / RB | − / + |
| Change blade / category | click a tab or icon | ↑ ↓ (Dashboard), ← → (Media Bar), Page Up / Down | LB / RB | − / + |
| Quick Menu | — | `Ctrl+Alt+Home` (configurable) | Guide or Back+Start | HOME |
| Windows desktop | **Desktop** button on the menu | Quick Menu › Windows Desktop | Quick Menu › Windows Desktop | Quick Menu › Windows Desktop |
| Emergency exit | — | `Ctrl+Alt+Shift+F12` | — | — |

**Wii Remote pairing:** open Windows *Settings › Bluetooth & devices › Add device › Bluetooth* and press **1+2**
(or the red SYNC button) on the remote. If a PIN is requested, leave it empty. Pointing needs an IR sensor bar;
without one, the D-pad moves the pointer.

**Launcher mode vs. shell mode**

- *Launcher mode* (default): Couchtop runs as a normal full-screen app on top of Explorer.
- *Shell mode*: Couchtop starts **instead of** Explorer when you sign in. See below.

---

## Shell mode (replacing Explorer)

### How it works

- **Per-user mechanism on every supported edition** (Home, Pro, Education, Enterprise, IoT). Couchtop writes only the per-user value `HKCU\Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`.
- **HKLM is never modified.** Other Windows accounts are unaffected. If you already had a per-user shell, it is saved and restored when you turn shell mode off.
- **Enterprise, Education and IoT editions** also offer Shell Launcher v2. Couchtop detects this but deliberately still uses the per-user value, because Explorer fallback can't be guaranteed under Shell Launcher and it needs admin rights.
- **Group Policy.** If the *Custom User Interface* policy is set, it overrides per-user shells. The compatibility check blocks enabling in that case.

The Winlogon value starts **`Couchtop.Guardian.exe`**, a tiny watchdog with no UI framework, which then starts
the menu. By default the value uses a *resilient bootstrap* built only from Windows components:

```
cmd.exe /d /c if exist "<Guardian>" (start "" "<Guardian>" --shell) else (reg.exe delete HKCU\...\Winlogon /v Shell /f & start "" explorer.exe)
```

If Couchtop's folder is ever deleted without uninstalling, the next sign-in removes the override and starts
Explorer. A *Direct* bootstrap (no brief console flash) is available in Settings.

### Turning it on

Settings › **Shell Mode** walks you through three steps:

1. **Compatibility check.** It covers:
   - Windows version and edition, S mode and Safe Mode.
   - Group Policy shell override and the machine default shell.
   - SYSTEM or elevated account.
   - Guardian, Recovery tool and recovery script present, on a fixed local drive, in a stable install location.
   - Explorer, cmd and reg available; per-user Winlogon key writable; Remote Desktop session.
2. **Shell Safety Test.** Every item must pass:
   - The recovery tool verifies its restore logic in a registry sandbox.
   - The emergency `Recover-Explorer.cmd` script is present.
   - The shell setting can be written and restored (sandboxed).
   - **Crash rehearsal:** the real Guardian starts the real app in crash mode three times and must fall back to Explorer and disable shell mode.
   - **Freeze rehearsal:** a hung app must be detected, killed and fallen back from.
   - **Boot-loop rehearsal:** two failed sign-ins must go straight to Explorer.
   - **You press `Ctrl+Alt+Shift+F12`** to prove the emergency shortcut works on your keyboard.
   - Couchtop has been used and exited cleanly in normal mode at least once.
3. **Enable Shell Mode.** This is refused, both in the UI and inside `ShellModeManager` itself, unless the compatibility check has no blocking items **and** a passing Safety Test record exists. That record must be under 14 days old, from the same install folder, and match the current SHA-256 hashes of `Couchtop.exe`, the Guardian and the Recovery tool. A missing or corrupted record counts as "not verified".

The change applies at your next sign-in. **Disable Shell Mode** is never gated.

### Safety net while running as the shell

| Situation | What happens |
|---|---|
| `Ctrl+Alt+Shift+F12` | Held by the Guardian, so it works even if the UI is frozen. Starts Explorer, turns shell mode off for the next sign-in, and closes Couchtop. |
| Couchtop crashes | Restarted; on the 3rd crash within 5 minutes → Explorer + shell mode off + a message explaining why. |
| Couchtop freezes | The UI thread sends a heartbeat every 2 s; if it stops, the Guardian kills it and treats it as a crash. |
| Never becomes ready | Killed after 90 s and counted as a crash. |
| Boot loop / power loss during sign-in | Each sign-in increments a counter that only a healthy UI clears. At the 3rd consecutive attempt the Guardian goes straight to Explorer. |
| App files missing | Explorer + shell mode off (and the resilient bootstrap covers a missing Guardian). |
| Guardian itself fails | A last-resort handler starts Explorer and turns shell mode off. |
| Sign-out / shutdown | Treated as a normal end of session, never as a crash. |
| Sleep / wake | Heartbeat timers are reset on resume, so the gap is not treated as a hang. |
| No tray or taskbar | The Quick Menu lists open windows, and **Windows Desktop** starts Explorer on demand. Startup-folder and Run-key apps are launched for you. |
| Corrupted settings | Loaded from the last good backup, or defaults; the bad file is kept as `*.corrupt-*`. |

---

## Recovery: getting back to Explorer

Any of these work, in order of convenience:

1. **Press `Ctrl+Alt+Shift+F12`.**
2. **Quick Menu › Windows Desktop**, or **Power › Windows Desktop**.
3. **Start menu › Couchtop Recovery** (or run `Couchtop.Recovery.exe`). A standalone tool that never loads the Couchtop UI. It stops Couchtop, removes the shell setting, restores any previous per-user shell and starts Explorer. Useful switches:
   - `--restore --quiet`: no prompts.
   - `--force`: also remove a non-Couchtop per-user shell.
   - `--status`: show the current configuration.
   - `--verify`: run the sandboxed self-test.
4. **`Recover-Explorer.cmd`** in the install folder. It needs nothing but Windows (`taskkill`, `reg`, `explorer`).
5. **Ctrl+Alt+Del › Task Manager › Run new task:**
   - `explorer.exe` gets you a desktop right now.
   - `%LOCALAPPDATA%\Programs\Couchtop\Recover-Explorer.cmd` fixes the next sign-in.
6. **From another admin account or Safe Mode:** load the affected user's `NTUSER.DAT` in `regedit` and delete `Software\Microsoft\Windows NT\CurrentVersion\Winlogon\Shell`.

---

## Testing in a virtual machine

Always try shell mode in a VM before using it on your main PC.

1. Create a Windows 11 VM (Hyper-V *Quick Create*, VirtualBox or VMware) and **take a snapshot/checkpoint**.
2. Copy the release zip into the VM, extract it, run Setup.
3. Use Couchtop in launcher mode. Launch a few apps, customize channels, then **Power › Exit Couchtop**.
4. Settings › Shell Mode › **Run Check**, then **Start Test**. All nine steps must turn green.
5. **Enable Shell Mode**, then sign out and back in. Couchtop should appear instead of the taskbar.
6. Exercise the safety net:
   - **Emergency shortcut:** press `Ctrl+Alt+Shift+F12`. Explorer appears. Sign out and back in: Explorer again. Re-enable shell mode.
   - **Crash:** `taskkill /f /im Couchtop.exe` three times within a minute. Explorer appears with a message.
   - **Freeze:** suspend `Couchtop.exe` with Resource Monitor for ~2 minutes. The Guardian replaces it.
   - **Missing files:** rename the install folder, sign out and in. Explorer starts (resilient bootstrap).
   - **Sleep/wake, monitor plug/unplug, DPI change:** the menu stays on the chosen screen at the right size.
   - **Recovery tool, script and uninstall:** each should leave you with Explorer at the next sign-in.
7. Revert the VM snapshot when done.

---

## Uninstall

Use **Settings › Apps › Installed apps › Couchtop › Uninstall**, or **Start menu › Uninstall Couchtop**.

Uninstall **always restores Explorer first**:

1. Stops Couchtop and the Guardian.
2. Turns shell mode off and restores any previous per-user shell.
3. Starts Explorer if it isn't running.
4. **Verifies** the shell setting no longer points to Couchtop. If that can't be verified, uninstall stops without deleting anything, so the recovery tools stay available.

Only then does it remove, in order:
- The startup entry, shortcuts and registry entries.
- The program folder.
- Optionally (*Also delete my channels and settings*), `%LOCALAPPDATA%\Couchtop`.

Silent: `Couchtop.Setup.exe --uninstall --quiet [--purge]`

**Portable version:** close Couchtop and delete its folder. It never changes shell settings, so there is nothing else to undo.

---

## Build from source

Requires the .NET 10 SDK (`global.json` pins 10.0.100 or newer in the same feature band).

```bash
dotnet build Couchtop.slnx -c Release
```

```bash
dotnet test tests/Couchtop.Tests/Couchtop.Tests.csproj -c Release --no-build
```

Run straight from the build output (launcher mode, nothing is installed):

```bash
src/Couchtop.App/bin/Release/net10.0-windows/Couchtop.exe
```

Create the self-contained installer and portable packages in `artifacts/` (runs the tests first):

```bash
powershell -ExecutionPolicy Bypass -File build/publish.ps1
```

Pushing a tag like `v0.3.0-beta.1` runs the release workflow, which builds, tests, packages and publishes a GitHub pre-release.


---

## Architecture

```
src/
  Couchtop.Core      No UI dependency; everything testable lives here
    Storage          Atomic JSON persistence with .bak fallback and corruption quarantine
    Settings         UserSettings + normalization
    Channels         Channel model, LayoutEditor (place/move/swap/remove/normalize), ChannelSeeder
    Discovery        Start menu (.lnk/.url/.appref-ms), AppsFolder (Store), Steam (VDF), Epic, Windows tools
    Launching        AppLauncher (ShellExecute, AUMID activation, protocol URIs) + re-resolution after updates
    Shell            Edition detection, CompatibilityChecker, WinlogonUserShellMechanism, ShellModeManager (gated)
    Safety           GuardianHost + CrashPolicy + BootGuard, RecoveryService, RecoveryVerifier, SafetyGate, SessionSentinel
    Platform         Hotkeys, monitors, startup apps, power, window switching, native message window
    Input            XInput, Wii Remote HID (report parsing, IR pointer mapping)
  Couchtop.App       WPF UI (menu, tiles, pointer, views, Quick Menu, audio synth, input routing, snapshots)
  Couchtop.Guardian  Shell-mode watchdog started by Winlogon; also runs Safety Test rehearsals
  Couchtop.Recovery  Standalone restore tool + Recover-Explorer.cmd
  Couchtop.Setup     Per-user installer / uninstaller
tests/
  Couchtop.Tests           xUnit unit + integration tests
  Couchtop.FakeShellApp    Scriptable stand-in app for Guardian integration tests
```

Why WPF: mature per-monitor DPI support, GPU-composited vector animation, no packaging requirement (which a
shell replacement needs), and fast startup with ReadyToRun.

---

## Automated tests

`dotnet test` runs about 140 tests on every push. The shell tests only ever touch sandboxes under `HKCU\Software\Couchtop\SafetyTest`; several assert the real Winlogon value is unchanged afterwards.

| Area | What's covered |
|---|---|
| Settings & layout | Round trip; corrupt file → backup → defaults; value clamping; place, move and swap across pages; normalizing damaged layouts; capped first-run seeding; auto-add without re-adding dismissed apps |
| Discovery | Real `.lnk` files, including broken, garbage, uninstaller and document shortcuts; Steam multi-library VDF; Epic manifests; noise filter; de-duplication; never discovering Couchtop itself |
| Launching | Missing apps, updated apps re-resolved, UAC cancel, protocol safety, Store apps, and real launches through an `.exe` and a `.lnk` |
| Shell mode | Command builder; enable/disable/restore previous/foreign-shell handling; gate enforcement; rollback when the registry is read-only; real HKCU sandbox round trip; edition classification; every blocking compatibility condition |
| Guardian | Crash threshold and expiry; hang and never-ready detection; boot-loop guard; emergency hotkey; sign-out; sleep/resume gap; missing app; restart requests |
| Integration | The real Guardian `--rehearsal` for crash, hang, boot-loop and clean handoff; Recovery `--verify`; sandboxed Recovery `--restore`; recovery script content |
| Input & displays | Wii Remote report parsing and IR mapping, stick deadzones, hotkeys, command-line splitting, Startup Approved flags, monitor selection and change detection |

Beyond the unit tests:

- **Installer end-to-end test** (per-user; installs into a temp folder and removes everything again). Close Couchtop first.

```bash
powershell -ExecutionPolicy Bypass -File build/test-installer.ps1
```

- **Smoke test** of the real full-screen window. It logs startup time, memory and hotkey registration, then exits. Set `COUCHTOP_DATA_DIR` first to keep your own data untouched.

```bash
Couchtop.exe --smoke-test 10
```

- **Snapshots** of every screen without showing a window:

```bash
Couchtop.exe --render-snapshot snapshots --size 1920x1080
```

---

## Performance

Measured with `--smoke-test 30` on a 16-thread desktop, Debug build, 1920×1080, warm caches:

| Metric | Result |
|---|---|
| Launch to first frame | ~1.5–2.4 s (Debug, no ReadyToRun; release builds use ReadyToRun) |
| Working set with 35 channels and icons | ~175 MB |
| CPU with the menu in front and animating | ~4% of total CPU (scenery at a smooth 60 fps; hover and transitions follow your monitor's refresh rate) |
| CPU with another app in front | Idle animations, menu music, clock blinking and fast controller polling pause |

---

## Privacy

- **No telemetry, accounts, cloud sync, or network calls.** The only exception is the Web channel, which loads what you browse.
- Everything is stored locally in `%LOCALAPPDATA%\Couchtop`: settings, channels, caches and logs (size-capped, rotated).
- The Web channel uses Microsoft Edge WebView2, which follows your Windows/Edge diagnostic data settings.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| "Enable Shell Mode" says the Safety Test is required | Run it. It becomes invalid after updates, moving the install, or 14 days. |
| Safety Test: "Used in normal mode first" fails | Open Couchtop normally and exit once via Power › Exit Couchtop, then re-run. |
| Emergency shortcut step fails | Another app holds `Ctrl+Alt+Shift+F12`; close it (e.g. hotkey utilities) and re-run. |
| Quick Menu shortcut doesn't work | Choose another shortcut in Settings › Controls. |
| An app channel says it can't be found | Choose **Locate…**, or **Find New Apps** in Customize. |
| Tray icons are missing in shell mode | Expected, since there is no Explorer taskbar. Use Quick Menu › Windows Desktop when you need it. |
| Something looks wrong | Logs are in `%LOCALAPPDATA%\Couchtop\logs`. Please attach them to a [bug report](https://github.com/TheAgencyMGE/couchtop/issues/new/choose). |

---

## Contributing

Bug reports, especially from real hardware, are hugely helpful. See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and ground rules, and [SECURITY.md](SECURITY.md) for reporting vulnerabilities. Changes are tracked in [CHANGELOG.md](CHANGELOG.md).

## License and trademarks

Couchtop is released under the [MIT License](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The Dashboard and Media Bar menu styles are original designs inspired by the shapes of mid-2000s console menus. They contain no assets, code or names from any console maker.

Couchtop is an independent project and is not affiliated with, sponsored by, or endorsed by Nintendo, Microsoft or Sony. It contains no Nintendo images, audio, fonts or code; all artwork and sounds are original. "Wii Remote" is a trademark of Nintendo, used here only to describe controller compatibility.
