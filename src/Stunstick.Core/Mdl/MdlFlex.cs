namespace Stunstick.Core.Mdl;

public sealed record MdlFlex(
	int Index,
	int FlexDescIndex,
	float Target0,
	float Target1,
	float Target2,
	float Target3,
	int VertCount,
	int FlexDescPartnerIndex,
	byte VertAnimType,
	IReadOnlyList<MdlVertAnim> VertAnims
);

