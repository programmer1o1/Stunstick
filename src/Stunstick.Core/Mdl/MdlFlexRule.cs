namespace Stunstick.Core.Mdl;

public sealed record MdlFlexRule(
	int Index,
	int FlexIndex,
	IReadOnlyList<MdlFlexOp> Ops
);

