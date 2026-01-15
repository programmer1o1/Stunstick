using System.Numerics;
using System.Text;

namespace Stunstick.Core.GoldSrc;

public static class GoldSrcMdlReader
{
	public const uint Idst = 0x54534449; // "IDST"

	public static GoldSrcMdlFile Read(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(stream, sourcePath: Path.GetFullPath(path));
	}

	public static GoldSrcMdlFile Read(Stream stream, string sourcePath)
	{
		if (stream is null)
		{
			throw new ArgumentNullException(nameof(stream));
		}

		if (!stream.CanRead)
		{
			throw new ArgumentException("Stream must be readable.", nameof(stream));
		}

		if (stream.Length < 8)
		{
			throw new InvalidDataException("File is too small to be a valid GoldSrc MDL.");
		}

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
		stream.Seek(0, SeekOrigin.Begin);

		var id = reader.ReadUInt32();
		if (id != Idst)
		{
			throw new NotSupportedException("Unsupported MDL (expected IDST signature).");
		}

		var version = reader.ReadInt32();
		if (version != 10)
		{
			throw new NotSupportedException($"Unsupported GoldSrc MDL version: {version} (expected 10).");
		}

		var name = ReadFixedString(reader, 64);
		var fileSize = reader.ReadInt32();

		// Skip vectors: eyePosition, hullMin, hullMax, viewBbMin, viewBbMax
		SkipBytes(reader, 5 * 3 * sizeof(float));

		var flags = reader.ReadInt32();

		var boneCount = reader.ReadInt32();
		var boneOffset = reader.ReadInt32();

		_ = reader.ReadInt32(); // bonecontrollercount
		_ = reader.ReadInt32(); // bonecontrolleroffset
		_ = reader.ReadInt32(); // hitboxcount
		_ = reader.ReadInt32(); // hitboxoffset
		_ = reader.ReadInt32(); // sequencecount
		_ = reader.ReadInt32(); // sequenceoffset
		_ = reader.ReadInt32(); // sequencegroupcount
		_ = reader.ReadInt32(); // sequencegroupoffset

		var textureCount = reader.ReadInt32();
		var textureOffset = reader.ReadInt32();
		_ = reader.ReadInt32(); // texturedatoffset

		_ = reader.ReadInt32(); // skinreferencecount
		_ = reader.ReadInt32(); // skinfamilycount
		_ = reader.ReadInt32(); // skinoffset

		var bodyPartCount = reader.ReadInt32();
		var bodyPartOffset = reader.ReadInt32();

		var header = new GoldSrcMdlHeader(
			Id: id,
			Version: version,
			Name: name,
			FileSize: fileSize,
			Flags: flags,
			BoneCount: boneCount,
			BoneOffset: boneOffset,
			TextureCount: textureCount,
			TextureOffset: textureOffset,
			BodyPartCount: bodyPartCount,
			BodyPartOffset: bodyPartOffset);

		var bones = ReadBones(stream, reader, header);
		var textures = ReadTextures(stream, reader, header);
		var bodyParts = ReadBodyParts(stream, reader, header);

		return new GoldSrcMdlFile(
			SourcePath: sourcePath,
			Header: header,
			Bones: bones,
			Textures: textures,
			BodyParts: bodyParts);
	}

	private static IReadOnlyList<GoldSrcMdlBone> ReadBones(Stream stream, BinaryReader reader, GoldSrcMdlHeader header)
	{
		if (header.BoneCount <= 0 || header.BoneOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlBone>();
		}

		if (header.BoneOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlBone>();
		}

		const int boneSizeBytes = 112;
		var requiredBytes = (long)header.BoneCount * boneSizeBytes;
		if ((long)header.BoneOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<GoldSrcMdlBone>();
		}

		var bones = new List<GoldSrcMdlBone>(capacity: header.BoneCount);
		for (var boneIndex = 0; boneIndex < header.BoneCount; boneIndex++)
		{
			var boneStart = (long)header.BoneOffset + boneIndex * boneSizeBytes;
			stream.Seek(boneStart, SeekOrigin.Begin);

			var name = ReadFixedString(reader, 32);
			var parentIndex = reader.ReadInt32();
			_ = reader.ReadInt32(); // flags
			SkipBytes(reader, 6 * sizeof(int)); // boneControllerIndex[6]

			var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			SkipBytes(reader, 6 * sizeof(float)); // positionScale + rotationScale

			bones.Add(new GoldSrcMdlBone(
				Index: boneIndex,
				Name: name,
				ParentIndex: parentIndex,
				Position: position,
				RotationRadians: rotation));
		}

		return bones;
	}

