using System.Numerics;

namespace Stunstick.Core.Mdl;

public sealed record MdlBone(
	int Index,
	string Name,
	int ParentIndex,
	Vector3 Position,
	Vector3 RotationRadians,
	Vector3 PositionScale,
	Vector3 RotationScale,
	Matrix4x4 PoseToBone,
	int PhysicsBoneIndex,
	int Flags
);
