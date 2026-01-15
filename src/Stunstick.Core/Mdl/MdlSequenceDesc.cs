namespace Stunstick.Core.Mdl;

public sealed record MdlSequenceDesc(
	int Index,
	long OffsetStart,
	string Name,
	int Flags,
	int BlendCount,
	int GroupSize0,
	int GroupSize1,
	int AnimIndexOffset,
	IReadOnlyList<short> AnimDescIndexes
);

