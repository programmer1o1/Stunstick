namespace Stunstick.Core.Vtx;

public sealed record VtxBodyPart(
	int ModelCount,
	IReadOnlyList<VtxModel> Models
);

