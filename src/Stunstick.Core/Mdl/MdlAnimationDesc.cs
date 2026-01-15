namespace Stunstick.Core.Mdl;

public sealed record MdlAnimationDesc(
	int Index,
	long OffsetStart,
	string Name,
	float Fps,
	int Flags,
	int FrameCount,
	IReadOnlyList<MdlMovement> Movements,
	int AnimBlock,
	int AnimOffset,
	int SectionOffset,
	int SectionFrameCount
);
