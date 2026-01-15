# Stunstick FAQ

## What is Stunstick?

Stunstick is a cross-platform fork of **Crowbar** (GoldSource and Source Engine modding tool), written in C#/.NET 8.

This repo contains both:

- **Stunstick (cross-platform):** `Stunstick.CrossPlatform.sln` (CLI + Avalonia Desktop UI under `src/`)
- **Original Crowbar (Windows-only):** `Crowbar.sln` (VB.NET WinForms on .NET Framework 4)

## Where can I download the original Crowbar?

The official Crowbar page (info + downloads): https://steamcommunity.com/groups/CrowbarTool

## Is Stunstick stable yet?

Not yet. Stunstick is experimental and will ship prereleases while features stabilize.
Expect bugs, missing parity, and breaking changes while things are still in flux.

## Where do I report issues for Stunstick?

Use GitHub issues on the Stunstick repo:

- https://github.com/programmer1o1/Stunstick/issues

When reporting a bug, include:

- What you expected vs what happened
- The exact command you ran (CLI) or the steps you clicked (Desktop)
- Logs (Desktop: Help → Copy log / Save log)
- A minimal repro model/mod if possible

## Do I need Source SDK tools installed?

Not always. Stunstick can use game-provided tools when present, and can optionally use a bundled `studiomdl` (see `tools/studiomdl/README.md`).

For workflows that require Windows-only toolchain components (or certain collision model outputs), you may still need a Windows `studiomdl.exe` (or Wine/Proton on Linux/macOS).

## What’s the relationship to Crowbar?

Stunstick aims to match or exceed Crowbar’s behavior across platforms while keeping the original Crowbar program unchanged in this repo.