	private static IReadOnlyList<GoldSrcMdlTexture> ReadTextures(Stream stream, BinaryReader reader, GoldSrcMdlHeader header)
	{
		if (header.TextureCount <= 0 || header.TextureOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlTexture>();
		}

		if (header.TextureOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlTexture>();
		}

		const int textureSizeBytes = 80;
		var requiredBytes = (long)header.TextureCount * textureSizeBytes;
		if ((long)header.TextureOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<GoldSrcMdlTexture>();
		}

		var textures = new List<GoldSrcMdlTexture>(capacity: header.TextureCount);
		for (var textureIndex = 0; textureIndex < header.TextureCount; textureIndex++)
		{
			var textureStart = (long)header.TextureOffset + textureIndex * textureSizeBytes;
			stream.Seek(textureStart, SeekOrigin.Begin);

			var fileNameBytes = reader.ReadBytes(64);
			var fileName = Encoding.Default.GetString(fileNameBytes).TrimEnd('\0');
			var flags = (uint)reader.ReadInt32();
			var width = reader.ReadUInt32();
			var height = reader.ReadUInt32();
			var dataOffset = reader.ReadUInt32();

			textures.Add(new GoldSrcMdlTexture(
				Index: textureIndex,
				FileName: fileName,
				Width: width,
				Height: height,
				Flags: flags,
				DataOffset: dataOffset));
		}

		return textures;
	}

	private static IReadOnlyList<GoldSrcMdlBodyPart> ReadBodyParts(Stream stream, BinaryReader reader, GoldSrcMdlHeader header)
	{
		if (header.BodyPartCount <= 0 || header.BodyPartOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlBodyPart>();
		}

		if (header.BodyPartOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlBodyPart>();
		}

		const int bodyPartSizeBytes = 76;
		var requiredBytes = (long)header.BodyPartCount * bodyPartSizeBytes;
		if ((long)header.BodyPartOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<GoldSrcMdlBodyPart>();
		}

		var bodyParts = new List<GoldSrcMdlBodyPart>(capacity: header.BodyPartCount);
		for (var bodyPartIndex = 0; bodyPartIndex < header.BodyPartCount; bodyPartIndex++)
		{
			var bodyPartStart = (long)header.BodyPartOffset + bodyPartIndex * bodyPartSizeBytes;
			stream.Seek(bodyPartStart, SeekOrigin.Begin);

			var name = ReadFixedString(reader, 64);
			var modelCount = reader.ReadInt32();
			_ = reader.ReadInt32(); // base
			var modelOffset = reader.ReadInt32();

			var models = ReadModels(stream, reader, modelCount, modelOffset);
			bodyParts.Add(new GoldSrcMdlBodyPart(
				Index: bodyPartIndex,
				Name: name,
				Models: models));
		}

		return bodyParts;
	}

	private static IReadOnlyList<GoldSrcMdlModel> ReadModels(Stream stream, BinaryReader reader, int modelCount, int modelOffset)
	{
		if (modelCount <= 0 || modelOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlModel>();
		}

		if (modelOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlModel>();
		}

		const int modelSizeBytes = 112;
		var requiredBytes = (long)modelCount * modelSizeBytes;
		if ((long)modelOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<GoldSrcMdlModel>();
		}

		var models = new List<GoldSrcMdlModel>(capacity: modelCount);
		for (var modelIndex = 0; modelIndex < modelCount; modelIndex++)
		{
			var modelStart = (long)modelOffset + modelIndex * modelSizeBytes;
			stream.Seek(modelStart, SeekOrigin.Begin);

			var name = ReadFixedString(reader, 64);
			_ = reader.ReadInt32(); // type
			_ = reader.ReadSingle(); // boundingRadius
			var meshCount = reader.ReadInt32();
			var meshOffset = reader.ReadInt32();

			var vertexCount = reader.ReadInt32();
			var vertexBoneInfoOffset = reader.ReadInt32();
			var vertexOffset = reader.ReadInt32();
			var normalCount = reader.ReadInt32();
			var normalBoneInfoOffset = reader.ReadInt32();
			var normalOffset = reader.ReadInt32();

			_ = reader.ReadInt32(); // groupCount
			_ = reader.ReadInt32(); // groupOffset

			var vertexBoneInfos = ReadByteArray(stream, reader, vertexBoneInfoOffset, vertexCount);
			var normalBoneInfos = ReadByteArray(stream, reader, normalBoneInfoOffset, normalCount);
			var vertexes = ReadVector3Array(stream, reader, vertexOffset, vertexCount);
			var normals = ReadVector3Array(stream, reader, normalOffset, normalCount);
			var meshes = ReadMeshes(stream, reader, meshCount, meshOffset);

			models.Add(new GoldSrcMdlModel(
				Index: modelIndex,
				Name: name,
				VertexBoneInfos: vertexBoneInfos,
				NormalBoneInfos: normalBoneInfos,
				Vertexes: vertexes,
				Normals: normals,
				Meshes: meshes));
		}

		return models;
	}

