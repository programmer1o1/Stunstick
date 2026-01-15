namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlMesh(
	int Index,
	int SkinRef,
	IReadOnlyList<GoldSrcMdlStripOrFan> StripsAndFans
);

