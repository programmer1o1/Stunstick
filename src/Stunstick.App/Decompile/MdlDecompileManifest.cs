using Stunstick.Core.Mdl;

namespace Stunstick.App.Decompile;

public sealed record MdlDecompileManifest(
	string SourceMdlPath,
	string OutputFolder,
	string OriginalFilesFolder,
	MdlHeader Header,
	IReadOnlyList<MdlBone> Bones,
	IReadOnlyList<string> CopiedOriginalFiles,
	string GeneratedBy
);

