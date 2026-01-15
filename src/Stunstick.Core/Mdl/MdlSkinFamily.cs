namespace Stunstick.Core.Mdl;

public sealed record MdlSkinFamily(
	int Index,
	IReadOnlyList<int> TextureIndexes
);

