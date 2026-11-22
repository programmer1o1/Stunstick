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
)
{
	public IReadOnlyList<MdlSequenceBlendAxis> BlendAxes { get; init; } = Array.Empty<MdlSequenceBlendAxis>();
	public IReadOnlyList<float> BoneWeights { get; init; } = Array.Empty<float>();
}

public sealed record MdlSequenceBlendAxis(string Name, float Start, float End);

