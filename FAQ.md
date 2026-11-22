# Stunstick FAQ

## How does Stunstick relate to Crowbar?

Stunstick is a cross-platform fork with a C#/.NET 8 CLI and an Avalonia desktop app. The original VB.NET WinForms application remains in `Crowbar.sln`. [Original Crowbar downloads and discussion](https://steamcommunity.com/groups/CrowbarTool)

## Does every Crowbar model work?

Use the [model format coverage table](parity.md#mdl-format-coverage) to check your version. In particular, the built-in preview has different format coverage from the decompiler. You can inspect a model’s version in the **Inspect** tab or with `stunstick inspect`.

## Why did a decompile produce a skeleton but no mesh?

For Source models, check that the matching `.vvd` and `.vtx` files are beside the MDL if the version stores them separately. Check **Write ref SMDs** in the desktop options and review the decompile log for missing files.

## Why did compilation produce an MDL but no PHY?

The QC must include a collision directive and reference a collision SMD. StudioMDL also needs a compatible physics backend. The bundled macOS build supplies one. For other compilers and platforms, see the [collision setup notes](tools/studiomdl/README.md#collision-models-on-linuxmacos).

## How do I report a bug?

Include the platform, Stunstick version, model format or Steam App ID, steps or CLI command, and the relevant log. The desktop **Help** tab can copy or save the log. Attach a small sample you have permission to share if the problem depends on a particular asset. [Stunstick issue tracker](https://github.com/programmer1o1/Stunstick/issues)
