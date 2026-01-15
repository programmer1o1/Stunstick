namespace Stunstick.Core.Vtx;

public sealed record VtxMaterialReplacementList(
	int LodIndex,
	IReadOnlyList<VtxMaterialReplacement> Replacements
);

public sealed record VtxMaterialReplacement(
	int MaterialIndex,
	string ReplacementMaterial
);

