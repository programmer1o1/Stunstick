namespace Stunstick.Core.Mdl;

public sealed record MdlSubModel(
	int Index,
	string Name,
	int VertexCount,
	IReadOnlyList<MdlMesh> Meshes
);

