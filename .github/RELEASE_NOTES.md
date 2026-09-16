> **Very early beta.** Expect rough edges. Try shell mode in a virtual machine before using it on your main PC.

### New in this version

- **Couchtop can be your whole desktop.** Turn on Shell Mode yourself (Settings › Shell Mode, after the Safety Test) and Couchtop replaces the Windows desktop, taskbar and Start menu while Windows keeps running underneath. It is never switched on for you.
- **Couchtop Bar:** open apps grouped per program, search, the notification area for background apps, volume, battery, Wi-Fi, clock and power. It keeps maximized windows clear and hides for full-screen games. Try it in launcher mode with Settings › Desktop › Couchtop Bar › Always.
- **Shortcuts:** `Ctrl + Alt + Space` searches apps, files, settings and open windows; `Ctrl + Alt + Tab` switches apps; `Ctrl + Alt + D` clears the screen.
- **Status center** with volume, battery, Wi-Fi, notifications and quick links to the Windows settings panels.
- **Files is a real file manager now:** tabs, split view, search, copy/move, rename, Recycle Bin, zip and unzip, properties, drives, USB and network folders.
- **Accessibility:** a High Contrast theme and bigger text.

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
