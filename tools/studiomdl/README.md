# Bundled StudioMDL (MDLForge)

Stunstick can use a bundled `studiomdl` if it exists either:

- Next to the executable, or
- Under `tools/studiomdl/` (supports both `tools/studiomdl/studiomdl*` and `tools/studiomdl/bin/studiomdl*`).

## Build + install (from repo root)

### Linux/macOS

```sh
./scripts/toolchain/build_studiomdl.sh
```

On Apple Silicon, the script builds the x86-64 tool. Running StudioMDL on that Mac requires Rosetta.

To also build the optional 32-bit Linux binary (useful for older Source toolchains / 32-bit `vphysics.so`), set:

```sh
STUDIOMDL_BUILD_I686=ON ./scripts/toolchain/build_studiomdl.sh
```

### Windows

```powershell
.\scripts\toolchain\build_studiomdl.ps1
```

After install, `stunstick compile` can auto-fallback to this bundled tool when a game-provided `studiomdl` isn't found.

## Large SMD files

The bundled compiler accepts up to 262,144 triangles per SMD. Vertex limits still apply. Mesh-local VTX indices remain 16-bit, and the compiler uses wider indices internally when arranging vertices across the model.

## Collision models on Linux/macOS

The macOS build compiles the portable MDLForge physics library from the vendored source. Both macOS release packages include `libvphysics.dylib` beside StudioMDL in the CLI and Desktop. StudioMDL loads it automatically when compiling `$collisionmodel` or `$collisionjoints` to `.phy`. On Apple Silicon this compiler and library run as x86-64 through Rosetta.

To test a different compatible build of the library, pass its path when building StudioMDL.

```sh
STUDIOMDL_VPHYSICS_DYLIB=/path/to/libvphysics.dylib ./scripts/toolchain/build_studiomdl.sh
```

Set `MDLFORGE_ALLOW_NATIVE_COLLISION=0` to disable native collision compilation. Without the companion library, the default bundled tool skips `.phy` generation. A game-compatible Windows `studiomdl.exe` via Wine/Proton is an alternative on Linux and macOS.
