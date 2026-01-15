# Stunstick parity tracker — original Crowbar (WinForms VB) vs cross-platform (.NET 8)

This file is a living comparison of **original Crowbar** features vs the **Stunstick** cross-platform rewrite.

Compared codebases:
- **Original app**: `Crowbar.sln` (VB.NET WinForms, .NET Framework 4) — main UI under `Crowbar/Widgets/`
- **Cross-platform fork (Stunstick)**: `Stunstick.CrossPlatform.sln` (C#/.NET 8) — `src/Stunstick.App` (logic) + `src/Stunstick.Desktop` (Avalonia UI) + `src/Stunstick.Cli` (CLI)

## Legend

- ✅ **Implemented** — comparable feature exists (minor UX differences OK)
- 🟡 **Partial** — works, but missing options / missing UX / different workflow
- ❌ **Missing** — no equivalent feature yet
- 🚫 **Out of scope** — intentionally not planned for cross-platform

## Scope decisions (intentional differences)

- 🚫 No OS shell integration (file associations, “Open with Stunstick”, Explorer/Finder context menu integration).
- 🚫 No in-app updater (original “Update” tab).
- ✅ Keep single-instance activation parity (safe IPC/bring-to-front) without relying on OS shell integration.

## Cross-platform-only additions (not in original)

- ➕ Scriptable CLI for most workflows (`src/Stunstick.Cli/Program.cs`): `inspect`, `unpack`, `pack`, `decompile`, `compile`, `view`, `download`, `publish`, `delete`, `quota`, `list`.
- ➕ Windows/Linux/macOS support; can launch Windows-only tools via Wine (`studiomdl.exe`, `hlmv.exe`, `hammer.exe`) when needed.
- ➕ Workshop helper process `Stunstick.SteamPipe` (Steamworks.NET) to isolate Steamworks calls from the UI/CLI process.

## Tab mapping (original → cross-platform)

The original Crowbar app is organized around these main tabs (see `Crowbar/Widgets/Main Tabs/` and `Crowbar/Widgets/- Application/MainForm.vb`).

| Original tab | Original entrypoint | Cross-platform equivalent | Status | Key differences |
|---|---|---|---:|---|
| Set Up Games | `SetUpGamesUserControl.vb` | Desktop: `Games` tab | ✅ | Steam presets are read-only (overrides supported); custom presets add/clone/delete; macro + extra library root editor added. |
| Download | `DownloadUserControl.vb` | Desktop: `Workshop → Download`; CLI: `download` | ✅ | Cross-platform adds cache + SteamCMD fallback; original has output-path presets + richer UI polish. |
| Unpack | `UnpackUserControl.vb` | Desktop: `Explore`; CLI: `unpack` | ✅ | Same/subfolder/work/addons presets, keep-path toggle, saved searches, CRC/MD5, temp extract + use-in flows. |
| Preview | `ViewUserControl.vb` (ViewerType=Preview) | Desktop: `View` (built-in preview) + `Inspect` | ✅ | Preview merged into View/Inspect (wireframe + data viewer), MDL version override retained. |
| Decompile | `DecompileUserControl.vb` | Desktop: `Decompile`; CLI: `decompile` | ✅ | Folder/folder+subfolders; accessed-bytes logs; QC writes LOD facial/nofacial, replacebone, shadow material toggle, shadowlod replacemodel; keep-path/flat output, debug files optional. |
| Edit | `EditUserControl.vb` | — | ❌ | Original tab is empty/placeholder in this repo. |
| Compile | `CompileUserControl.vb` | Desktop: `Compile`; CLI: `compile` | ✅ | Folder input + recursion; copy-output presets (game models/work/subfolder/same folder); per-QC log; improved “Use in View”. |
| View | `ViewUserControl.vb` (ViewerType=View) | Desktop: `View`; CLI: `view` | ✅ | Built-in preview + external HLMV launch; view-as-replacement supported; override MDL version. |
| Pack | `PackUserControl.vb` | Desktop: `Pack`; CLI: `pack` | ✅ | Single/batch modes, presets (same/parent/work), skip current folder, free-form opts, GMA tags/ignore whitelist helper. |
| Publish | `PublishUserControl.vb` | Desktop: `Workshop → Publish`; CLI: `publish/delete/quota/list` | ✅ | Draft list with dirty marker, search/filter, tag presets, SteamCMD option, “Use in Download”. |
| Patch | `PatchUserControl.vb` | — | ❌ | Original tab is mostly placeholder in this repo. |
| Options | `OptionsUserControl.vb` | Desktop: `Options` tab | ✅ | Work-folder picker, per-extension drop routing, Windows-only file associations (select extensions), global drop-anywhere toggle; single-instance always on. |
| Update | `UpdateUserControl.vb` | — | 🚫 | Out of scope. |
| Help | `HelpUserControl.vb` | Desktop: `Help` tab | ✅ | Quick start, docs/CLI/parity links, tutorial/guide/FAQ/issues/changelog/repo links, settings path + log/version helpers. |
| About | `AboutUserControl.vb` | Desktop: `About` tab | ✅ | Shows version and opens license. |

## App-wide behavior parity (not tied to a single tab)

### Single-instance behavior + activation

- ✅ **Original:** optional single-instance mode (Options → “Single instance”); activates existing window and passes command line (`Crowbar/Core/- Application/Main.vb`).
- ✅ **Cross-platform:** always single-instance; forwards args and focuses existing window via named pipes (`src/Stunstick.Desktop/SingleInstanceIpc.cs`).

### Settings persistence

- ✅ **Original:** XML settings + per-tab state (`Crowbar/Core/- Application/AppSettings.vb`).
- ✅ **Cross-platform:** JSON settings for Desktop UI (`src/Stunstick.Desktop/DesktopSettings.cs`) plus in-app Options UI (work folder, drop routing, file associations on Windows).

### Drag-and-drop routing + auto-open

- ✅ **Original:** global drag/drop on the main window and routing rules (file types + folder heuristics), plus optional auto-open via file associations (`Crowbar/Widgets/- Application/MainForm.vb`, `Crowbar/Widgets/Main Tabs/OptionsUserControl.vb`).
- ✅ **Cross-platform:** drag/drop is supported on individual inputs and globally (“drop anywhere to route”), with configurable routing + per-extension routing for MDL/QC/packages, and Windows file associations that launch into the selected tab.

### Cross-tab “Use in …” shortcuts

Original has many one-click handoffs between tabs (wired in `Crowbar/Widgets/- Application/MainForm.vb`).

- ✅ Download → Unpack
- ✅ Unpack → Preview / Decompile
- ✅ Preview/View → Decompile
- ✅ Decompile → Compile
- ✅ Compile → View
- ✅ Pack → Publish (Desktop: Pack tab “Use in Publish” routes the packed output folder into Workshop → Publish content folder)
- ✅ Publish → Download (context-menu “Use in Download”)

Cross-platform equivalents:

- ✅ Workshop Download → “Use in other tab” (routes the last downloaded output into Inspect/Explore/Pack/Decompile/Compile).
- ✅ Games tab → “Use for Compile/View”.
- ✅ Explore → Decompile, ✅ Decompile → Compile, ✅ Compile → View, ✅ View → Decompile shortcuts exist.

## Full feature matrix (original feature inventory → cross-platform status)

### Set Up Games (game presets + Steam macros)

Original: `Crowbar/Widgets/Main Tabs/SetUpGamesUserControl.vb`, `Crowbar/Core/GameSetup/GameSetup.vb`

- ✅ Game preset management:
  - ✅ Add/clone/delete custom presets (name + install dir + tool paths).
  - ✅ Steam presets remain read-only (overrides supported).
  - ✅ Engine-specific “GameSetup” fields (GoldSrc/Source/Source2) detected and saved per preset (with overrides).
- ✅ Discover Steam installs and infer tool paths (via Steam library paths + game setup fields).
- ✅ Steam library roots + macros editor (add/update/delete extra library folders and path macros usable as `$(Name)` in paths).
- ✅ Tool paths per game:
  - ✅ StudioMDL path
  - ✅ HLMV path
  - ✅ Hammer path
  - ✅ “Packer tool” path (VPK/GMAD) stored per preset and routable to Pack tab
- ✅ Run game + open mapping tool (cross-platform Games tab buttons).
- ✅ Engine-specific differences (GoldSrc/Source/Source2 fields/behavior) handled via engine selector and per-engine tool resolution.

### Download (Workshop)

Original: `Crowbar/Widgets/Main Tabs/DownloadUserControl.vb`

- ✅ Parse Workshop item ID from raw input or URL.
- ✅ Fetch details and download via web when `file_url` exists (RemoteStorage details API).
- ✅ Fallback download via Steam when web download isn’t possible.
  - Original: BackgroundSteamPipe (Steamworks) download.
  - Cross-platform: `Stunstick.SteamPipe` (Steamworks.NET) download, plus optional SteamCMD fallback.
- ✅ Output naming options:
  - ✅ include title
  - ✅ include ID (or use file name)
  - ✅ append updated timestamp
  - ✅ replace spaces with underscores
- ✅ Output folder presets:
  - Original: Documents vs Work folder dropdown.
  - Cross-platform: explicit output directory + preset buttons (Documents / Work folder); Work folder configurable in Options.
- ✅ “Convert to expected file/folder” (best-effort):
  - ✅ Garry’s Mod `.lzma` → `.gma` conversion.
  - ✅ App-specific post-processing: Garry’s Mod .lzma→.gma; generic .zip auto-extract to folder; overwrite toggle.
- ✅ UX parity:
  - Progress bar + cancel + example output name preview are present.
- ✅ “Open Workshop page”.
- ✅ “Use in …”:
  - Original: “Use in Unpack”.
  - Cross-platform: “Use in other tab” (routes into Explore/Decompile/Compile/etc).

### Unpack (package browser + extract)

Original: `Crowbar/Widgets/Main Tabs/UnpackUserControl.vb`

- ✅ Package types:
  - ✅ VPK
  - ✅ FPX
  - ✅ GMA
  - ✅ APK
  - ✅ HFS
- ✅ Package browser:
  - ✅ entry listing
  - ✅ tree view
  - ✅ list view
  - ✅ text search
- ✅ Extract operations:
  - ✅ extract all
  - ✅ extract selection
  - ✅ extract selection to temp
  - ✅ open temp/output folder
- ✅ Output path presets:
  - Original: same folder / subfolder / work folder / game addons folder (partially implemented in original).
  - Cross-platform: explicit output folder + preset buttons (Same folder / Subfolder / Work folder / Game addons) + temp output helper + keep-path toggle.
- ✅ Unpack options:
  - Folder-per-package, keep folder structure toggle, log file, size-units toggle, saved searches, CRC32/MD5 verification.
- ✅ One-click “Preview selected” flow exists via Explore → “Use selected in View” (extracts MDL + companion files to temp, routes to View, and auto-loads preview).

### Preview (MDL data viewer + viewer launch)

Original: `Crowbar/Widgets/Main Tabs/ViewUserControl.vb` (ViewerType=Preview)

- ✅ Launch HLMV (viewer).
- ✅ Open mapping tool; ✅ run game (via selected game setup).
- ✅ “Use in Decompile”.
- ✅ Override MDL version (dropdown) for viewer/data viewer.
- ✅ “Data viewer” panel (auto-runs to show model info).

Cross-platform status:

- ✅ Viewer launch exists (`View` tab + Games tab tool discovery/overrides).
- ✅ Mapping tool + run game exist (View tab and Games tab buttons).
- ✅ Preview is integrated into `View` (no separate tab) with built-in wireframe/data viewer.
- ✅ MDL “data viewer” is built into `View` (Data tab; auto-run, shows header counts + lists), and is also available in `Inspect`.
- ✅ “Override MDL version” UI exists (View → Data tab) for the data viewer/inspect read.

### Decompile (MDL → QC/SMD/VTA/etc)

Original: `Crowbar/Widgets/Main Tabs/DecompileUserControl.vb`

- ✅ Input modes:
  - Original: file / folder / folder+subfolders.
  - Cross-platform: file / folder / folder+subfolders (CLI + Desktop).
- ✅ Override MDL version:
  - Original: dropdown option.
  - Cross-platform: Desktop dropdown + CLI `decompile --mdl-version <n>`.
- ✅ Output path presets:
  - Original: work folder / subfolder (of input).
  - Cross-platform: explicit output folder + preset buttons (Work folder / Subfolder), plus “flat output” toggle.
- ✅ Core QC/SMD outputs (baseline):
  - ✅ QC file output with configurable formatting options
  - ✅ reference mesh SMD(s)
  - ✅ LOD mesh SMD(s)
  - ✅ physics SMD (supported cases)
- ✅ Optional outputs:
  - ✅ bone animation SMD output (supports external `.ani` animblocks + root/piecewise movement fixes)
  - ✅ DeclareSequence QCI file output (`<model>_DeclareSequence.qci`, `$declaresequence` lines)
  - ✅ VTA output (flex frames + QC `flexfile` mapping)
  - ✅ texture BMP export (GoldSrc)
  - ✅ procedural bones VRD export
- ✅ Key QC formatting options (cross-platform tracks these closely):
  - ✅ group definebones into `.qci` + `$include`
  - ✅ skinfamily single-line vs multi-line
  - ✅ “only changed materials” in `$texturegroup`
  - ✅ include `$definebone` lines
  - ✅ keyword casing (mixed-case vs lowercase)
- ✅ Key mesh formatting options:
  - ✅ strip material paths in SMD
  - ✅ non-Valve UV conversion
- ✅ Naming/formatting:
  - ✅ folder-per-model / flat-output toggle
  - ✅ prefix mesh file names with model name
  - ✅ “stricter importers” formatting toggle (header comment + `time` indentation in QC/SMD/VTA/VRD/QCI)
- ✅ Logging/debug:
  - Original: log file + debug info files toggles.
  - Cross-platform: console/UI logs + `manifest.json`; ✅ optional `decompile.log` (Desktop checkbox / CLI `--log`); ✅ optional `debug/` outputs (`debug-info.json` + Crowbar-style accessed-bytes logs like `<model> decompile-MDL.txt`, `...-VVD.txt`, `...-VTX.txt`, `...-PHY.txt`, `...-ANI.txt`).
- ✅ Decompile → Compile handoff shortcut (Desktop: “Use output QC in Compile”).

### Compile (QC → MDL via StudioMDL)

Original: `Crowbar/Widgets/Main Tabs/CompileUserControl.vb`

- ➕ Optional bundled StudioMDL: this repo vendors `tools/MDLForge` (a standalone `studiomdl` build). If a game-provided StudioMDL can't be found, the cross-platform app can fall back to a bundled one under `tools/studiomdl/`.
- ✅ Input modes:
  - Original: file / folder / folder+subfolders.
  - Cross-platform: file / folder / folder+subfolders (CLI + Desktop).
- ✅ Output folder handling:
  - Output-copy with presets (game models / work folder / subfolder / same folder) plus browse; CLI `compile --copy-to <dir>`.
- ✅ StudioMDL invocation:
  - ✅ `-nop4`, `-verbose`
  - ✅ direct options text
  - ✅ definebones workflow (`-definebones` + write/overwrite `.qci`, optionally modify QC)
- ✅ Log files:
  - Captures stdout/stderr; optional `.compile.log` per QC (Desktop checkbox / CLI `--log`).
- ✅ “Use in View”:
  - One-click Compile→View; searches copy-output, game models, and QC-relative paths using `$modelname`.

### View (MDL data viewer + viewer launch)

Original: `Crowbar/Widgets/Main Tabs/ViewUserControl.vb` (ViewerType=View)

- ✅ Launch HLMV (viewer), including “view as replacement”.
- ✅ “Data viewer” panel (auto-runs to show model info).
- ✅ Open mapping tool; ✅ run game.
- ✅ “Use in Decompile”.
- ✅ Override MDL version selection.

Cross-platform status:

- ✅ Launch HLMV via Desktop `View` tab and CLI `view` (Wine supported).
- ✅ Passes `-game` (and `-olddialogs`) to HLMV when a Game Dir (or Steam AppID preset) is available.
- ✅ Open mapping tool + run game via Desktop `View` tab (and `Games` tab).
- ✅ MDL “data viewer” is built into `View` (Data tab; auto-run, shows header counts + lists), and is also available in `Inspect`.
- ✅ “View as replacement” exists (Desktop button + CLI `--replacement`; temp-copy workflow with internal name rewrite).
- ✅ “Override MDL version” UI exists (View → Data tab) for the data viewer/inspect read.

### Pack (folder → VPK/FPX/GMA)

Original: `Crowbar/Widgets/Main Tabs/PackUserControl.vb`

- ✅ Input modes:
  - Original: pack a single folder OR “parent of child folders” (batch).
  - Cross-platform: Desktop + CLI support single-folder + “parent of child folders” (batch).
- ✅ Output path presets:
  - Original: work folder vs parent folder.
  - Cross-platform: explicit output path (single) or output folder (batch), plus preset buttons (Parent folder / Work folder).
- ✅ Supported output formats:
  - ✅ VPK
  - ✅ FPX
  - ✅ GMA
- ✅ Free-form “packer options” text (Desktop: `Pack → Packer opts`; CLI: `pack --opts "<text>"`; for `.vpk` opts use external `vpk` tool via `--vpk-tool` or Steam hints).
- ✅ VPK features:
  - ✅ multi-file VPK option
  - ✅ VPK v2 MD5 sections option
  - ✅ split (MB), preload bytes, VPK version selection (cross-platform adds more knobs here)
- ✅ GMA features:
  - Title/description/author/version, tags textbox, addon.json helper, ignore patterns and whitelist toggle; optional GMAD path via CLI.
- ✅ Operational controls:
  - Skip current folder (batch), log-file toggle, ignore whitelist warnings toggle, cancel/progress supported.

### Publish (Workshop)

Original: `Crowbar/Widgets/Main Tabs/PublishUserControl.vb`

- ✅ Steamworks publish/update/delete/list/quota (via SteamPipe/background process).
- ✅ Rich “My items” grid:
  - ✅ changed marker (“*”)
  - ✅ posted/updated timestamps
  - ✅ visibility/owner columns
  - ✅ search/filter (ID/Owner/Title/Description/All)
- ✅ Item editor fields:
  - ✅ content folder/file selection
  - ✅ preview image selection
  - ✅ title/description/changenote
  - ✅ visibility
  - ✅ tags (app-specific widgets)
- ✅ “Use in Download” context-menu shortcut.

Cross-platform status:

- ✅ Workshop operations exist:
  - ✅ publish/update via Steamworks (`Stunstick.SteamPipe`)
  - ✅ delete via Steamworks (`Stunstick.SteamPipe`)
  - ✅ list my items + quota via Steamworks (`Stunstick.SteamPipe`)
  - ✅ optional SteamCMD publish path (extra; not in original UI)
- ✅ UI parity:
  - Local drafts with dirty marker, search/filter, tag presets, quota/my-items list, and “Use in Download”.

### Options (app behavior + shell integration)

Original: `Crowbar/Widgets/Main Tabs/OptionsUserControl.vb`

- ✅ Single-instance toggle.
- ✅ Auto-open via file associations:
  - `.vpk` / `.gma` / `.fpx` / `.mdl` / `.qc`
  - per-extension “open into tab” routing.
- ✅ Drag-and-drop routing defaults (MDL → Preview/Decompile/View; folder → chosen action).
- ✅ Windows Explorer context menu integration (“Open with Stunstick”, view/decompile/compile on files/folders).

Cross-platform status:

- ✅ Single-instance behavior exists (always on) and activation forwards command-line paths into the running window.
- ✅ Settings UI (persisted JSON) covers work folder, drop-anywhere routing, and per-extension routing for MDL/QC/package files.
- ✅ Windows-only file associations with per-extension selection; register/unregister from Options/Help, double-click routes into the chosen tab (open action).
- ✅ Drag-and-drop supports per-input paths and global routing (configurable via `Options`).

### Update (self-update)

Original: `Crowbar/Widgets/Main Tabs/UpdateUserControl.vb`

- ✅ Check for updates + show changelog.
- ✅ Download update with progress and cancel.
- ✅ Apply update (with options like “copy settings” and “update to new path”).

Cross-platform status:

- 🚫 Out of scope (no in-app updater planned).

### Help / About

Original: `HelpUserControl.vb`, `AboutUserControl.vb`

- ✅ In-app links (tutorial/guide/index/tips), product/about info, author links.

Cross-platform status:

- ✅ In-app `Help`/`About` tabs include quick start guidance, doc links (README, cross-platform notes, parity tracker, CLI), tutorial/guide links, settings path, version, and license.

### Edit / Patch

Original: `EditUserControl.vb`, `PatchUserControl.vb`

- Edit: ❌ effectively empty/placeholder in this repo.
- Patch: 🚫 placeholder in original; out of scope for cross-platform.

Cross-platform status:

- ❌ No equivalents (not implemented).
