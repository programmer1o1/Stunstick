using System.Text;

namespace Stunstick.Core.Vtx;

public static class VtxReader
{
	private const int VtxVertexSizeBytes = 9;
	private const int MaxMaterialReplacementCount = 1_000_000;

	public static VtxFile Read(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(stream, Path.GetFullPath(path));
	}

	public static VtxFile Read(Stream stream, string sourcePath)
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
		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

		if (stream.Length < 0x24)
		{
			throw new InvalidDataException("File is too small to be a valid VTX.");
		}

		var version = reader.ReadInt32();
		_ = reader.ReadInt32(); // vertexCacheSize
		_ = reader.ReadUInt16(); // maxBonesPerStrip
		_ = reader.ReadUInt16(); // maxBonesPerTri
		_ = reader.ReadInt32(); // maxBonesPerVertex
		var checksum = reader.ReadInt32();
		var lodCount = reader.ReadInt32();
		var materialReplacementListOffset = reader.ReadInt32();
		var bodyPartCount = reader.ReadInt32();
		var bodyPartOffset = reader.ReadInt32();

		var header = new VtxHeader(
			Version: version,
			Checksum: checksum,
			LodCount: lodCount,
			MaterialReplacementListOffset: materialReplacementListOffset,
			BodyPartCount: bodyPartCount,
			BodyPartOffset: bodyPartOffset);

		// Some games store extra topology fields in strip group and strip headers.
		// Try the common layout first; fall back to the "extra fields" variant if offsets don't validate.
		if (TryReadBodyParts(stream, reader, header, usesExtraStripGroupFields: false, out var bodyParts))
		{
			var materialReplacementLists = ReadMaterialReplacementLists(stream, reader, header);
			return new VtxFile(
				SourcePath: sourcePath,
				Header: header,
				UsesExtraStripGroupFields: false,
				BodyParts: bodyParts,
				MaterialReplacementLists: materialReplacementLists);
		}

		if (TryReadBodyParts(stream, reader, header, usesExtraStripGroupFields: true, out bodyParts))
		{
			var materialReplacementLists = ReadMaterialReplacementLists(stream, reader, header);
			return new VtxFile(
				SourcePath: sourcePath,
				Header: header,
				UsesExtraStripGroupFields: true,
				BodyParts: bodyParts,
				MaterialReplacementLists: materialReplacementLists);
		}

