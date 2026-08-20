<img src="icon.png" width="128" alt="icon">

# System Spinner x64 (for Windows)

System monitoring with two faces: a tray icon outside and/or panel inside game.

[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-blue)](#requirements)

While a full-screen application is in front of you, this is an overlay. The moment it is gone, this is a tray icon.


## Features

- Show CPU usage in menu bar
- Audio and brightness external monitor contol (over HDMI/DVI/USB-C with standart keys)
- Custom OSD for Windows for volume and brightness control
- Custom choice for adjustments (more accurate volume and brightness control)
- Top CPU/MEM process in popup window
- Memory statistics with swap
- Network utilisation and extrnal ip address (use checkip.dyndns.org, you can turn off showing external ip)
- Hardware Information for Cpu Temp and Fan
- Native Windows application with system appearance support 
- Spinner overlay effects
- In fullscreen mode it's a customisable game overlay
- Localization (English, Arabic, Chinese, French, German, Italian, Japanese, Russian)

## Tech

Windows 11 and never, .NET Desktop Runtime 10 or newer, **PawnIO** — [pawnio.eu](https://pawnio.eu), written on c# native app

## Build

One command from the project root:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -install
```

If the SDK is missing, install it and restart the terminal:

```powershell
winget install --id Microsoft.DotNet.SDK.10
```
