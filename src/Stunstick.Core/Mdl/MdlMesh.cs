namespace Stunstick.Core.Mdl;

public sealed record MdlMesh(
	int Index,
	int MaterialIndex,
	int VertexCount,
	int VertexIndexStart,
	IReadOnlyList<MdlFlex> Flexes,
	IReadOnlyList<int> LodVertexCount
);
