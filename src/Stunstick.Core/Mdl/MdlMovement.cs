using System.Numerics;

namespace Stunstick.Core.Mdl;

public sealed record MdlMovement(
	int EndFrameIndex,
	int MotionFlags,
	float V0,
	float V1,
	float AngleDegrees,
	Vector3 Vector,
	Vector3 Position
);

