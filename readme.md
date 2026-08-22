<img src="icon.ico" width="128" alt="icon">

# System Spinner x64 (for Windows)

System monitoring with two faces: a tray icon on the desktop, and a game overlay while a full-screen application is in front of you.

[![Downloads](https://img.shields.io/github/downloads/andrey-lysikov/System-Spinner-x64/total)](https://github.com/andrey-lysikov/System-Spinner-x64/releases/latest)
[![Release](https://img.shields.io/github/v/release/andrey-lysikov/System-Spinner-x64)](https://github.com/andrey-lysikov/System-Spinner-x64/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-blue)](https://github.com/andrey-lysikov/System-Spinner-x64/releases/latest)

This is the Windows version. If you are looking for macOS, go to [System Spinner](https://github.com/andrey-lysikov/System-Spinner).

## Features

- CPU usage shown by the animated tray icon
- Audio and brightness control for external monitors (over HDMI/DVI/USB-C, with the standard media keys)
- HDR switch in the tray menu for every screen that carries it
- Custom OSD for volume and brightness, shown in place of the Windows one rather than alongside it
- Monitor brightness driven by the brightness keys of the keyboard: Windows makes no key of them and acts on them nowhere
- Adjustable number of steps for more accurate volume and brightness control
- Top CPU/memory processes in the popup window
- Memory statistics with swap
- Network usage and external IP address (uses checkip.dyndns.org; showing the external IP can be turned off)
- Hardware information: CPU temperature and fan speed
- Native Windows application with system appearance support
- Spinner overlay effects
- Customisable game overlay in full-screen mode
- Localisation (English, Arabic, Chinese, French, German, Italian, Japanese, Russian)

*WARNING: the application is not officially signed, so Windows will ask you to allow it to run.*

This application uses **PawnIO** — [pawnio.eu](https://pawnio.eu) — for low-level access to the hardware. If you do not have it, please install it.

## Screenshots

<p align="center">
  <img src="pictures/main_window.jpg" height="380">
  <img src="pictures/spin_menu.jpg" height="380">
  <img src="pictures/main_detail_window.jpg" height="380">
</p>

## Tech

Written in C#, Windows 11+, .NET Desktop Runtime 10+
