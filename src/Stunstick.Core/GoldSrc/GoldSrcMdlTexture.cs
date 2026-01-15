namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlTexture(
	int Index,
	string FileName,
	uint Width,
	uint Height,
	uint Flags,
	uint DataOffset
);
