# Installer

Builds a `Setup.exe` for StreamOrchestrator with [Inno Setup](https://jrsoftware.org/isinfo.php).

## Build

1. Produce the self-contained app:

   ```powershell
   ./scripts/publish.ps1
   ```

   This writes `publish/` (the app, the .NET runtime, and `libmpv-2.dll`).

2. Compile the installer:

   ```powershell
   & "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" installer/StreamOrchestrator.iss
   ```

   (Inno Setup may install under `C:\Program Files (x86)\Inno Setup 6\` instead, depending on
   how it was installed.)

The result is `installer/Output/StreamOrchestrator-Setup.exe` (~80 MB) — a standard wizard that
installs to Program Files, adds Start Menu (and optional desktop) shortcuts, and registers an
uninstaller. Recipients need nothing else installed.

## Signing (optional)

The installer is unsigned by default. To avoid the SmartScreen "unknown publisher" prompt, sign
both the app exe and the produced `Setup.exe` with a code-signing certificate, or add a
`SignTool` directive to `StreamOrchestrator.iss`.