	private static IReadOnlyList<GoldSrcMdlMesh> ReadMeshes(Stream stream, BinaryReader reader, int meshCount, int meshOffset)
	{
		if (meshCount <= 0 || meshOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlMesh>();
		}

		if (meshOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlMesh>();
		}

		const int meshSizeBytes = 20;
		var requiredBytes = (long)meshCount * meshSizeBytes;
		if ((long)meshOffset + requiredBytes > stream.Length)
		{
			return Array.Empty<GoldSrcMdlMesh>();
		}

		var meshes = new List<GoldSrcMdlMesh>(capacity: meshCount);
		for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
		{
			var meshStart = (long)meshOffset + meshIndex * meshSizeBytes;
			stream.Seek(meshStart, SeekOrigin.Begin);

			var faceCount = reader.ReadInt32();
			var faceOffset = reader.ReadInt32();
			var skinRef = reader.ReadInt32();
			_ = reader.ReadInt32(); // normalCount
			_ = reader.ReadInt32(); // normalOffset

			var stripsAndFans = ReadFaces(stream, reader, faceCount, faceOffset);
			meshes.Add(new GoldSrcMdlMesh(
				Index: meshIndex,
				SkinRef: skinRef,
				StripsAndFans: stripsAndFans));
		}

		return meshes;
	}

	private static IReadOnlyList<GoldSrcMdlStripOrFan> ReadFaces(Stream stream, BinaryReader reader, int faceCount, int faceOffset)
	{
		if (faceCount <= 0 || faceOffset <= 0)
		{
			return Array.Empty<GoldSrcMdlStripOrFan>();
		}

		if (faceOffset >= stream.Length)
		{
			return Array.Empty<GoldSrcMdlStripOrFan>();
		}

		var stripsAndFans = new List<GoldSrcMdlStripOrFan>();
		stream.Seek(faceOffset, SeekOrigin.Begin);

		while (stream.Position + sizeof(short) <= stream.Length)
		{
			var listCount = reader.ReadInt16();
			if (listCount == 0)
			{
				break;
			}

			var isStrip = listCount > 0;
			var count = Math.Abs(listCount);
			if (count <= 0)
			{
				continue;
			}

			var vertexes = new List<GoldSrcMdlVertexInfo>(capacity: count);
			for (var i = 0; i < count; i++)
			{
				if (stream.Position + 8 > stream.Length)
				{
					break;
				}

				var vertexIndex = reader.ReadUInt16();
				var normalIndex = reader.ReadUInt16();
				var s = reader.ReadInt16();
				var t = reader.ReadInt16();
				vertexes.Add(new GoldSrcMdlVertexInfo(vertexIndex, normalIndex, s, t));
			}

			stripsAndFans.Add(new GoldSrcMdlStripOrFan(
				IsTriangleStrip: isStrip,
				Vertexes: vertexes));
		}

		return stripsAndFans;
	}

	private static IReadOnlyList<byte> ReadByteArray(Stream stream, BinaryReader reader, int offset, int count)
	{
		if (count <= 0 || offset <= 0)
		{
			return Array.Empty<byte>();
		}

		if ((long)offset + count > stream.Length)
		{
			return Array.Empty<byte>();
		}

		stream.Seek(offset, SeekOrigin.Begin);
		return reader.ReadBytes(count);
	}

	private static IReadOnlyList<Vector3> ReadVector3Array(Stream stream, BinaryReader reader, int offset, int count)
	{
		if (count <= 0 || offset <= 0)
		{
			return Array.Empty<Vector3>();
		}

		var requiredBytes = (long)count * 3 * sizeof(float);
		if ((long)offset + requiredBytes > stream.Length)
		{
			return Array.Empty<Vector3>();
		}

		stream.Seek(offset, SeekOrigin.Begin);
		var result = new List<Vector3>(capacity: count);
		for (var i = 0; i < count; i++)
		{
			result.Add(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
		}

		return result;
	}

	private static string ReadFixedString(BinaryReader reader, int byteLength)
	{
		var bytes = reader.ReadBytes(byteLength);
		return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
	}

	private static void SkipBytes(BinaryReader reader, int byteCount)
	{
		if (byteCount <= 0)
		{
			return;
		}

		_ = reader.ReadBytes(byteCount);
	}
}
