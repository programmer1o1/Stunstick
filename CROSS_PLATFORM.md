# Platform and toolchain guide

Use the .NET 8 runtime to run a release or the .NET 8 SDK to build Stunstick. The [release ZIPs](https://github.com/programmer1o1/Stunstick/releases) are framework-dependent.

| Platform | Toolchain setup |
| --- | --- |
| Windows x64 | Use your game’s `studiomdl.exe` and `hlmv.exe` where available. |
| macOS Intel or Apple Silicon | The release includes bundled StudioMDL and `libvphysics.dylib` for supported Source collision builds. On Apple Silicon, StudioMDL requires Rosetta. |
| Linux x64 | Use a compatible native toolchain or configure Wine or Proton for Windows game tools. To use bundled StudioMDL from source, build it locally. |

## Configure a game

Open **Games** and select a Steam preset or create a custom preset. Set the game or mod folder containing `gameinfo.txt` and the relevant tool paths. A game-specific StudioMDL is useful when the target engine expects its own MDL version or QC commands.

The bundled macOS compiler can produce `.phy` from `$collisionmodel` and `$collisionjoints` using its companion library. For other collision toolchains, check the [StudioMDL setup and limits](tools/studiomdl/README.md).

## CLI

Run these examples from the CLI directory or add that directory to your shell’s path. On Windows the executable is `stunstick.exe`. Paths are illustrative.

```sh
stunstick inspect models/prop.mdl
stunstick decompile --mdl models/prop.mdl --out work
stunstick compile --qc work/prop/model.qc --game /path/to/mod
stunstick view --mdl /path/to/mod/models/prop.mdl --game /path/to/mod
```

To use a specific compiler, pass `--studiomdl /path/to/studiomdl`. Use `--hlmv` for a specific model viewer. Run `stunstick --help` for options including Steam presets and Wine overrides.

### Archives

```sh
stunstick unpack --in pak01_dir.vpk --out extracted --verify
stunstick pack --in addon --out example.gma
stunstick unpack --in example.gma --out checked --verify
```

For split VPKs, keep the numbered archives beside the directory VPK. GMA packing uses `addon.json` as metadata and can generate it when needed. The archive stores that metadata in its header.

### Decompile output

By default, output goes in a per-model folder under `--out`. It can include `model.qc`, `skeleton.smd`, reference and LOD SMDs, animation and physics SMDs, flex VTA, procedural VRD, GoldSource texture BMPs, and a manifest. Files depend on the model and the selected options. Keep the output folder together when compiling so QC includes and SMD references resolve.

## See also

- [Build and launch Stunstick](README.md#build-from-source)
- [Supported model formats and checks against Crowbar](parity.md#mdl-format-coverage)
