using System.Numerics;

namespace Stunstick.Core.Mdl;

public sealed record MdlEyeball(
	int Index, string Name, int BoneIndex, Vector3 Origin, float ZOffset, float Radius, float IrisScale,
	MdlEyelid UpperLid, MdlEyelid LowerLid);

public sealed record MdlEyelid(int FlexDescIndex, int LowererFlexDescIndex, Vector3 Targets);
