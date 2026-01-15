namespace Stunstick.Core.Mdl;

public sealed record MdlFlexController(
	int Index,
	string Type,
	string Name,
	int LocalToGlobal,
	float Min,
	float Max
);

