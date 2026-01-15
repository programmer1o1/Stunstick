namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlFile(
	string SourcePath,
	GoldSrcMdlHeader Header,
	IReadOnlyList<GoldSrcMdlBone> Bones,
	IReadOnlyList<GoldSrcMdlTexture> Textures,
	IReadOnlyList<GoldSrcMdlBodyPart> BodyParts
);

