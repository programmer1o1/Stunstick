namespace Stunstick.Core.Vtx;

public sealed record VtxModelLod(
	int MeshCount,
	float SwitchPoint,
	IReadOnlyList<VtxMesh> Meshes,
	bool UsesFacial
);
