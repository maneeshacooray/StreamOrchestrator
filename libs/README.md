# Native libraries

This folder holds the native **libmpv** engine that the app embeds for RTSP playback.

Expected file:

- `libmpv-2.dll` (x64)

It is copied next to the built executable (see `StreamOrchestrator.csproj`). The DLL is a binary
dependency from a trusted mpv Windows build (the `zhongfly` mpv-winbuild releases, baseline
x86_64 variant for widest CPU compatibility). Keep the architecture at **x64** to match the app.

> The DLL (~120 MB) is **not** committed to git. Fetch it once after cloning:
>
> ```powershell
> ./scripts/fetch-libmpv.ps1          # pinned known-good build
> ./scripts/fetch-libmpv.ps1 -Latest  # latest release
> ```
>
> End users do not need this — the installer bundles the DLL into `Setup.exe`.
