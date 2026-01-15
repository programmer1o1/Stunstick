namespace Stunstick.Core.Mdl;

public sealed record MdlBodyPart(
	int Index,
	string Name,
	IReadOnlyList<MdlSubModel> Models
);

