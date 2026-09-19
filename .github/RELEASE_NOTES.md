> **Very early beta.** Expect rough edges. Try shell mode in a virtual machine before using it on your main PC.

### New in this version

- **Pals can now come out onto your desktop**, as intended for 0.6. Right-click your Pal and choose *Take me to the desktop* (or Settings › Pals › Visit the desktop). It walks along the taskbar, climbs onto your app windows, rides along when you drag one, and complains when you close the one it's standing on. Drag it anywhere; everything around it clicks through to your apps, and it hides for full-screen games and videos. Off until you turn it on.
- A Pal no longer stays asleep when Couchtop starts behind other windows.

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
