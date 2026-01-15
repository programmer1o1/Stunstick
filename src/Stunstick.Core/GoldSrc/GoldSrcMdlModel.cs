using System.Numerics;

namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlModel(
	int Index,
	string Name,
	IReadOnlyList<byte> VertexBoneInfos,
	IReadOnlyList<byte> NormalBoneInfos,
	IReadOnlyList<Vector3> Vertexes,
	IReadOnlyList<Vector3> Normals,
	IReadOnlyList<GoldSrcMdlMesh> Meshes
);

