# Stunstick

Stunstick is a cross-platform fork of Crowbar (GoldSource and Source Engine modding tool).

**Status: Experimental.** Stunstick is very early and may be missing features or change behavior.
Expect bugs, breaking changes, and incomplete parity. GitHub releases are prereleases intended for testing.

This repo contains:

- **Stunstick (cross-platform, .NET 8):** `Stunstick.CrossPlatform.sln` (CLI + Avalonia Desktop UI under `src/`)
- **Original Crowbar (Windows-only, unchanged):** `Crowbar.sln` (VB.NET WinForms on .NET Framework 4)

## Links

- Original Crowbar website: https://steamcommunity.com/groups/CrowbarTool
  - When linking to Crowbar, prefer this page since it aggregates the official info and downloads.

## Building

### Stunstick (cross-platform)

Prereq: install .NET 8 SDK.

- Build: `dotnet build Stunstick.CrossPlatform.sln`
- Run CLI: `dotnet run --project src/Stunstick.Cli -- --help`
- Run Desktop: `dotnet run --project src/Stunstick.Desktop`

### Original Crowbar (Windows-only)

I currently build via Visual Basic in Visual Studio Community 2017.
I use Debug x86 when debugging and Release x86 when releasing to public.

I tested building in Visual Studio Community 2019 on 15-Apr-2021. All I had to change were the settings from Debug Any CPU and Release Any CPU to Debug x86 and Release x86 at the top in the default toolbars.
