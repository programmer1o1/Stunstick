# Bundled StudioMDL (MDLForge)

Stunstick can use a bundled `studiomdl` if it exists either:

- Next to the executable, or
- Under `tools/studiomdl/` (supports both `tools/studiomdl/studiomdl*` and `tools/studiomdl/bin/studiomdl*`).

## Build + install (from repo root)

### Linux/macOS

```sh
./scripts/toolchain/build_studiomdl.sh
```

To also build the optional 32-bit Linux binary (useful for older Source toolchains / 32-bit `vphysics.so`), set:

```sh
STUDIOMDL_BUILD_I686=ON ./scripts/toolchain/build_studiomdl.sh
```

### Windows

```powershell
.\scripts\toolchain\build_studiomdl.ps1
```

After install, `stunstick compile` can auto-fallback to this bundled tool when a game-provided `studiomdl` isn't found.

## Collision models on Linux/macOS

The bundled (internal) StudioMDL (MDLForge) does **not** generate `.phy` files on Linux/macOS. If your QC uses `$collisionmodel`/`$collisionjoints` and you need `.phy` output, use a Windows `studiomdl.exe` via Wine/Proton (e.g. from Source SDK Base 2013).
