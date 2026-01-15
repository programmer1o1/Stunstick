namespace Stunstick.Core.Vtx;

public sealed record VtxModel(
	int LodCount,
	IReadOnlyList<VtxModelLod> Lods
);

