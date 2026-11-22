namespace Stunstick.Core.Mdl;

[Flags]
public enum MdlJiggleFlags
{
	Flexible = 0x01,
	Rigid = 0x02,
	YawConstraint = 0x04,
	PitchConstraint = 0x08,
	AngleConstraint = 0x10,
	LengthConstraint = 0x20,
	BaseSpring = 0x40,
	Boing = 0x80
}

public sealed record MdlJiggleBone(MdlJiggleFlags Flags, float Length, float TipMass,
	float YawStiffness, float YawDamping, float PitchStiffness, float PitchDamping,
	float AlongStiffness, float AlongDamping, float AngleLimit,
	MdlJiggleConstraint Yaw, MdlJiggleConstraint Pitch,
	float BaseMass, float BaseStiffness, float BaseDamping,
	MdlJiggleConstraint Left, MdlJiggleConstraint Up, MdlJiggleConstraint Forward,
	MdlJiggleBoing? Boing);

public readonly record struct MdlJiggleConstraint(float Min, float Max, float Friction, float Bounce = 0);

public readonly record struct MdlJiggleBoing(float ImpactSpeed, float ImpactAngleCosine, float DampingRate, float Frequency, float Amplitude);
