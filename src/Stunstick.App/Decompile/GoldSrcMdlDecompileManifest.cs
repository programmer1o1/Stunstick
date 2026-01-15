using Stunstick.Core.GoldSrc;

namespace Stunstick.App.Decompile;

public sealed record GoldSrcMdlDecompileManifest(
	string SourceMdlPath,
	string OutputFolder,
	string OriginalFilesFolder,
	GoldSrcMdlHeader Header,
	IReadOnlyList<GoldSrcMdlBone> Bones,
	IReadOnlyList<string> CopiedOriginalFiles,
	string GeneratedBy
);

