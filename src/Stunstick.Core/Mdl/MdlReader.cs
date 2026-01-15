using System.Numerics;
using System.Text;

namespace Stunstick.Core.Mdl;

public static class MdlReader
{
	public static MdlModel Read(string path, int? versionOverride = null)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(stream, path, versionOverride);
	}

	public static MdlModel Read(Stream stream, string sourcePath, int? versionOverride = null)
	{
		if (stream is null)
		{
			throw new ArgumentNullException(nameof(stream));
		}

		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			throw new ArgumentException("Source path is required.", nameof(sourcePath));
		}

		if (!stream.CanRead || !stream.CanSeek)
		{
			throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
		}

		stream.Seek(0, SeekOrigin.Begin);
		if (stream.Length < 4 + 4 + 4)
		{
			throw new InvalidDataException("File is too small to be a valid MDL.");
		}

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

		var id = reader.ReadUInt32();
		if (id != MdlConstants.Idst)
		{
			throw new NotSupportedException("Unsupported MDL (expected IDST signature).");
		}

		var version = reader.ReadInt32();
		var checksum = reader.ReadInt32();

		var effectiveVersion = versionOverride ?? version;
		if (!IsSupportedVersion(effectiveVersion))
		{
			if (versionOverride is not null)
			{
				throw new NotSupportedException($"Unsupported MDL version: {version} (override: {versionOverride.Value}; currently supported: 44–49, 52–56, 58–59).");
			}

			throw new NotSupportedException($"Unsupported MDL version: {version} (currently supported: 44–49, 52–56, 58–59).");
		}

		string name;
		var length = 0;

		if (effectiveVersion == 53)
		{
			var nameCopyOffset = reader.ReadInt32();
			var returnPosition = stream.Position;
			var nameCopy = nameCopyOffset > 0
				? ReadNullTerminatedStringAt(stream, nameCopyOffset, maxBytes: 256)
				: string.Empty;
			stream.Seek(returnPosition, SeekOrigin.Begin);

			var nameFromHeader = ReadFixedString(reader, 64);
			length = reader.ReadInt32();

			name = string.IsNullOrWhiteSpace(nameCopy) ? nameFromHeader : nameCopy.Trim();
		}
		else
		{
			name = ReadFixedString(reader, 64);
			length = reader.ReadInt32();
		}

		// Skip vectors: eyePosition, illumPosition, hullMin, hullMax, viewBbMin, viewBbMax
		SkipBytes(reader, 6 * 3 * sizeof(float));

		var flags = reader.ReadInt32();
		var boneCount = reader.ReadInt32();
		var boneOffset = reader.ReadInt32();

		_ = reader.ReadInt32(); // numbonecontrollers
		_ = reader.ReadInt32(); // bonecontrollerindex
		_ = reader.ReadInt32(); // numhitboxsets
		_ = reader.ReadInt32(); // hitboxsetindex

		var localAnimationCount = reader.ReadInt32();
		var localAnimationOffset = reader.ReadInt32();
		var localSequenceCount = reader.ReadInt32();
		var localSequenceOffset = reader.ReadInt32();

		_ = reader.ReadInt32(); // activitylistversion
		_ = reader.ReadInt32(); // eventsindexed

		var textureCount = reader.ReadInt32();
		var textureOffset = reader.ReadInt32();
		var texturePathCount = reader.ReadInt32();
		var texturePathOffset = reader.ReadInt32();

		var skinReferenceCount = reader.ReadInt32();
		var skinFamilyCount = reader.ReadInt32();
		var skinFamilyOffset = reader.ReadInt32();

		var bodyPartCount = reader.ReadInt32();
		var bodyPartOffset = reader.ReadInt32();

		var localAttachmentCount = reader.ReadInt32();
		var localAttachmentOffset = reader.ReadInt32();
		_ = reader.ReadInt32(); // localnode count
		_ = reader.ReadInt32(); // localnode offset
		_ = reader.ReadInt32(); // localnodename offset

		var flexDescCount = reader.ReadInt32();
		var flexDescOffset = reader.ReadInt32();
		var flexControllerCount = reader.ReadInt32();
		var flexControllerOffset = reader.ReadInt32();
		var flexRuleCount = reader.ReadInt32();
		var flexRuleOffset = reader.ReadInt32();

		_ = reader.ReadInt32(); // ikchain count
		_ = reader.ReadInt32(); // ikchain offset
		_ = reader.ReadInt32(); // mouth count
		_ = reader.ReadInt32(); // mouth offset
		_ = reader.ReadInt32(); // localposeparam count
		_ = reader.ReadInt32(); // localposeparam offset
		_ = reader.ReadInt32(); // surfaceprop offset
		_ = reader.ReadInt32(); // keyvalue offset
		_ = reader.ReadInt32(); // keyvalue size
		_ = reader.ReadInt32(); // localikautoplaylock count
		_ = reader.ReadInt32(); // localikautoplaylock offset
		_ = reader.ReadSingle(); // mass
		_ = reader.ReadInt32(); // contents
		_ = reader.ReadInt32(); // includemodel count
		_ = reader.ReadInt32(); // includemodel offset
		_ = reader.ReadInt32(); // virtualmodel ptr

		var animBlockNameOffset = reader.ReadInt32();
		var animBlockCount = reader.ReadInt32();
		var animBlockOffset = reader.ReadInt32();
		_ = reader.ReadInt32(); // animblockmodel ptr

		var header = new MdlHeader(
			Id: id,
			Version: version,
			Checksum: checksum,
			Name: name,
			Length: length,
			Flags: flags,
			BoneCount: boneCount,
			BoneOffset: boneOffset,
			LocalAnimationCount: localAnimationCount,
			LocalAnimationOffset: localAnimationOffset,
			LocalSequenceCount: localSequenceCount,
			LocalSequenceOffset: localSequenceOffset,
			TextureCount: textureCount,
			TextureOffset: textureOffset,
			TexturePathCount: texturePathCount,
			TexturePathOffset: texturePathOffset,
			SkinReferenceCount: skinReferenceCount,
			SkinFamilyCount: skinFamilyCount,
			SkinFamilyOffset: skinFamilyOffset,
			BodyPartCount: bodyPartCount,
			BodyPartOffset: bodyPartOffset,
			LocalAttachmentCount: localAttachmentCount,
			LocalAttachmentOffset: localAttachmentOffset,
			FlexDescCount: flexDescCount,
			FlexDescOffset: flexDescOffset,
			FlexControllerCount: flexControllerCount,
			FlexControllerOffset: flexControllerOffset,
			FlexRuleCount: flexRuleCount,
			FlexRuleOffset: flexRuleOffset,
			AnimBlockNameOffset: animBlockNameOffset,
			AnimBlockCount: animBlockCount,
			AnimBlockOffset: animBlockOffset);

		var bones = ReadBones(stream, reader, header);
		var texturePaths = ReadTexturePaths(stream, reader, header);
		var textures = ReadTextures(stream, reader, header);
		var skinFamilies = ReadSkinFamilies(stream, reader, header);
		var bodyParts = ReadBodyParts(stream, reader, header);
		var flexDescs = ReadFlexDescs(stream, reader, header);
		var flexControllers = ReadFlexControllers(stream, reader, header);
		var flexRules = ReadFlexRules(stream, reader, header);
		var animationDescs = ReadLocalAnimationDescs(stream, reader, header);
		var sequenceDescs = ReadLocalSequenceDescs(stream, reader, header);

		return new MdlModel(
			SourcePath: Path.GetFullPath(sourcePath),
			Header: header,
			Bones: bones,
			TexturePaths: texturePaths,
			Textures: textures,
			SkinFamilies: skinFamilies,
			BodyParts: bodyParts,
			FlexDescs: flexDescs,
			FlexControllers: flexControllers,
			FlexRules: flexRules,
			AnimationDescs: animationDescs,
			SequenceDescs: sequenceDescs);
	}

	private static bool IsSupportedVersion(int version)
	{
		return version is 52 or 53 or >= 44 and <= 49 or >= 54 and <= 56 or >= 58 and <= 59;
	}

	private static IReadOnlyList<MdlFlexDesc> ReadFlexDescs(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.FlexDescCount <= 0 || header.FlexDescOffset <= 0)
		{
			return Array.Empty<MdlFlexDesc>();
		}

		if (header.FlexDescOffset >= stream.Length)
		{
			return Array.Empty<MdlFlexDesc>();
		}

		const int flexDescSizeBytes = 4;
		var requiredBytes = (long)header.FlexDescCount * flexDescSizeBytes;
		if ((long)header.FlexDescOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlFlexDesc>();
		}

		var flexDescs = new List<MdlFlexDesc>(capacity: header.FlexDescCount);
		for (var index = 0; index < header.FlexDescCount; index++)
		{
			var flexDescStart = (long)header.FlexDescOffset + index * flexDescSizeBytes;
			stream.Seek(flexDescStart, SeekOrigin.Begin);

			var nameOffset = reader.ReadInt32();
			var name = string.Empty;
			if (nameOffset > 0)
			{
				name = ReadNullTerminatedStringAt(stream, flexDescStart + nameOffset, maxBytes: 512).Trim();
			}

			flexDescs.Add(new MdlFlexDesc(
				Index: index,
				Name: name));
		}

		return flexDescs;
	}

	private static IReadOnlyList<MdlFlexController> ReadFlexControllers(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.FlexControllerCount <= 0 || header.FlexControllerOffset <= 0)
		{
			return Array.Empty<MdlFlexController>();
		}

		if (header.FlexControllerOffset >= stream.Length)
		{
			return Array.Empty<MdlFlexController>();
		}

		const int flexControllerSizeBytes = 20;
		var requiredBytes = (long)header.FlexControllerCount * flexControllerSizeBytes;
		if ((long)header.FlexControllerOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlFlexController>();
		}

		var controllers = new List<MdlFlexController>(capacity: header.FlexControllerCount);
		for (var index = 0; index < header.FlexControllerCount; index++)
		{
			var controllerStart = (long)header.FlexControllerOffset + index * flexControllerSizeBytes;
			stream.Seek(controllerStart, SeekOrigin.Begin);

			var typeOffset = reader.ReadInt32();
			var nameOffset = reader.ReadInt32();
			var localToGlobal = reader.ReadInt32();
			var min = reader.ReadSingle();
			var max = reader.ReadSingle();

			var type = string.Empty;
			if (typeOffset > 0)
			{
				type = ReadNullTerminatedStringAt(stream, controllerStart + typeOffset, maxBytes: 256).Trim();
			}

			var name = string.Empty;
			if (nameOffset > 0)
			{
				name = ReadNullTerminatedStringAt(stream, controllerStart + nameOffset, maxBytes: 256).Trim();
			}

			controllers.Add(new MdlFlexController(
				Index: index,
				Type: type,
				Name: name,
				LocalToGlobal: localToGlobal,
				Min: min,
				Max: max));
		}

		return controllers;
	}

	private static IReadOnlyList<MdlFlexRule> ReadFlexRules(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.FlexRuleCount <= 0 || header.FlexRuleOffset <= 0)
		{
			return Array.Empty<MdlFlexRule>();
		}

		if (header.FlexRuleOffset >= stream.Length)
		{
			return Array.Empty<MdlFlexRule>();
		}

		const int flexRuleSizeBytes = 12;
		var requiredBytes = (long)header.FlexRuleCount * flexRuleSizeBytes;
		if ((long)header.FlexRuleOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlFlexRule>();
		}

		var rules = new List<MdlFlexRule>(capacity: header.FlexRuleCount);
		for (var index = 0; index < header.FlexRuleCount; index++)
		{
			var ruleStart = (long)header.FlexRuleOffset + index * flexRuleSizeBytes;
			stream.Seek(ruleStart, SeekOrigin.Begin);

			var flexIndex = reader.ReadInt32();
			var opCount = reader.ReadInt32();
			var opOffset = reader.ReadInt32();

			var ops = ReadFlexOps(stream, reader, ruleStart, opCount, opOffset);
			rules.Add(new MdlFlexRule(
				Index: index,
				FlexIndex: flexIndex,
				Ops: ops));
		}

		return rules;
	}

	private static IReadOnlyList<MdlFlexOp> ReadFlexOps(Stream stream, BinaryReader reader, long ruleStart, int opCount, int opOffset)
	{
		if (opCount <= 0 || opOffset <= 0)
		{
			return Array.Empty<MdlFlexOp>();
		}

		var opsStart = ruleStart + opOffset;
		if (opsStart < 0 || opsStart >= stream.Length)
		{
			return Array.Empty<MdlFlexOp>();
		}

		const int opSizeBytes = 8;
		var maxCount = Math.Min(opCount, (int)Math.Max(0, (stream.Length - opsStart) / opSizeBytes));
		if (maxCount <= 0)
		{
			return Array.Empty<MdlFlexOp>();
		}

		var ops = new List<MdlFlexOp>(capacity: maxCount);
		for (var i = 0; i < maxCount; i++)
		{
			var opStart = opsStart + (long)i * opSizeBytes;
			stream.Seek(opStart, SeekOrigin.Begin);

			var op = reader.ReadInt32();
			var valueOrIndex = reader.ReadUInt32();

			var index = unchecked((int)valueOrIndex);
			var value = BitConverter.Int32BitsToSingle(unchecked((int)valueOrIndex));

			ops.Add(new MdlFlexOp(
				Op: op,
				Index: index,
				Value: value));
		}

		return ops;
	}

	private static IReadOnlyList<MdlSkinFamily> ReadSkinFamilies(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.SkinFamilyCount <= 0 || header.SkinReferenceCount <= 0 || header.SkinFamilyOffset <= 0)
		{
			return Array.Empty<MdlSkinFamily>();
		}

		if (header.SkinFamilyOffset >= stream.Length)
		{
			return Array.Empty<MdlSkinFamily>();
		}

		var entries = (long)header.SkinFamilyCount * header.SkinReferenceCount;
		var requiredBytes = entries * sizeof(short);
		if ((long)header.SkinFamilyOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlSkinFamily>();
		}

		var skinFamilies = new List<MdlSkinFamily>(capacity: header.SkinFamilyCount);
		stream.Seek(header.SkinFamilyOffset, SeekOrigin.Begin);
		for (var skinFamilyIndex = 0; skinFamilyIndex < header.SkinFamilyCount; skinFamilyIndex++)
		{
			var textureIndexes = new int[header.SkinReferenceCount];
			for (var skinRefIndex = 0; skinRefIndex < header.SkinReferenceCount; skinRefIndex++)
			{
				textureIndexes[skinRefIndex] = reader.ReadInt16();
			}

			skinFamilies.Add(new MdlSkinFamily(
				Index: skinFamilyIndex,
				TextureIndexes: textureIndexes));
		}

		return skinFamilies;
	}

	private static IReadOnlyList<MdlBone> ReadBones(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.BoneCount <= 0 || header.BoneOffset <= 0)
		{
			return Array.Empty<MdlBone>();
		}

		if (header.BoneOffset >= stream.Length)
		{
			return Array.Empty<MdlBone>();
		}

		// Common mstudiobone_t sizes across Source-era MDL versions.
		var candidates = new[]
		{
			244,
			216,
			200,
			184,
			176,
			160,
			152,
			144
		};

		var boneSize = DetermineBoneStructSize(stream, reader, header.BoneOffset, header.BoneCount, candidates);
		if (boneSize is null)
		{
			return Array.Empty<MdlBone>();
		}

		var result = new List<MdlBone>(capacity: header.BoneCount);
		for (var index = 0; index < header.BoneCount; index++)
		{
			var boneStart = (long)header.BoneOffset + index * boneSize.Value;
			if (boneStart + 8 > stream.Length)
			{
				break;
			}

			stream.Seek(boneStart, SeekOrigin.Begin);
			var nameOffset = reader.ReadInt32();
			var parentIndex = reader.ReadInt32();

			var name = ReadNullTerminatedStringAt(stream, boneStart + nameOffset, maxBytes: 256);

			var position = Vector3.Zero;
			var rotation = Vector3.Zero;
			var positionScale = Vector3.One;
			var rotationScale = Vector3.One;
			var poseToBone = Matrix4x4.Identity;
			var physicsBoneIndex = -1;
			var flags = 0;
			if (boneSize.Value >= 72)
			{
				// pos @ +32, rot @ +60 (common Source SDK layout).
				stream.Seek(boneStart + 32, SeekOrigin.Begin);
				position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

				stream.Seek(boneStart + 60, SeekOrigin.Begin);
				rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			}

			if (boneSize.Value >= 96)
			{
				// posscale @ +72, rotscale @ +84 in common Source-era layouts.
				stream.Seek(boneStart + 72, SeekOrigin.Begin);
				positionScale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

				stream.Seek(boneStart + 84, SeekOrigin.Begin);
				rotationScale = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			}

			if (boneSize.Value >= 144)
			{
				// poseToBone @ +96 (matrix3x4_t, 12 floats, row-major).
				stream.Seek(boneStart + 96, SeekOrigin.Begin);

				var m00 = reader.ReadSingle();
				var m01 = reader.ReadSingle();
				var m02 = reader.ReadSingle();
				var m03 = reader.ReadSingle();

				var m10 = reader.ReadSingle();
				var m11 = reader.ReadSingle();
				var m12 = reader.ReadSingle();
				var m13 = reader.ReadSingle();

				var m20 = reader.ReadSingle();
				var m21 = reader.ReadSingle();
				var m22 = reader.ReadSingle();
				var m23 = reader.ReadSingle();

				poseToBone = new Matrix4x4(
					m00, m01, m02, m03,
					m10, m11, m12, m13,
					m20, m21, m22, m23,
					0, 0, 0, 1);
			}

			// flags sit after poseToBone (with or without qAlignment padding). Handle both common layouts.
			if (boneSize.Value >= 160)
			{
				stream.Seek(boneStart + 160, SeekOrigin.Begin);
				flags = reader.ReadInt32();
			}
			else if (boneSize.Value >= 152)
			{
				stream.Seek(boneStart + 144, SeekOrigin.Begin);
				flags = reader.ReadInt32();
			}

			if (boneSize.Value >= 176)
			{
				// physicsbone @ +172 (int32) in common Source-era layouts.
				stream.Seek(boneStart + 172, SeekOrigin.Begin);
				physicsBoneIndex = reader.ReadInt32();
			}

			result.Add(new MdlBone(
				Index: index,
				Name: name,
				ParentIndex: parentIndex,
				Position: position,
				RotationRadians: rotation,
				PositionScale: positionScale,
				RotationScale: rotationScale,
				PoseToBone: poseToBone,
				PhysicsBoneIndex: physicsBoneIndex,
				Flags: flags));
		}

		return result;
	}

	private static IReadOnlyList<string> ReadTexturePaths(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.TexturePathCount <= 0 || header.TexturePathOffset <= 0)
		{
			return Array.Empty<string>();
		}

		if (header.TexturePathOffset >= stream.Length)
		{
			return Array.Empty<string>();
		}

		var result = new List<string>(capacity: header.TexturePathCount);
		stream.Seek(header.TexturePathOffset, SeekOrigin.Begin);

		for (var index = 0; index < header.TexturePathCount; index++)
		{
			var texturePathOffset = reader.ReadInt32();
			if (texturePathOffset <= 0 || texturePathOffset >= stream.Length)
			{
				result.Add(string.Empty);
				continue;
			}

			var returnPosition = stream.Position;
			var path = ReadNullTerminatedStringAt(stream, texturePathOffset, maxBytes: 512)
				.Replace('\\', '/')
				.Trim();
			stream.Seek(returnPosition, SeekOrigin.Begin);

			result.Add(path);
		}

		return result;
	}

	private static IReadOnlyList<MdlTexture> ReadTextures(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.TextureCount <= 0 || header.TextureOffset <= 0)
		{
			return Array.Empty<MdlTexture>();
		}

		if (header.TextureOffset >= stream.Length)
		{
			return Array.Empty<MdlTexture>();
		}

		const int textureStructSizeBytes = 64;
		var requiredBytes = (long)header.TextureCount * textureStructSizeBytes;
		if ((long)header.TextureOffset + requiredBytes > stream.Length)
		{
			// Avoid seeking past end of file for obviously bad headers.
			return Array.Empty<MdlTexture>();
		}

		var textures = new List<MdlTexture>(capacity: header.TextureCount);
		stream.Seek(header.TextureOffset, SeekOrigin.Begin);
		for (var index = 0; index < header.TextureCount; index++)
		{
			var textureStart = (long)header.TextureOffset + index * textureStructSizeBytes;
			stream.Seek(textureStart, SeekOrigin.Begin);

			var nameOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // flags
			_ = reader.ReadInt32(); // used
			_ = reader.ReadInt32(); // unused1
			_ = reader.ReadInt32(); // materialP
			_ = reader.ReadInt32(); // clientMaterialP
			SkipBytes(reader, 10 * sizeof(int)); // unused[10]

			var pathFileName = string.Empty;
			if (nameOffset > 0)
			{
				pathFileName = ReadNullTerminatedStringAt(stream, textureStart + nameOffset, maxBytes: 512)
					.Replace('\\', '/')
					.Trim();
			}

			textures.Add(new MdlTexture(Index: index, PathFileName: pathFileName));
		}

		return textures;
	}

	private static IReadOnlyList<MdlBodyPart> ReadBodyParts(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.BodyPartCount <= 0 || header.BodyPartOffset <= 0)
		{
			return Array.Empty<MdlBodyPart>();
		}

		if (header.BodyPartOffset >= stream.Length)
		{
			return Array.Empty<MdlBodyPart>();
		}

		const int bodyPartStructSizeBytes = 16;
		var requiredBytes = (long)header.BodyPartCount * bodyPartStructSizeBytes;
		if ((long)header.BodyPartOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlBodyPart>();
		}

		var result = new List<MdlBodyPart>(capacity: header.BodyPartCount);
		stream.Seek(header.BodyPartOffset, SeekOrigin.Begin);

		for (var bodyPartIndex = 0; bodyPartIndex < header.BodyPartCount; bodyPartIndex++)
		{
			var bodyPartStart = (long)header.BodyPartOffset + bodyPartIndex * bodyPartStructSizeBytes;
			stream.Seek(bodyPartStart, SeekOrigin.Begin);

			var nameOffset = reader.ReadInt32();
			var modelCount = reader.ReadInt32();
			_ = reader.ReadInt32(); // base
			var modelOffset = reader.ReadInt32();

			var bodyPartName = string.Empty;
			if (nameOffset > 0)
			{
				bodyPartName = ReadNullTerminatedStringAt(stream, bodyPartStart + nameOffset, maxBytes: 256).Trim();
			}

			var models = ReadSubModels(stream, reader, bodyPartStart, modelCount, modelOffset);
			result.Add(new MdlBodyPart(
				Index: bodyPartIndex,
				Name: bodyPartName,
				Models: models));
		}

		return result;
	}

	private static IReadOnlyList<MdlSubModel> ReadSubModels(
		Stream stream,
		BinaryReader reader,
		long bodyPartStart,
		int modelCount,
		int modelOffset)
	{
		if (modelCount <= 0 || modelOffset <= 0)
		{
			return Array.Empty<MdlSubModel>();
		}

		var modelsStart = bodyPartStart + modelOffset;
		if (modelsStart < 0 || modelsStart >= stream.Length)
		{
			return Array.Empty<MdlSubModel>();
		}

		const int modelStructSizeBytes = 148;
		var requiredBytes = (long)modelCount * modelStructSizeBytes;
		if (modelsStart + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlSubModel>();
		}

		var result = new List<MdlSubModel>(capacity: modelCount);
		for (var modelIndex = 0; modelIndex < modelCount; modelIndex++)
		{
			var modelStart = modelsStart + (long)modelIndex * modelStructSizeBytes;
			stream.Seek(modelStart, SeekOrigin.Begin);

			var name = ReadFixedString(reader, 64);
			_ = reader.ReadInt32(); // type
			_ = reader.ReadSingle(); // boundingradius
			var meshCount = reader.ReadInt32();
			var meshOffset = reader.ReadInt32();
			var vertexCount = reader.ReadInt32();

			SkipBytes(reader, 6 * sizeof(int)); // vertexindex, tangentsindex, numattachments, attachmentindex, numeyeballs, eyeballindex
			SkipBytes(reader, 2 * sizeof(int)); // vertexdata pointers
			SkipBytes(reader, 8 * sizeof(int)); // unused[8]

			var meshes = ReadMeshes(stream, reader, modelStart, meshCount, meshOffset);
			result.Add(new MdlSubModel(
				Index: modelIndex,
				Name: name,
				VertexCount: vertexCount,
				Meshes: meshes));
		}

		return result;
	}

	private static IReadOnlyList<MdlMesh> ReadMeshes(
		Stream stream,
		BinaryReader reader,
		long modelStart,
		int meshCount,
		int meshOffset)
	{
		if (meshCount <= 0 || meshOffset <= 0)
		{
			return Array.Empty<MdlMesh>();
		}

		var meshesStart = modelStart + meshOffset;
		if (meshesStart < 0 || meshesStart >= stream.Length)
		{
			return Array.Empty<MdlMesh>();
		}

		const int meshStructSizeBytes = 116;
		var requiredBytes = (long)meshCount * meshStructSizeBytes;
		if (meshesStart + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlMesh>();
		}

		var meshes = new List<MdlMesh>(capacity: meshCount);
		for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
		{
			var meshStart = meshesStart + (long)meshIndex * meshStructSizeBytes;
			stream.Seek(meshStart, SeekOrigin.Begin);

			var materialIndex = reader.ReadInt32();
			_ = reader.ReadInt32(); // modelindex
			var vertexCount = reader.ReadInt32();
			var vertexIndexStart = reader.ReadInt32();
			var flexCount = reader.ReadInt32();
			var flexOffset = reader.ReadInt32();

			SkipBytes(reader, 3 * sizeof(int)); // materialtype, materialparam, meshid
			SkipBytes(reader, 3 * sizeof(float)); // center (Vector)
			SkipBytes(reader, sizeof(int)); // vertexdata.modelvertexdata (runtime pointer)
			var lodVertexCount = new int[8];
			for (var i = 0; i < lodVertexCount.Length; i++)
			{
				lodVertexCount[i] = reader.ReadInt32();
			}

			var flexes = ReadMeshFlexes(stream, reader, meshStart, flexCount, flexOffset);

			meshes.Add(new MdlMesh(
				Index: meshIndex,
				MaterialIndex: materialIndex,
				VertexCount: vertexCount,
				VertexIndexStart: vertexIndexStart,
				Flexes: flexes,
				LodVertexCount: lodVertexCount));
		}

		return meshes;
	}

	private static IReadOnlyList<MdlFlex> ReadMeshFlexes(Stream stream, BinaryReader reader, long meshStart, int flexCount, int flexOffset)
	{
		if (flexCount <= 0 || flexOffset <= 0)
		{
			return Array.Empty<MdlFlex>();
		}

		var flexesStart = meshStart + flexOffset;
		if (flexesStart < 0 || flexesStart >= stream.Length)
		{
			return Array.Empty<MdlFlex>();
		}

		const int flexSizeBytes = 60;
		var maxCount = Math.Min(flexCount, (int)Math.Max(0, (stream.Length - flexesStart) / flexSizeBytes));
		if (maxCount <= 0)
		{
			return Array.Empty<MdlFlex>();
		}

		var flexes = new List<MdlFlex>(capacity: maxCount);
		for (var index = 0; index < maxCount; index++)
		{
			var flexStart = flexesStart + (long)index * flexSizeBytes;
			stream.Seek(flexStart, SeekOrigin.Begin);

			var flexDescIndex = reader.ReadInt32();
			var target0 = reader.ReadSingle();
			var target1 = reader.ReadSingle();
			var target2 = reader.ReadSingle();
			var target3 = reader.ReadSingle();
			var vertCount = reader.ReadInt32();
			var vertOffset = reader.ReadInt32();
			var flexDescPartnerIndex = reader.ReadInt32();
			var vertAnimType = reader.ReadByte();
			SkipBytes(reader, 3); // unusedchar[3]
			SkipBytes(reader, 6 * sizeof(int)); // unused[6]

			var vertAnims = ReadVertAnims(stream, reader, flexStart, vertCount, vertOffset, vertAnimType);

			flexes.Add(new MdlFlex(
				Index: index,
				FlexDescIndex: flexDescIndex,
				Target0: target0,
				Target1: target1,
				Target2: target2,
				Target3: target3,
				VertCount: vertCount,
				FlexDescPartnerIndex: flexDescPartnerIndex,
				VertAnimType: vertAnimType,
				VertAnims: vertAnims));
		}

		return flexes;
	}

	private static IReadOnlyList<MdlVertAnim> ReadVertAnims(Stream stream, BinaryReader reader, long flexStart, int vertCount, int vertOffset, byte vertAnimType)
	{
		if (vertCount <= 0 || vertOffset <= 0)
		{
			return Array.Empty<MdlVertAnim>();
		}

		var vertsStart = flexStart + vertOffset;
		if (vertsStart < 0 || vertsStart >= stream.Length)
		{
			return Array.Empty<MdlVertAnim>();
		}

		var vertSizeBytes = vertAnimType == 1 ? 18 : 16;
		var maxCount = Math.Min(vertCount, (int)Math.Max(0, (stream.Length - vertsStart) / vertSizeBytes));
		if (maxCount <= 0)
		{
			return Array.Empty<MdlVertAnim>();
		}

		var verts = new List<MdlVertAnim>(capacity: maxCount);
		for (var i = 0; i < maxCount; i++)
		{
			var vertStart = vertsStart + (long)i * vertSizeBytes;
			stream.Seek(vertStart, SeekOrigin.Begin);

			var index = reader.ReadUInt16();
			var speed = reader.ReadByte();
			var side = reader.ReadByte();

			var delta0 = reader.ReadUInt16();
			var delta1 = reader.ReadUInt16();
			var delta2 = reader.ReadUInt16();

			var nDelta0 = reader.ReadUInt16();
			var nDelta1 = reader.ReadUInt16();
			var nDelta2 = reader.ReadUInt16();

			short? wrinkleDelta = null;
			if (vertAnimType == 1)
			{
				wrinkleDelta = reader.ReadInt16();
			}

			verts.Add(new MdlVertAnim(
				Index: index,
				Speed: speed,
				Side: side,
				Delta0: delta0,
				Delta1: delta1,
				Delta2: delta2,
				NDelta0: nDelta0,
				NDelta1: nDelta1,
				NDelta2: nDelta2,
				WrinkleDelta: wrinkleDelta));
		}

		return verts;
	}

	private static IReadOnlyList<MdlAnimationDesc> ReadLocalAnimationDescs(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.LocalAnimationCount <= 0 || header.LocalAnimationOffset <= 0)
		{
			return Array.Empty<MdlAnimationDesc>();
		}

		if (header.LocalAnimationOffset >= stream.Length)
		{
			return Array.Empty<MdlAnimationDesc>();
		}

		const int animDescSizeBytes = 100;
		var requiredBytes = (long)header.LocalAnimationCount * animDescSizeBytes;
		if ((long)header.LocalAnimationOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlAnimationDesc>();
		}

		var animDescs = new List<MdlAnimationDesc>(capacity: header.LocalAnimationCount);
		for (var index = 0; index < header.LocalAnimationCount; index++)
		{
			var animDescStart = (long)header.LocalAnimationOffset + index * animDescSizeBytes;
			stream.Seek(animDescStart, SeekOrigin.Begin);

			_ = reader.ReadInt32(); // baseptr
			var nameOffset = reader.ReadInt32();
			var fps = reader.ReadSingle();
			var flags = reader.ReadInt32();
			var frameCount = reader.ReadInt32();
			var movementCount = reader.ReadInt32();
			var movementOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // ikrulezeroframeindex
			_ = reader.ReadInt32(); // compressedikerrorindex
			SkipBytes(reader, 4 * sizeof(int)); // unused1[4]
			var animBlock = reader.ReadInt32();
			var animOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // numikrules
			_ = reader.ReadInt32(); // ikruleindex
			_ = reader.ReadInt32(); // animblockikruleindex
			_ = reader.ReadInt32(); // numlocalhierarchy
			_ = reader.ReadInt32(); // localhierarchyindex
			var sectionOffset = reader.ReadInt32();
			var sectionFrameCount = reader.ReadInt32();
			_ = reader.ReadInt16(); // zeroframespan
			_ = reader.ReadInt16(); // zeroframecount
			_ = reader.ReadInt32(); // zeroframeindex
			_ = reader.ReadSingle(); // zeroframestalltime

			var name = string.Empty;
			if (nameOffset > 0)
			{
				name = ReadNullTerminatedStringAt(stream, animDescStart + nameOffset, maxBytes: 512).Trim();

				// Match old Crowbar naming edge-case: "a_../..." → keep path but move "a_" prefix to filename.
				if (name.StartsWith("a_../", StringComparison.Ordinal) || name.StartsWith("a_..\\", StringComparison.Ordinal))
				{
					name = name[5..];
					name = Path.Combine(Path.GetDirectoryName(name) ?? string.Empty, "a_" + Path.GetFileName(name));
				}
			}

			var movements = ReadMovements(stream, reader, animDescStart, movementCount, movementOffset);

			animDescs.Add(new MdlAnimationDesc(
				Index: index,
				OffsetStart: animDescStart,
				Name: name,
				Fps: fps,
				Flags: flags,
				FrameCount: frameCount,
				Movements: movements,
				AnimBlock: animBlock,
				AnimOffset: animOffset,
				SectionOffset: sectionOffset,
				SectionFrameCount: sectionFrameCount));
		}

		return animDescs;
	}

	private static IReadOnlyList<MdlMovement> ReadMovements(Stream stream, BinaryReader reader, long animDescStart, int movementCount, int movementOffset)
	{
		if (movementCount <= 0 || movementOffset <= 0)
		{
			return Array.Empty<MdlMovement>();
		}

		if (movementCount > 16_384)
		{
			return Array.Empty<MdlMovement>();
		}

		const int movementSizeBytes = 44;
		var movementsStart = animDescStart + movementOffset;
		var bytesNeeded = (long)movementCount * movementSizeBytes;
		if (movementsStart < 0 || movementsStart + bytesNeeded > stream.Length)
		{
			return Array.Empty<MdlMovement>();
		}

		stream.Seek(movementsStart, SeekOrigin.Begin);
		var movements = new List<MdlMovement>(capacity: movementCount);
		for (var i = 0; i < movementCount; i++)
		{
			var endFrameIndex = reader.ReadInt32();
			var motionFlags = reader.ReadInt32();
			var v0 = reader.ReadSingle();
			var v1 = reader.ReadSingle();
			var angle = reader.ReadSingle();
			var vector = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			movements.Add(new MdlMovement(endFrameIndex, motionFlags, v0, v1, angle, vector, position));
		}

		return movements;
	}

	private static IReadOnlyList<MdlSequenceDesc> ReadLocalSequenceDescs(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.LocalSequenceCount <= 0 || header.LocalSequenceOffset <= 0)
		{
			return Array.Empty<MdlSequenceDesc>();
		}

		if (header.LocalSequenceOffset >= stream.Length)
		{
			return Array.Empty<MdlSequenceDesc>();
		}

		const int sequenceDescSizeBytes = 212;
		var requiredBytes = (long)header.LocalSequenceCount * sequenceDescSizeBytes;
		if ((long)header.LocalSequenceOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<MdlSequenceDesc>();
		}

		var sequences = new List<MdlSequenceDesc>(capacity: header.LocalSequenceCount);
		for (var index = 0; index < header.LocalSequenceCount; index++)
		{
			var seqStart = (long)header.LocalSequenceOffset + index * sequenceDescSizeBytes;
			stream.Seek(seqStart, SeekOrigin.Begin);

			_ = reader.ReadInt32(); // baseptr
			var nameOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // activityNameOffset
			var flags = reader.ReadInt32();
			_ = reader.ReadInt32(); // activity
			_ = reader.ReadInt32(); // actweight
			_ = reader.ReadInt32(); // numevents
			_ = reader.ReadInt32(); // eventindex
			SkipBytes(reader, 6 * sizeof(float)); // bbmin + bbmax

			var blendCount = reader.ReadInt32();
			var animIndexOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // movementindex
			var groupSize0 = reader.ReadInt32();
			var groupSize1 = reader.ReadInt32();

			var name = string.Empty;
			if (nameOffset > 0)
			{
				name = ReadNullTerminatedStringAt(stream, seqStart + nameOffset, maxBytes: 512).Trim();
			}

			var animIndexes = Array.Empty<short>();
			var animIndexCount = groupSize0 * groupSize1;
			if (animIndexCount > 0 && animIndexOffset > 0)
			{
				var animIndexesStart = seqStart + animIndexOffset;
				var bytesNeeded = (long)animIndexCount * sizeof(short);
				if (animIndexesStart > 0 && animIndexesStart + bytesNeeded <= stream.Length)
				{
					stream.Seek(animIndexesStart, SeekOrigin.Begin);
					var temp = new short[animIndexCount];
					for (var i = 0; i < animIndexCount; i++)
					{
						temp[i] = reader.ReadInt16();
					}
					animIndexes = temp;
				}
			}

			sequences.Add(new MdlSequenceDesc(
				Index: index,
				OffsetStart: seqStart,
				Name: name,
				Flags: flags,
				BlendCount: blendCount,
				GroupSize0: groupSize0,
				GroupSize1: groupSize1,
				AnimIndexOffset: animIndexOffset,
				AnimDescIndexes: animIndexes));
		}

		return sequences;
	}

	private static int? DetermineBoneStructSize(
		Stream stream,
		BinaryReader reader,
		int boneOffset,
		int boneCount,
		IReadOnlyList<int> candidates)
	{
		foreach (var size in candidates)
		{
			var totalBytes = (long)boneCount * size;
			if ((long)boneOffset + totalBytes > stream.Length)
			{
				continue;
			}

			var ok = true;
			var maxToCheck = Math.Min(boneCount, 32);
			for (var index = 0; index < maxToCheck; index++)
			{
				var boneStart = (long)boneOffset + index * size;
				stream.Seek(boneStart, SeekOrigin.Begin);

				var nameOffset = reader.ReadInt32();
				var parentIndex = reader.ReadInt32();

				if (parentIndex < -1 || parentIndex >= boneCount)
				{
					ok = false;
					break;
				}

				var nameStart = boneStart + nameOffset;
				if (nameOffset <= 0 || nameStart < 0 || nameStart >= stream.Length)
				{
					ok = false;
					break;
				}

				var name = ReadNullTerminatedStringAt(stream, nameStart, maxBytes: 256);
				if (!IsPlausibleBoneName(name))
				{
					ok = false;
					break;
				}
			}

			if (ok)
			{
				return size;
			}
		}

		return null;
	}

	private static bool IsPlausibleBoneName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		if (name.Length > 200)
		{
			return false;
		}

		foreach (var c in name)
		{
			if (c == '\0')
			{
				return false;
			}

			// Reject other control chars.
			if (char.IsControl(c))
			{
				return false;
			}
		}

		return true;
	}

	private static string ReadFixedString(BinaryReader reader, int byteCount)
	{
		var bytes = reader.ReadBytes(byteCount);
		var zeroIndex = Array.IndexOf(bytes, (byte)0);
		if (zeroIndex >= 0)
		{
			bytes = bytes[..zeroIndex];
		}

		return Encoding.ASCII.GetString(bytes).Trim();
	}

	private static void SkipBytes(BinaryReader reader, int byteCount)
	{
		if (byteCount <= 0)
		{
			return;
		}

		reader.BaseStream.Seek(byteCount, SeekOrigin.Current);
	}

	private static string ReadNullTerminatedStringAt(Stream stream, long offset, int maxBytes)
	{
		if (offset < 0 || offset >= stream.Length)
		{
			return string.Empty;
		}

		stream.Seek(offset, SeekOrigin.Begin);
		var bytes = new List<byte>(capacity: 64);
		for (var i = 0; i < maxBytes; i++)
		{
			var value = stream.ReadByte();
			if (value < 0 || value == 0)
			{
				break;
			}

			bytes.Add((byte)value);
		}

		return bytes.Count == 0 ? string.Empty : Encoding.ASCII.GetString(bytes.ToArray());
	}
}
