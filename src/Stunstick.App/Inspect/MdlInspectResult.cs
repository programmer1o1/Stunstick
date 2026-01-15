namespace Stunstick.App.Inspect;

public sealed record MdlInspectResult(
	string SourcePath,
	int Version,
	int Checksum,
	string Name,
	int Length,
	int Flags,
	int BoneCount,
	int LocalAnimationCount,
	int LocalSequenceCount,
	int TextureCount,
	int TexturePathCount,
	int SkinReferenceCount,
	int SkinFamilyCount,
	int BodyPartCount,
	int FlexDescCount,
	int FlexControllerCount,
	int FlexRuleCount,
	int AnimBlockCount,
	IReadOnlyList<string> TexturePaths,
	IReadOnlyList<string> Textures,
	IReadOnlyList<string> BodyParts,
	IReadOnlyList<string> Bones,
	IReadOnlyList<string> Sequences,
	IReadOnlyList<string> Animations);

