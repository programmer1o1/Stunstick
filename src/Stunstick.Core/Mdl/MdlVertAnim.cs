namespace Stunstick.Core.Mdl;

public sealed record MdlVertAnim(
	ushort Index,
	byte Speed,
	byte Side,
	ushort Delta0,
	ushort Delta1,
	ushort Delta2,
	ushort NDelta0,
	ushort NDelta1,
	ushort NDelta2,
	short? WrinkleDelta
);

