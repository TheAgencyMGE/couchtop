# Changelog

All notable changes to Couchtop are documented here. Versions stay on `0.x` until the first stable release.

## [0.4.0-beta.1] - 2026-09-15

### Added

- **Your own music and sounds** in **Settings › Sound**:
  - **Menu music:** pick any song to loop on the menu instead of the built-in music box.
  - **Menu sounds:** replace the startup, point, select, back, page turn, start, Quick Menu, tick and error sounds one by one, with **Play** to hear each one and **Reset** to go back.
  - MP3, WAV, M4A, AAC, WMA and FLAC files are supported; sounds longer than 5 seconds are cut short with a soft fade.
  - Couchtop copies every file you pick into its own data folder, so moving or deleting the original doesn't break anything. Files you stop using are cleaned up.
  - Files Windows can't play are refused with a plain explanation, and unused copies are never left behind.
- **Couchtop Sports is coming soon.** A Sports channel now sits on the menu as a sneak peek at what's next: tennis, baseball, bowling, golf and boxing, built right into Couchtop, with easy controls, training challenges and games with friends. It is still being made and **isn't playable yet**; the channel opens a "coming soon" page.

## [0.3.2-beta.1] - 2026-09-14

### Fixed

- **Sky Resort** now looks like real Frutiger Aero instead of a tinted Classic menu:
  - Tiles, app cards, the bottom bar and panels are see-through glass with a hard top-half gloss.
  - Built-in channel icons sit inside clear, colored glass orbs, and app icons get a glass bubble.
  - The scenery is a saturated blue sky with big clouds, a turning sun burst with rainbow lens flares, a hazy glass city, and a green meadow with water droplets, bokeh and rising bubbles.

## [0.3.1-beta.1] - 2026-09-14

### Fixed

- **Web:** the toolbar no longer runs off both edges of the screen. The address box now stretches to fill the space between the buttons at any resolution.
- **Internet tile:** the globe's orbit ring no longer gets clipped at the tile edge.
- **Getting back to Windows:** going to the normal desktop no longer means digging into Power. There is now an always-visible **Desktop** button on the home menu, a **Windows Desktop** entry in the Couchtop Menu, and the Quick Menu's **Windows Desktop** button in every mode. Couchtop keeps running; click it on the taskbar or use the Quick Menu hotkey to come back.

## [0.3.0-beta.1] - 2026-09-14

### Added

- **Five new themes** in **Settings › Display › Theme**, switchable live:
  - **Sky Resort:** glossy glass, open skies, a turquoise sea with island palms, and glass bubbles drifting up.
  - **Neon City:** black steel, hazard yellow and cyan neon, cut-corner tiles, a scanning HUD and a flickering skyline.
  - **Midnight:** sleek, flat and dark, with hairline borders and soft ambient glows.
  - **Sakura:** warm paper, blossom branches and falling petals.
  - **Sunset:** golden hour over the water with palm silhouettes.
- Themes restyle everything: colors, fonts, corner shapes, the pointer, the Quick Menu, dialogs, the web start page and backdrops on other monitors.
- Neon City and Midnight come with their own artwork for the built-in channels, and app tiles tint to suit each theme.
- Animated theme scenery runs only while Couchtop is in front and turns off with **Reduce motion**.

## [0.2.0-beta.1] - 2026-09-14

### Added

- **Portable version:** a second download, `Couchtop-<version>-win-x64-portable.zip`. Extract it anywhere (even a USB drive) and run `Couchtop.exe`. Nothing is installed, and channels, settings and logs stay in a `Data` folder next to the exe.
- Shell mode is blocked in portable copies. Install with Setup to use it.

## [0.1.0-beta.1] - 2026-09-14

First public, very early beta.

### Added

- **Menu:** full-screen 4×3 channel menu with pages, glossy animated tiles, a tilting hand pointer, a curved bottom bar with clock and date, and a Message Board.
- **Channel start screen:** zoom transitions and a launch flash.
- **App discovery:** Start menu programs, Microsoft Store apps, Steam (all libraries), Epic Games and Windows tools, with automatic re-discovery after app updates.
- **Built-in channels:** Files, Photos (with slideshow), Web (WebView2), Settings, Power and Customize (drag and drop, rename, recolor, custom pictures).
- **Quick Menu** overlay over any app: return to the menu, close apps, switch windows, adjust volume.
- **Input:** mouse, keyboard, XInput controllers, and Wii Remote controllers over Bluetooth HID.
- **Displays:** multi-monitor placement with backdrops on other screens, per-monitor DPI v2, and recovery after display, DPI and sleep/wake changes.
- **Sound:** original synthesized sound effects and menu music.
- **Themes:** Classic and Night, plus a reduced-motion option.
- **Optional per-user shell mode** (HKCU Winlogon), gated behind a compatibility check and a Shell Safety Test.
- **Guardian watchdog:** crash, freeze and boot-loop fallback to Explorer, plus the emergency shortcut `Ctrl+Alt+Shift+F12`.
- **Recovery:** a standalone recovery tool and `Recover-Explorer.cmd`.
- **Setup:** per-user installer and uninstaller. Uninstall always restores Explorer first.
- **Tests:** unit and integration tests, an installer end-to-end test, a smoke test, and snapshot rendering.

### Known limitations

- Shell mode has not yet been validated across a wide range of hardware. Try it in a VM first.
- In shell mode there is no system tray; use **Quick Menu › Windows Desktop** when you need Explorer.
- Wii Remote pointing requires an IR sensor bar.

[0.3.2-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.3.2-beta.1
[0.3.1-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.3.1-beta.1
[0.3.0-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.3.0-beta.1
[0.2.0-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.2.0-beta.1
[0.1.0-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.1.0-beta.1
