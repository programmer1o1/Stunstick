namespace Stunstick.Core.Mdl;

public sealed record MdlEmbeddedSections(
	int VtxOffset,
	int VtxSize,
	int VvdOffset,
	int VvdSize,
	int VvcOffset,
	int VvcSize,
	int PhyOffset,
	int PhySize
);

