# Third-party notices

Couchtop ships the following third-party components. All artwork, the hand pointer, the icon, every sound
effect and the menu music are original to this project; the audio is synthesized at runtime.

Couchtop Sports (in progress) uses no third-party assets either: its 3D characters ("Pals"), equipment, courts,
courses, textures, emblems and sounds are all generated procedurally by Couchtop's own code.

Music and sounds you add yourself stay on your PC, in Couchtop's data folder. They are never shipped with
Couchtop or sent anywhere, and you are responsible for having the right to use the files you pick.

| Component | Used for | License |
|---|---|---|
| [M PLUS Rounded 1c](https://github.com/coz-m/MPLUS_FONTS) (Copyright 2021 The M+ FONTS Project Authors) | UI font | SIL Open Font License 1.1 (full text in `licenses/M-PLUS-Rounded-1c-OFL.txt`) |
| [NAudio](https://github.com/naudio/NAudio) (NAudio.Core, NAudio.WinMM, NAudio.Wasapi) | Low-latency sound mixing, system volume | MIT |
| [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) | Web channel | Microsoft WebView2 SDK license (BSD-style) |
| .NET runtime and WPF (self-contained builds) | Application runtime | MIT |
| Segoe UI / Segoe UI Variable | Font for the Dashboard and Media Bar menu styles | Supplied by Windows and rendered by the system; not bundled or redistributed |

Test-only dependencies (not shipped): xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT).

Build-only, never shipped with Couchtop: the promotional trailer and clips in `video/` are rendered with
[Remotion](https://www.remotion.dev), which is source-available under its own licence (free for individuals,
non-profits and for-profit organisations with up to three employees; larger organisations need a company
licence). Remotion itself is not part of any Couchtop download.

## Trademarks

Couchtop is an independent project. It is not affiliated with, sponsored by, or endorsed by Nintendo,
Microsoft or Sony, and it contains no artwork, audio, fonts, icons or code from any of them.

Nintendo and Wii Remote are trademarks of Nintendo. "Wii Remote" appears only to describe controller
compatibility.

The **Dashboard** and **Media Bar** menu styles are original designs, drawn in code for this project, that take
inspiration from the shapes of mid-2000s console menus. No console maker's assets, icons, fonts, sounds or
names are used, and neither style claims any association with the consoles that inspired it.

Screenshots and videos of Couchtop show the icons of whatever programs are installed on the PC that produced
them (Windows accessories, Store apps, Steam and Epic titles). Those icons remain the property of their
respective owners and appear only as an incidental part of showing Couchtop running.

Windows, Microsoft Store, Segoe UI and WebView2 are trademarks of Microsoft. Steam is a trademark of Valve
Corporation; Epic Games is a trademark of Epic Games, Inc. These names appear only to describe what Couchtop
finds and launches on your PC.
