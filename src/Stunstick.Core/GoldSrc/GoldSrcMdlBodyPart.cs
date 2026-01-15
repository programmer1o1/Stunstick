namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlBodyPart(
	int Index,
	string Name,
	IReadOnlyList<GoldSrcMdlModel> Models
);

