using System.Numerics;

namespace Stunstick.Core.Phy;

public sealed record PhyFile(
	string SourcePath,
	PhyHeader Header,
	IReadOnlyList<PhySolid> Solids
);

public sealed record PhyHeader(
	int Size,
	int Id,
	int SolidCount,
	int Checksum
);

public sealed record PhySolid(
	int Size,
	IReadOnlyList<Vector3> Vertices,
	IReadOnlyList<PhyConvexMesh> ConvexMeshes
);

public sealed record PhyConvexMesh(
	int BoneIndex,
	int Flags,
	IReadOnlyList<PhyFace> Faces
);

public sealed record PhyFace(
	ushort VertexIndex0,
	ushort VertexIndex1,
	ushort VertexIndex2
);

