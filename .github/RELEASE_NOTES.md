> **Very early beta.** Expect rough edges. Try shell mode in a virtual machine before using it on your main PC.

### New in this version

- **Three shells, switched live in Settings › Display.** Couchtop now comes in three complete looks, not skins: **Channels** (the original grid, with themes, the hand pointer, menu music and Pals), **Dashboard** (graphite and green, angled blade tabs, a row of app tiles, dry mechanical clicks) and **Media Bar** (deep blue, a row of categories crossed by a column of items, thin outlined icons, soft blips, and a background that shifts with the month and dims at night). Each restyles every screen — Settings, Files, Power, the start screen, dialogs, the Quick Menu and the Couchtop Bar — draws your apps with their real Windows icons on flat plates, and has its own sounds. Your apps, games, files and settings are shared by all three; switching takes effect immediately with no restart. Both new designs are original: no console assets or names are used.
- **A welcome tour for new installs.** Seven steps covering what Couchtop is and what it doesn't touch, a side-by-side pick of the three shells (choosing one applies it there and then), how many of your apps and games it found, the built-in screens, the controls for mouse, keyboard, controller and Wii Remote, and the ways back to Windows. Replay it any time from Settings › About › Take the tour.
- **The Windows taskbar gets out of the way.** With the Couchtop Bar on screen, Explorer's taskbar auto-hides so there aren't two bars along the bottom, and it's put back exactly as you had it afterwards. Settings › Desktop turns it off.

### Fixed

- **Dashboard:** hovering no longer runs the row away to the end. A hover now only counts when the pointer has actually moved, and pointer moves leave the row where it is, scrolling only when the selection would run off an edge. The Media Bar had the same runaway down its column.
- Pals no longer appear anywhere in the Dashboard and Media Bar shells — they're part of the Channels style.

### Which download?

| Download | For |
|---|---|
| `Couchtop-…-win-x64.zip` | **Install.** Extract it and run `Couchtop.Setup.exe`. It installs just for you, with no administrator rights. Needed for shell mode. |
| `Couchtop-…-win-x64-portable.zip` | **Portable.** Extract it anywhere, even a USB drive, and run `Couchtop.exe`. Nothing is installed; your channels and settings stay in the `Data` folder next to it. |

Both are self-contained, so no .NET install is needed. Removing the portable version is just deleting its folder.

### "Windows protected your PC"?

The beta isn't code-signed yet, so Windows SmartScreen may warn you the first time. Click **More info → Run anyway**.
Some antivirus tools are also cautious about apps that can replace the Windows shell. Couchtop is open source; you can read every line or build it yourself.

### If anything goes wrong

- `Ctrl+Alt+Shift+F12` returns to the normal Windows desktop and turns shell mode off.
- **Start menu › Couchtop Recovery**, or `Recover-Explorer.cmd` in the install folder, restores Explorer without opening Couchtop.

[Watch the trailer](https://theagencymge.github.io/couchtop/) · [Changelog](https://github.com/TheAgencyMGE/couchtop/blob/main/CHANGELOG.md)
