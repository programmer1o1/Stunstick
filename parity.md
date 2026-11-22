# Stunstick parity tracker — original Crowbar (WinForms VB) vs cross-platform (.NET 8)

Stunstick covers the main Crowbar workflows, with known gaps in legacy MDL support, the Workshop publish grid, and Windows Explorer actions.

Compared codebases:
- **Original app**: bundled Crowbar 0.74 (fork point `0d46f3b`), built from `Crowbar.sln` (VB.NET WinForms, .NET Framework 4)
- **Cross-platform fork (Stunstick)**: `Stunstick.CrossPlatform.sln` (C#/.NET 8) — `src/Stunstick.App` (logic) + `src/Stunstick.Desktop` (Avalonia UI) + `src/Stunstick.Cli` (CLI)

## Legend

- ✅ **Implemented** — the main workflow has a comparable implementation
- 🟡 **Partial** — a comparable workflow exists with a known gap in inputs, behavior, or controls
- ❌ **Missing** — no equivalent feature yet
- 🚫 **Excluded** — intentional omission or inactive Crowbar placeholder

Statuses reflect source review. See [verification coverage](#verification-coverage) for tests and open comparisons.

## Scope decisions (intentional differences)

- 🟡 Windows file associations register the standard `open` action. Crowbar's Explorer commands for viewing, decompiling, and compiling have no Stunstick equivalent. macOS/Linux file associations are outside the current implementation. [Windows registration](src/Stunstick.Desktop/MainWindow.axaml.cs#L2141)
- 🚫 The Crowbar in-app updater is out of scope.
- ✅ Both apps offer a single-instance toggle. Stunstick defaults to enabled and reads the preference at launch. [Crowbar startup](Crowbar/Core/-%20Application/Main.vb#L40) · [Stunstick startup](src/Stunstick.Desktop/Program.cs#L11)

## Cross-platform-only additions (not in original)

- ➕ Scriptable CLI for most workflows (`src/Stunstick.Cli/Program.cs`): `inspect`, `unpack`, `pack`, `decompile`, `compile`, `view`, `download`, `publish`, `delete`, `quota`, `list`.
- ➕ Windows/Linux/macOS support; can launch Windows-only tools via Wine (`studiomdl.exe`, `hlmv.exe`, `hammer.exe`) when needed.
- ➕ Workshop helper process `Stunstick.SteamPipe` (Steamworks.NET) to isolate Steamworks calls from the UI/CLI process.

## Tab mapping (original → cross-platform)

The table covers the bundled Crowbar main window and its inactive Edit and Patch controls. [Main window designer](Crowbar/Widgets/-%20Application/MainForm.Designer.vb)

| Original tab | Original entrypoint | Cross-platform equivalent | Status | Key differences |
|---|---|---|---:|---|
| Set Up Games | `SetUpGamesUserControl.vb` | Desktop: `Games` tab | ✅ | Steam presets are read-only (overrides supported); custom presets add/clone/delete; macro + extra library root editor added. |
| Download | `DownloadUserControl.vb` | Desktop: `Workshop → Download`; CLI: `download` | ✅ | Documents and Work folder presets are available. Stunstick also supports cached content and optional SteamCMD fallback. |
| Unpack | `UnpackUserControl.vb` | Desktop: `Explore`; CLI: `unpack` | ✅ | Same/subfolder/work/addons presets, keep-path toggle, saved searches, CRC/MD5, temp extract + use-in flows. |
| Preview | `ViewUserControl.vb` (ViewerType=Preview) | Desktop: `View` (built-in preview) + `Inspect` | 🟡 | Data viewer and HLMV launch are available. The built-in renderer rejects GoldSource v10 and compressed MDLZ models, and Stunstick's Source reader covers fewer model versions. |
| Decompile | `DecompileUserControl.vb` | Desktop: `Decompile`; CLI: `decompile` | 🟡 | Core outputs and batch modes exist. Several Crowbar legacy MDL readers are missing. See [MDL format coverage](#mdl-format-coverage). |
| Edit | `EditUserControl.vb` | — | 🚫 | Inactive placeholder in the bundled Crowbar source. |
| Compile | `CompileUserControl.vb` | Desktop: `Compile`; CLI: `compile` | ✅ | Folder input + recursion; copy-output presets (game models/work/subfolder/same folder); per-QC log; improved “Use in View”. |
| View | `ViewUserControl.vb` (ViewerType=View) | Desktop: `View`; CLI: `view` | 🟡 | External HLMV launch and view-as-replacement exist. The integrated preview and data reader have the format limits above. |
| Pack | `PackUserControl.vb` | Desktop: `Pack`; CLI: `pack` | ✅ | Single/batch modes, presets (same/parent/work), skip current folder, free-form opts, GMA tags/ignore whitelist helper. |
| Publish | `PublishUserControl.vb` | Desktop: `Workshop → Publish`; CLI: `publish/delete/quota/list` | 🟡 | Steamworks operations, posted and updated dates, visibility, owner, and field-specific search are available. Crowbar offers sortable grid columns. |
| Patch | `PatchUserControl.vb` | — | 🚫 | Inactive Crowbar control with file browse and go-to handlers. |
| Options | `OptionsUserControl.vb` | Desktop: `Options` tab | 🟡 | Drop routing, Windows file associations, and an optional single-instance mode exist. Crowbar's Explorer action menu is missing. |
| Update | `UpdateUserControl.vb` | — | 🚫 | Out of scope. |
| Help | `HelpUserControl.vb` | Desktop: `Help` tab | ✅ | Quick start, docs/CLI/parity links, tutorial/guide/FAQ/issues/changelog/repo links, settings path + log/version helpers. |
| About | `AboutUserControl.vb` | Desktop: `About` tab | ✅ | Shows version and opens license. |

## App-wide behavior parity (not tied to a single tab)

### Single-instance behavior + activation

- ✅ **Original:** optional single-instance mode (Options → “Single instance”); activates existing window and passes command line (`Crowbar/Core/- Application/Main.vb`).
- ✅ **Cross-platform:** the Options toggle applies on launch. When enabled, the named pipe forwards arguments to the running window. [IPC implementation](src/Stunstick.Desktop/SingleInstanceIpc.cs)

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
- ✅ The engine selector and per-engine tool fields cover GoldSource, Source, and Source 2 presets. The desktop reports Source 2 workflow support as unavailable when it detects a Source 2-only install. [Game detection and tool paths](src/Stunstick.Desktop/MainWindow.axaml.cs#L6955)

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
- 🟡 Preview is integrated into `View` with a built-in wireframe and data viewer. The geometry loaders reject GoldSource v10 and MDLZ. HLMV launch uses a separate path. [Preview loader](src/Stunstick.App/Viewer/MdlPreviewLoader.cs#L50) · [HLMV launcher](src/Stunstick.App/StunstickApplication.cs#L807)
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
  - ✅ Collision QC with mass, ragdoll constraints, and collision pairs when the PHY contains the corresponding metadata
  - ✅ Blank bodygroup choices, eyeball and eyelid directives, and IK chains for supported Source models
- ✅ Optional outputs:
  - ✅ bone animation SMD output (supports external `.ani` animblocks, root and piecewise motion, and corrective SMDs for delta sequences)
  - ✅ QC weightlists reconstructed from the compiled sequence bone weights, including blended sequences
  - ✅ DeclareSequence QCI file output (`<model>_DeclareSequence.qci`, `$declaresequence` lines)
  - ✅ VTA output (flex frames + QC `flexfile` mapping)
  - ✅ 8-bit indexed texture BMP export (GoldSrc), preserving the palette and pixel indices
  - ✅ procedural bones VRD export and QC jiggle-bone reconstruction
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
- ✅ Animation-only Source models can produce a QC without VTX companions. QC output retains blended sequence grids, their pose parameter names and ranges, frame rates, and root-motion extraction commands.

#### MDL format coverage

| Input | Crowbar 0.74 | Stunstick decompile |
|---|---|---|
| GoldSource `IDST` v10 | Parser available | Dedicated GoldSource path |
| Compressed `MDLZ` v14 | Recognizes the header, but the factory has no v14 parser | Attempts to decompress to an `IDST` v10 payload |
| Legacy v6 and v2531 | Dedicated parser paths | Unsupported |
| Legacy v27–32 and v35–37 | Parser paths marked incomplete in the factory | Unsupported |
| Source v44–49, v52–56, v58–59 | Parser paths available, with v52 and v53 marked unfinished in the factory | Accepted by the Source reader. Output equivalence remains unverified across these versions. |

[Crowbar model factory and header reader](Crowbar/Core/GameModel/-%20Base/SourceModel.vb#L8) · [Stunstick Source version guard](src/Stunstick.Core/Mdl/MdlReader.cs#L214) · [Stunstick GoldSource and MDLZ dispatch](src/Stunstick.App/Decompile/MdlDecompiler.cs#L80)

### Compile (QC → MDL via StudioMDL)

Original: `Crowbar/Widgets/Main Tabs/CompileUserControl.vb`

- ➕ Optional bundled StudioMDL: this repo vendors `tools/MDLForge` (a standalone `studiomdl` build). If a game-provided StudioMDL can't be found, the cross-platform app can fall back to a bundled one under `tools/studiomdl/`.
- On Apple Silicon, the bundled StudioMDL build uses x86-64 under Rosetta. Both macOS release packages include StudioMDL and the portable `libvphysics.dylib` compiled from vendored source. StudioMDL loads the library automatically for native `.phy` generation. [Build script](scripts/toolchain/build_studiomdl.sh) · [Release workflow](.github/workflows/release.yml) · [Collision notes](tools/studiomdl/README.md)
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
- Free-form packer options (Desktop `Pack → Packer opts` and CLI `pack --opts "<text>"`) can route VPK packing through an external VPK tool. The FPX writer rejects direct options and external VPK tools. [Packer dispatch](src/Stunstick.App/StunstickApplication.cs#L1220)
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
- 🟡 Published-item management:
  - Local drafts have a dirty marker. Published-item rows show ID, title, posted and updated dates, visibility, and owner when Steam supplies those details. The owner uses the signed-in Steam display name when available and falls back to the Steam ID. [List item formatting](src/Stunstick.Desktop/MainWindow.axaml.cs#L246)
  - Search supports ID, title, description, and owner. All-fields search also matches tags. Crowbar's grid offers per-column sorting. [Desktop controls](src/Stunstick.Desktop/MainWindow.axaml#L918) · [Crowbar grid setup](Crowbar/Widgets/Main%20Tabs/PublishUserControl.vb#L174)
  - Tag presets, quota, and “Use in Download” are available.

### Options (app behavior + shell integration)

Original: `Crowbar/Widgets/Main Tabs/OptionsUserControl.vb`

- ✅ Single-instance toggle.
- ✅ Auto-open via file associations:
  - `.vpk` / `.gma` / `.fpx` / `.mdl` / `.qc`
  - per-extension “open into tab” routing.
- ✅ Drag-and-drop routing defaults (MDL → Preview/Decompile/View; folder → chosen action).
- ✅ Windows Explorer context menu integration (“Open with Crowbar”, view/decompile/compile on files/folders). [Crowbar Options bindings](Crowbar/Widgets/Main%20Tabs/OptionsUserControl.vb#L40)

Cross-platform status:

- ✅ The single-instance toggle is persisted separately from window settings. With the option enabled, activation forwards command-line paths into the running window.
- ✅ Settings UI (persisted JSON) covers work folder, drop-anywhere routing, and per-extension routing for MDL/QC/package files.
- ✅ Windows-only file associations with per-extension selection; register/unregister from Options/Help, double-click routes into the chosen tab (open action).
- ❌ Crowbar's Explorer action menu for files and folders has no equivalent.
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

- 🚫 Edit is an empty control in the bundled source.
- 🚫 Patch has browse and go-to handlers and no patch operation. Neither control appears in Crowbar's main tab list. [Edit control](Crowbar/Widgets/Main%20Tabs/EditUserControl.vb) · [Patch control](Crowbar/Widgets/Main%20Tabs/PatchUserControl.vb) · [Main window designer](Crowbar/Widgets/-%20Application/MainForm.Designer.vb)

## Verification coverage

- CI is configured to build and run the Stunstick test project on Windows, macOS, and Linux. The checked-in tests cover header serialization, QC parsing and dry-run checks, synthetic shadow LOD output, and ZIP post-processing for Workshop downloads. [CI workflow](.github/workflows/ci.yml) · [Tests](src/Stunstick.App.Tests/)
- CLI round trips on macOS covered a simple v44 mesh and a v49 animation-only model. The v44 mesh triangles matched, and the yaw difference was 0.0099°. The v49 comparison matched animation names, frame rates and counts, sequence blend grids, and all 56 root-motion segment end frames and flags. Re-exported bone transforms differed by up to 0.0435 model units in translation and 0.418° in orientation. These fixtures cover only part of the supported formats and QC features.
- Live Garry's Mod Workshop listing and quota queries succeeded on Apple Silicon macOS. The SteamPipe listing returned owner names and IDs, posted and updated timestamps, and visibility.
- A live Garry's Mod Steamworks download produced a GMA archive. Stunstick inspected it and extracted all 21 files with CRC verification. A separate synthetic Workshop item was published, updated, and deleted. Steam reported Private visibility before and after the update and confirmed all pre-existing items were still listed after cleanup.
- A CLI GMA round trip preserved the relative paths and bytes of a nested text file and a binary file. CRC verification rejected a modified payload in a second copy of the archive.
- On macOS, dropping the GMA from Finder populated Explore. The View-to-Decompile handoff preserved the pallet model path, and Decompile-to-Compile populated the generated QC path.
- For synthetic GoldSource textures, the indexed BMP writer reproduced Crowbar's output byte for byte at a width of four. At a width of three, the palette, pixels, and padding matched, and Stunstick kept the original width in the BMP header.
- The Crowbar 0.74 decompile worker ran through CrossOver on the same Kleiner, wood pallet, and Buggy fixtures as the Stunstick CLI. After fixing SMD decimal precision, all 28 reference and LOD meshes match in order and winding, materials, vertex positions, normals, UVs, and skin weights. The comparison covered 43,455 triangles and 130,365 vertex rows. The only text differences in the triangle sections are signed zeroes. Physics triangle counts matched for all three models.
- Kleiner's VTA matches Crowbar 0.74 frame by frame after widening the base positions and normals before applying flex deltas. All 35 frame labels and all 17,890 rows match in vertex index, position, and normal at the exported precision. The only numeric text differences are 24 signed zeroes.
- The corrected exports match Crowbar's collision masses for the wood pallet, Buggy, and Kleiner (20, 800, and 90). Kleiner's collision QC has the same 42 joint constraints and 13 collision pairs. Its QC also exports two eyeballs, four eyelids, and four IK chains. Buggy exports 28 corrective animation SMDs, and both Buggy and Kleiner preserve their blank bodygroup choices.
- Buggy's QC matches Crowbar's 13 weightlists and 38 animation assignments. After recompilation, all 31 bone weights in each of the 36 sequences match the original MDL. The compiled format stores combined sequence weights, which Stunstick applies to each animation in a blend.
- Kleiner's VRD matches Crowbar's 13 procedural helpers and 67 triggers numerically. The bundled compiler preserved all 67 triggers, with a maximum orientation difference of 0.000024° on read-back. Sierra's 18 jiggle-bone rules match Crowbar's QC, and every field round-tripped exactly with the bundled compiler on the full model.
- The bundled macOS StudioMDL rebuilt the full Sierra export, which has 77,450 triangles in its largest SMD. Stunstick read all five meshes from the resulting 64-bit MDL records. All 95,706 render triangles matched their input materials and geometry, with a maximum corner displacement of 0.000034 model units. The re-exported physics mesh retained all 1,400 triangles. The VVD, VTX, and PHY payloads matched the newer local MDLForge build, and their checksums matched the rebuilt MDL. An SMD above the new 262,144-triangle capacity failed with a compiler error.
- All three Stunstick exports recompiled to MDL and PHY with the bundled StudioMDL and the macOS MDLForge physics library. On read-back, the matching checksums, masses, and collision triangle counts (72, 96, and 804) were verified. Kleiner preserved 42 joint constraints and 13 collision pairs by count. The Sierra model's generated `.phy` also worked in-game in a user test. In-game animation and physics parity for the three comparison fixtures remains unverified.
- The decompile comparison script defaults to `~/Documents/decompiled models/` and accepts `--reference-root` for a different corpus. It requires manifests that point to readable source models and fails on unreadable inputs or differing outputs after checking both UV conventions. The default corpus was absent during the parity review. [Decompile comparison script](scripts/regression/decompile_regress.sh)

| Area | Side-by-side check still open |
|---|---|
| Model decompile | Repeat the mesh, skin-weight, facial animation, sequence weightlist, and procedural bone comparisons across more model versions and rule types. Check GoldSource texture exports on real embedded and companion textures and verify recompilation. Verify the rebuilt models in-game. |
| Compile and view | Use the same game, StudioMDL, and HLMV versions. Compare batch output paths, definebones, view-as-replacement, and the bundled-tool or Wine path where applicable. |
| Pack and unpack | Cross-open representative archives, compare extracted paths and bytes, and exercise split VPKs, integrity checks, and GMA whitelist handling. |
| Workshop | Verify direct web downloads and compressed LZMA-to-GMA conversion. Repeat the live Steamworks checks on additional games and platforms. |
| Desktop behavior | Extend drag-and-drop and cross-tab handoff checks to more routes and platforms. Verify separate launches with single-instance mode disabled, and argument forwarding with it enabled. On Windows, verify file-association registration. |
