using System.Numerics;

namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlBone(
	int Index,
	string Name,
	int ParentIndex,
	Vector3 Position,
	Vector3 RotationRadians
);

