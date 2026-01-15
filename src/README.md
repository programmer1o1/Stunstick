# Cross-platform scaffold

This folder contains the new cross-platform backend + CLI + (eventual) desktop UI.

## Projects

- `src/Stunstick.Core` — Pure, UI-agnostic logic and data types.
- `src/Stunstick.App` — Application layer (use-cases) + external tool launching abstraction.
- `src/Stunstick.Cli` — Cross-platform CLI entrypoint for the MVP feature set.
- `src/Stunstick.Desktop` — Cross-platform desktop UI shell (Avalonia).

## Build prerequisites

- Install .NET 8 SDK.
- `Stunstick.Desktop` needs NuGet restore for Avalonia packages.
- Optional (for native `studiomdl` on Linux/macOS/Windows): build `tools/MDLForge` and install it into `tools/studiomdl/` via `scripts/toolchain/build_studiomdl.*`.

## CLI (current scaffold)

The CLI exists mainly to lock the backend API and provide a stable integration surface for the Avalonia desktop UI (and other frontends).

Examples:

- `stunstick inspect path/to/model.mdl`
- `stunstick decompile --mdl path/to/model.mdl --out outdir`
- `stunstick unpack --in path/to/pak01_dir.vpk --out outdir --verify`
- `stunstick pack --in folder --out path/to/pak01_dir.vpk --vpk-version 2 --with-md5`
- `stunstick pack --in folder --out path/to/pak01_dir.vpk --multi-file --split-mb 1024`
- `stunstick pack --batch --in parentFolder --out outdir --type vpk`
- `stunstick steam list`
- `stunstick compile --qc path/to/model.qc --steam-appid 620 --wine-prefix ~/.wine`
- `stunstick view --mdl path/to/model.mdl --steam-appid 620 --wine-prefix ~/.wine`

Decompile writes to `<out>/<modelname>/` and outputs:

- `manifest.json`
- `skeleton.smd`
- `model.qc` (minimal QC)
- `ref_bodypart*_model*_lod*.smd` (reference mesh SMDs for available LODs, when `.vvd` + `.vtx` are present next to the `.mdl` or embedded in v53 `.mdl`)
- `original/` (a copy of the model files found next to the `.mdl`)
