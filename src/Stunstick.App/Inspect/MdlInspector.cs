using Stunstick.Core.Mdl;

namespace Stunstick.App.Inspect;

internal static class MdlInspector
{
	public static MdlInspectResult Inspect(string mdlPath, MdlInspectOptions? options, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(mdlPath))
		{
			throw new ArgumentException("Path is required.", nameof(mdlPath));
		}

		if (!File.Exists(mdlPath))
		{
			throw new FileNotFoundException("File not found.", mdlPath);
		}

		cancellationToken.ThrowIfCancellationRequested();

		var model = MdlReader.Read(mdlPath, versionOverride: options?.VersionOverride);
		cancellationToken.ThrowIfCancellationRequested();

		var header = model.Header;
		return new MdlInspectResult(
			SourcePath: model.SourcePath,
			Version: header.Version,
			Checksum: header.Checksum,
			Name: header.Name,
			Length: header.Length,
			Flags: header.Flags,
			BoneCount: header.BoneCount,
			LocalAnimationCount: header.LocalAnimationCount,
			LocalSequenceCount: header.LocalSequenceCount,
			TextureCount: header.TextureCount,
			TexturePathCount: header.TexturePathCount,
			SkinReferenceCount: header.SkinReferenceCount,
			SkinFamilyCount: header.SkinFamilyCount,
			BodyPartCount: header.BodyPartCount,
			FlexDescCount: header.FlexDescCount,
			FlexControllerCount: header.FlexControllerCount,
			FlexRuleCount: header.FlexRuleCount,
			AnimBlockCount: header.AnimBlockCount,
			TexturePaths: model.TexturePaths,
			Textures: model.Textures.Select(texture => texture.PathFileName).ToArray(),
			BodyParts: model.BodyParts.Select(bodyPart => bodyPart.Name).ToArray(),
			Bones: model.Bones.Select(bone => bone.Name).ToArray(),
			Sequences: model.SequenceDescs.Select(sequence => sequence.Name).ToArray(),
			Animations: model.AnimationDescs.Select(animation => animation.Name).ToArray());
	}
}
