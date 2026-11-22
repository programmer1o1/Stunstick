using System.Numerics;

namespace Stunstick.Core.Phy;

public sealed record PhyFile(
	string SourcePath,
	PhyHeader Header,
	IReadOnlyList<PhySolid> Solids
)
{
	public PhyCollisionMetadata? Collision { get; init; }
}

public sealed record PhyCollisionMetadata(
	double? TotalMass,
	string? RootBone,
	bool Concave,
	IReadOnlyList<PhyCollisionSolidMetadata> Solids,
	IReadOnlyList<PhyJointLimit> JointLimits,
	bool? SelfCollisions,
	IReadOnlyList<(int First, int Second)> CollisionPairs
);

public sealed record PhyCollisionSolidMetadata(
	int Index,
	string Name,
	double? Mass,
	double? MassBias,
	double? Inertia,
	double? Damping,
	double? RotationDamping
);

public sealed record PhyJointLimit(int ChildIndex, char Axis, double Minimum, double Maximum, double Friction);

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

