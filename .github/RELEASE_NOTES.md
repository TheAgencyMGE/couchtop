> **Very early beta.** Expect rough edges. Try shell mode in a virtual machine before using it on your main PC.

### New in this version

- **Pals.** A new channel on the main page for making your own 3D character: body, face, eyes, hair, clothes, accessories, a name and a personality. Your Pal hangs out on the home screen, follows your pointer, reacts to what you do with 160+ handwritten lines, and turns up by the Start button, on the Power screen, peeking in on other screens and in the Couchtop Bar. Poke it, or pick it up and drop it somewhere. How often it talks is up to you (Settings › Pals), and it never talks over full-screen games or videos.
- Closing search, the task switcher, the calendar or the status center with Esc no longer posts a "Something went wrong" notice.
- The Files toolbar buttons (Details view, Show hidden, Sort) keep working after the first use.
- The task switcher shortcut is now **Ctrl + Alt + W**. Windows keeps Ctrl + Alt + Tab for itself, so the old one never worked.

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