		throw new InvalidDataException("Unable to parse VTX body parts (unsupported layout or corrupt file).");
	}

	private static IReadOnlyList<VtxMaterialReplacementList> ReadMaterialReplacementLists(Stream stream, BinaryReader reader, VtxHeader header)
	{
		try
		{
			if (header.LodCount <= 0 || header.MaterialReplacementListOffset <= 0)
			{
				return Array.Empty<VtxMaterialReplacementList>();
			}

			if (header.MaterialReplacementListOffset >= stream.Length)
			{
				return Array.Empty<VtxMaterialReplacementList>();
			}

			// Each LOD entry is two ints: replacementCount + replacementOffset.
			const int listHeaderSizeBytes = 8;
			var requiredBytes = (long)header.LodCount * listHeaderSizeBytes;
			if ((long)header.MaterialReplacementListOffset + requiredBytes > stream.Length)
			{
				return Array.Empty<VtxMaterialReplacementList>();
			}

			var result = new List<VtxMaterialReplacementList>(capacity: header.LodCount);
			stream.Seek(header.MaterialReplacementListOffset, SeekOrigin.Begin);
			for (var lodIndex = 0; lodIndex < header.LodCount; lodIndex++)
			{
				var listStart = stream.Position;
				var replacementCount = reader.ReadInt32();
				var replacementOffset = reader.ReadInt32();

				var replacements = ReadMaterialReplacements(stream, reader, listStart, replacementCount, replacementOffset);
				result.Add(new VtxMaterialReplacementList(
					LodIndex: lodIndex,
					Replacements: replacements));
			}

			return result;
		}
		catch
		{
			return Array.Empty<VtxMaterialReplacementList>();
		}
	}

	private static IReadOnlyList<VtxMaterialReplacement> ReadMaterialReplacements(
		Stream stream,
		BinaryReader reader,
		long listStart,
		int replacementCount,
		int replacementOffset)
	{
		if (replacementCount <= 0 || replacementOffset <= 0)
		{
			return Array.Empty<VtxMaterialReplacement>();
		}

		if (replacementCount > MaxMaterialReplacementCount)
		{
			return Array.Empty<VtxMaterialReplacement>();
		}

		var replacementsStart = listStart + replacementOffset;
		if (replacementsStart < 0 || replacementsStart >= stream.Length)
		{
			return Array.Empty<VtxMaterialReplacement>();
		}

		var result = new List<VtxMaterialReplacement>(capacity: replacementCount);
		var returnPosition = stream.Position;
		try
		{
			stream.Seek(replacementsStart, SeekOrigin.Begin);
			for (var i = 0; i < replacementCount; i++)
			{
				var entryStart = stream.Position;
				if (entryStart + 6 > stream.Length)
				{
					break;
				}

				var materialIndex = reader.ReadInt16();
				var nameOffset = reader.ReadInt32();

				var replacement = string.Empty;
				if (nameOffset > 0)
				{
					replacement = ReadNullTerminatedStringAt(stream, entryStart + nameOffset, maxBytes: 512)
						.Replace('\\', '/')
						.Trim();
				}

				result.Add(new VtxMaterialReplacement(
					MaterialIndex: materialIndex,
					ReplacementMaterial: replacement));
			}
		}
		finally
		{
			stream.Seek(returnPosition, SeekOrigin.Begin);
		}

		return result;
	}

	private static string ReadNullTerminatedStringAt(Stream stream, long offset, int maxBytes)
	{
		if (offset < 0 || offset >= stream.Length)
		{
			return string.Empty;
		}

		var returnPosition = stream.Position;
		try
		{
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
		finally
		{
			stream.Seek(returnPosition, SeekOrigin.Begin);
		}
	}

	private static bool TryReadBodyParts(
		Stream stream,
		BinaryReader reader,
		VtxHeader header,
		bool usesExtraStripGroupFields,
		out IReadOnlyList<VtxBodyPart> bodyParts)
	{
		try
		{
			bodyParts = ReadBodyParts(stream, reader, header, usesExtraStripGroupFields);
			return true;
		}
		catch
		{
			bodyParts = Array.Empty<VtxBodyPart>();
			return false;
		}
	}

	private static IReadOnlyList<VtxBodyPart> ReadBodyParts(Stream stream, BinaryReader reader, VtxHeader header, bool usesExtraStripGroupFields)
	{
		if (header.BodyPartCount <= 0 || header.BodyPartOffset <= 0)
		{
			return Array.Empty<VtxBodyPart>();
		}

		if (header.BodyPartOffset >= stream.Length)
		{
			throw new InvalidDataException("VTX bodyPartOffset is invalid.");
		}

		// Body part header: int modelCount, int modelOffset
		const int bodyPartStructSizeBytes = 8;
		var requiredBytes = (long)header.BodyPartCount * bodyPartStructSizeBytes;
		if ((long)header.BodyPartOffset + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX body parts exceed file length.");
		}

		var result = new List<VtxBodyPart>(capacity: header.BodyPartCount);
		for (var bodyPartIndex = 0; bodyPartIndex < header.BodyPartCount; bodyPartIndex++)
		{
			var bodyPartStart = (long)header.BodyPartOffset + bodyPartIndex * bodyPartStructSizeBytes;
			stream.Seek(bodyPartStart, SeekOrigin.Begin);

			var modelCount = reader.ReadInt32();
			var modelOffset = reader.ReadInt32();
			if (modelCount < 0)
			{
				throw new InvalidDataException("VTX body part modelCount is invalid.");
			}
			if (modelOffset < 0)
			{
				throw new InvalidDataException("VTX body part modelOffset is invalid.");
			}

			var models = ReadModels(stream, reader, bodyPartStart, modelCount, modelOffset, usesExtraStripGroupFields);
			result.Add(new VtxBodyPart(ModelCount: modelCount, Models: models));
		}

		return result;
	}

	private static IReadOnlyList<VtxModel> ReadModels(
		Stream stream,
		BinaryReader reader,
		long bodyPartStart,
		int modelCount,
		int modelOffset,
		bool usesExtraStripGroupFields)
	{
		if (modelCount <= 0 || modelOffset <= 0)
		{
			return Array.Empty<VtxModel>();
		}

		var modelsStart = bodyPartStart + modelOffset;
		if (modelsStart < 0 || modelsStart >= stream.Length)
		{
			throw new InvalidDataException("VTX modelOffset is invalid.");
		}

		// Model header: int lodCount, int lodOffset
		const int modelStructSizeBytes = 8;
		var requiredBytes = (long)modelCount * modelStructSizeBytes;
		if (modelsStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX models exceed file length.");
		}

		var models = new List<VtxModel>(capacity: modelCount);
		for (var modelIndex = 0; modelIndex < modelCount; modelIndex++)
		{
			var modelStart = modelsStart + modelIndex * modelStructSizeBytes;
			stream.Seek(modelStart, SeekOrigin.Begin);

			var lodCount = reader.ReadInt32();
			var lodOffset = reader.ReadInt32();
			if (lodCount < 0)
			{
				throw new InvalidDataException("VTX model lodCount is invalid.");
			}
			if (lodOffset < 0)
			{
				throw new InvalidDataException("VTX model lodOffset is invalid.");
			}

			var lods = ReadModelLods(stream, reader, modelStart, lodCount, lodOffset, usesExtraStripGroupFields);
			models.Add(new VtxModel(LodCount: lodCount, Lods: lods));
		}

		return models;
	}

	private static IReadOnlyList<VtxModelLod> ReadModelLods(
		Stream stream,
		BinaryReader reader,
		long modelStart,
		int lodCount,
		int lodOffset,
		bool usesExtraStripGroupFields)
	{
		if (lodCount <= 0 || lodOffset <= 0)
		{
			return Array.Empty<VtxModelLod>();
		}

		var lodsStart = modelStart + lodOffset;
		if (lodsStart < 0 || lodsStart >= stream.Length)
		{
			throw new InvalidDataException("VTX lodOffset is invalid.");
		}

		// ModelLod header: int meshCount, int meshOffset, float switchPoint
		const int lodStructSizeBytes = 12;
		var requiredBytes = (long)lodCount * lodStructSizeBytes;
		if (lodsStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX model LODs exceed file length.");
		}

		var lods = new List<VtxModelLod>(capacity: lodCount);
		for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
		{
			var lodStart = lodsStart + (long)lodIndex * lodStructSizeBytes;
			stream.Seek(lodStart, SeekOrigin.Begin);

			var meshCount = reader.ReadInt32();
			var meshOffset = reader.ReadInt32();
			var switchPoint = reader.ReadSingle();
			if (meshCount < 0)
			{
				throw new InvalidDataException("VTX model LOD meshCount is invalid.");
			}
			if (meshOffset < 0)
			{
				throw new InvalidDataException("VTX model LOD meshOffset is invalid.");
			}

			var meshes = ReadMeshes(stream, reader, lodStart, meshCount, meshOffset, usesExtraStripGroupFields);
			var usesFacial = meshes.Any(m => m.StripGroups.Any(g => (g.Flags & 0x01) != 0 || (g.Flags & 0x04) != 0));

			lods.Add(new VtxModelLod(
				MeshCount: meshCount,
				SwitchPoint: switchPoint,
				Meshes: meshes,
				UsesFacial: usesFacial));
		}

		return lods;
	}

	private static IReadOnlyList<VtxMesh> ReadMeshes(
		Stream stream,
		BinaryReader reader,
		long lodStart,
		int meshCount,
		int meshOffset,
		bool usesExtraStripGroupFields)
	{
		if (meshCount <= 0 || meshOffset <= 0)
		{
			return Array.Empty<VtxMesh>();
		}

		var meshesStart = lodStart + meshOffset;
		if (meshesStart < 0 || meshesStart >= stream.Length)
		{
			throw new InvalidDataException("VTX meshOffset is invalid.");
		}

		// Mesh header: int stripGroupCount, int stripGroupOffset, byte flags
		const int meshStructSizeBytes = 9;
		var requiredBytes = (long)meshCount * meshStructSizeBytes;
		if (meshesStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX meshes exceed file length.");
		}

		var meshes = new List<VtxMesh>(capacity: meshCount);
		for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
		{
			var meshStart = meshesStart + (long)meshIndex * meshStructSizeBytes;
			stream.Seek(meshStart, SeekOrigin.Begin);

			var stripGroupCount = reader.ReadInt32();
			var stripGroupOffset = reader.ReadInt32();
			_ = reader.ReadByte(); // flags
			if (stripGroupCount < 0)
			{
				throw new InvalidDataException("VTX mesh stripGroupCount is invalid.");
			}
			if (stripGroupOffset < 0)
			{
				throw new InvalidDataException("VTX mesh stripGroupOffset is invalid.");
			}

			var stripGroups = ReadStripGroups(stream, reader, meshStart, stripGroupCount, stripGroupOffset, usesExtraStripGroupFields);
			meshes.Add(new VtxMesh(StripGroups: stripGroups));
		}

		return meshes;
	}

	private static IReadOnlyList<VtxStripGroup> ReadStripGroups(
		Stream stream,
		BinaryReader reader,
		long meshStart,
		int stripGroupCount,
		int stripGroupOffset,
		bool usesExtraStripGroupFields)
	{
		if (stripGroupCount <= 0 || stripGroupOffset <= 0)
		{
			return Array.Empty<VtxStripGroup>();
		}

		var stripGroupsStart = meshStart + stripGroupOffset;
		if (stripGroupsStart < 0 || stripGroupsStart >= stream.Length)
		{
			throw new InvalidDataException("VTX stripGroupOffset is invalid.");
		}

		var stripGroupHeaderSizeBytes = usesExtraStripGroupFields ? 33 : 25;
		var requiredBytes = (long)stripGroupCount * stripGroupHeaderSizeBytes;
		if (stripGroupsStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX strip group headers exceed file length.");
		}

		var stripGroups = new List<VtxStripGroup>(capacity: stripGroupCount);
		for (var groupIndex = 0; groupIndex < stripGroupCount; groupIndex++)
		{
			var stripGroupStart = stripGroupsStart + (long)groupIndex * stripGroupHeaderSizeBytes;
			stream.Seek(stripGroupStart, SeekOrigin.Begin);

			var vertexCount = reader.ReadInt32();
			var vertexOffset = reader.ReadInt32();
			var indexCount = reader.ReadInt32();
			var indexOffset = reader.ReadInt32();
			_ = reader.ReadInt32(); // stripCount
			_ = reader.ReadInt32(); // stripOffset
			var flags = reader.ReadByte();
			if (vertexCount < 0)
			{
				throw new InvalidDataException("VTX stripGroup vertexCount is invalid.");
			}
			if (vertexOffset < 0)
			{
				throw new InvalidDataException("VTX stripGroup vertexOffset is invalid.");
			}
			if (indexCount < 0)
			{
				throw new InvalidDataException("VTX stripGroup indexCount is invalid.");
			}
			if (indexOffset < 0)
			{
				throw new InvalidDataException("VTX stripGroup indexOffset is invalid.");
			}
			if (usesExtraStripGroupFields)
			{
				_ = reader.ReadInt32(); // topologyIndexCount
				_ = reader.ReadInt32(); // topologyIndexOffset
			}

			var vertexes = ReadVertexes(stream, reader, stripGroupStart, vertexCount, vertexOffset);
			var indexes = ReadIndexes(stream, reader, stripGroupStart, indexCount, indexOffset);

			stripGroups.Add(new VtxStripGroup(Vertexes: vertexes, Indexes: indexes, Flags: flags));
		}

		return stripGroups;
	}

	private static IReadOnlyList<VtxVertex> ReadVertexes(
		Stream stream,
		BinaryReader reader,
		long stripGroupStart,
		int vertexCount,
		int vertexOffset)
	{
		if (vertexCount <= 0 || vertexOffset <= 0)
		{
			return Array.Empty<VtxVertex>();
		}

		if (vertexCount > 10_000_000)
		{
			throw new InvalidDataException("VTX vertexCount is unrealistically large.");
		}

		var vertexesStart = stripGroupStart + vertexOffset;
		var requiredBytes = (long)vertexCount * VtxVertexSizeBytes;
		if (vertexesStart < 0 || vertexesStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX vertexes exceed file length.");
		}

		stream.Seek(vertexesStart, SeekOrigin.Begin);
		var vertexes = new List<VtxVertex>(capacity: vertexCount);
		for (var i = 0; i < vertexCount; i++)
		{
			// boneWeightIndex[3], boneCount, originalMeshVertexIndex, boneId[3]
			_ = reader.ReadByte();
			_ = reader.ReadByte();
			_ = reader.ReadByte();
			_ = reader.ReadByte(); // boneCount
			var originalMeshVertexIndex = reader.ReadUInt16();
			_ = reader.ReadByte();
			_ = reader.ReadByte();
			_ = reader.ReadByte();

			vertexes.Add(new VtxVertex(originalMeshVertexIndex));
		}

		return vertexes;
	}

	private static IReadOnlyList<ushort> ReadIndexes(
		Stream stream,
		BinaryReader reader,
		long stripGroupStart,
		int indexCount,
		int indexOffset)
	{
		if (indexCount <= 0 || indexOffset <= 0)
		{
			return Array.Empty<ushort>();
		}

		if (indexCount > 100_000_000)
		{
			throw new InvalidDataException("VTX indexCount is unrealistically large.");
		}

		var indexesStart = stripGroupStart + indexOffset;
		var requiredBytes = (long)indexCount * sizeof(ushort);
		if (indexesStart < 0 || indexesStart + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VTX indexes exceed file length.");
		}

		stream.Seek(indexesStart, SeekOrigin.Begin);
		var indexes = new ushort[indexCount];
		for (var i = 0; i < indexCount; i++)
		{
			indexes[i] = reader.ReadUInt16();
		}

		return indexes;
	}
}
