# Stunstick cross-platform notes

This repo contains both:

- **Stunstick (cross-platform, .NET 8):** `Stunstick.CrossPlatform.sln` (CLI + Avalonia Desktop UI under `src/`)
- **Original Crowbar (Windows-only):** `Crowbar.sln` (VB.NET WinForms on .NET Framework 4)

The Stunstick projects are designed to run on Windows, Linux, and macOS.

**Status: Experimental.** These cross-platform builds are early and may change rapidly.

## Build

Prereq: install the .NET 8 SDK.

From the repo root:

- Build: `dotnet build Stunstick.CrossPlatform.sln`
- Run CLI: `dotnet run --project src/Stunstick.Cli -- --help`
- Run Desktop: `dotnet run --project src/Stunstick.Desktop`

## Toolchain notes

Stunstick can launch Source toolchain tools (like `studiomdl`) from:

- Your game install (preferred when available), or
- A bundled `studiomdl` under `tools/studiomdl/` (optional; see `tools/studiomdl/README.md`)

On Linux/macOS, some workflows (especially collision models / `.phy`) may still require a Windows `studiomdl.exe` via Wine/Proton, depending on the game’s toolchain modules.

## Docs

- `README.md` — overview + build commands
- `FAQ.md` — common questions
- `parity.md` — parity tracker vs original Crowbar
