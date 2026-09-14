# Changelog

All notable changes to Couchtop are documented here. Versions stay on `0.x` until the first stable release.

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

[0.2.0-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.2.0-beta.1
[0.1.0-beta.1]: https://github.com/TheAgencyMGE/couchtop/releases/tag/v0.1.0-beta.1
