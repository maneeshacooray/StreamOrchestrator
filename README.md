# StreamOrchestrator

A low-latency Windows desktop app for viewing a live **RTSP CCTV stream** and orchestrating it
across a **dual-monitor** setup alongside presentation windows.

## Features

- **Low-latency RTSP playback** of a live CCTV feed (libmpv, low-latency profile).
- **Move the fullscreen stream to the other display** on a dual-monitor (extend) setup.
- **Swap** the stream with external presentation windows (PowerPoint / PDF / browser) between the
  two monitors at any time, with cycling when several presentations are open.
- **Global hotkeys** so move/swap/fullscreen work even while the stream is focused.

## Stack

- WPF on **.NET 8** (`net8.0-windows`).
- **libmpv** render engine embedded via an `HwndHost` child window (`wid`).
- Win32 interop for monitor enumeration and foreign-window positioning.

## Requirements

- Windows 10/11, x64.
- **.NET 8 SDK** to build (`dotnet --list-sdks` should list an 8.x SDK).
- `libmpv-2.dll` (x64) in `libs/` — shipped with the app; end users need nothing installed.

## Build & run

```powershell
./scripts/fetch-libmpv.ps1          # one-time: download the native libmpv engine into libs/
dotnet build
dotnet run --project src/StreamOrchestrator
```

## Distribution

Self-contained publish (no .NET/mpv needed on the target machine), packaged as a `Setup.exe`
via Inno Setup. See `installer/`.

## Development workflow

- Base branch: **`development`**. Features are built on `feat/*` / `chore/*` branches and merged
  into `development` with a merge commit (`--no-ff`) once tested.
- Commits follow **Conventional Commits**.
