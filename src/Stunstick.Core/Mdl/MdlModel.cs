namespace Stunstick.Core.Mdl;

public sealed record MdlModel(
	string SourcePath,
	MdlHeader Header,
	IReadOnlyList<MdlBone> Bones,
	IReadOnlyList<string> TexturePaths,
	IReadOnlyList<MdlTexture> Textures,
	IReadOnlyList<MdlSkinFamily> SkinFamilies,
	IReadOnlyList<MdlBodyPart> BodyParts,
	IReadOnlyList<MdlFlexDesc> FlexDescs,
	IReadOnlyList<MdlFlexController> FlexControllers,
	IReadOnlyList<MdlFlexRule> FlexRules,
	IReadOnlyList<MdlAnimationDesc> AnimationDescs,
	IReadOnlyList<MdlSequenceDesc> SequenceDescs
);
