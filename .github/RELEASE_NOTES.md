> **Very early beta.** Expect rough edges. Try shell mode in a virtual machine before using it on your main PC.

### New in this version

- **The Dashboard shell was rebuilt to look like the dashboards it is named after.** Angled blade tabs are gone: sections are now plain lowercase names in a rail, and the tiles are a mosaic rather than one row — a big tile to open each section, columns of stacked tiles, and wide tiles every so often. Flat square tiles with the name along the bottom, solid green for Couchtop's own screens, real Windows icons for apps, a white outline on the tile you are on, the neighbouring sections peeking in from the edges, and up/down moving between the two rows of a column.

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
