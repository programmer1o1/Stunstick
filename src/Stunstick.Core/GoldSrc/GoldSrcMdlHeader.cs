namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlHeader(
	uint Id,
	int Version,
	string Name,
	int FileSize,
	int Flags,
	int BoneCount,
	int BoneOffset,
	int TextureCount,
	int TextureOffset,
	int BodyPartCount,
	int BodyPartOffset
);

