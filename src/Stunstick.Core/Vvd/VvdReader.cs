using System.Numerics;
using System.Text;

namespace Stunstick.Core.Vvd;

public static class VvdReader
{
	private const int MaxLods = 8;

	public static VvdFile Read(string path, int mdlVersion)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(stream, Path.GetFullPath(path), mdlVersion);
	}

	public static VvdFile Read(Stream stream, string sourcePath, int mdlVersion)
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

		if (stream.Length < 4 + 4 + 4)
		{
			throw new InvalidDataException("File is too small to be a valid VVD.");
		}

		var id = ReadFixedString(reader, 4);
		var version = reader.ReadInt32();
		var checksum = reader.ReadInt32();
		var lodCount = reader.ReadInt32();

		var lodVertexCount = new int[MaxLods];
		for (var i = 0; i < MaxLods; i++)
		{
			lodVertexCount[i] = reader.ReadInt32();
		}

		var fixupCount = reader.ReadInt32();
		var fixupTableOffset = reader.ReadInt32();
		var vertexDataOffset = reader.ReadInt32();
		var tangentDataOffset = reader.ReadInt32();

		var header = new VvdHeader(
			Id: id,
			Version: version,
			Checksum: checksum,
			LodCount: lodCount,
			LodVertexCount: lodVertexCount,
			FixupCount: fixupCount,
			FixupTableOffset: fixupTableOffset,
			VertexDataOffset: vertexDataOffset,
			TangentDataOffset: tangentDataOffset);

		var vertexes = ReadVertexes(stream, reader, header, mdlVersion);
		var fixups = ReadFixups(stream, reader, header);
		var fixedVertexesByLod = BuildFixedVertexesByLod(header, vertexes, fixups);

		return new VvdFile(
			SourcePath: sourcePath,
			Header: header,
			Vertexes: vertexes,
			Fixups: fixups,
			FixedVertexesByLod: fixedVertexesByLod);
	}

	private static IReadOnlyList<VvdVertex> ReadVertexes(Stream stream, BinaryReader reader, VvdHeader header, int mdlVersion)
	{
		if (header.LodCount <= 0)
		{
			return Array.Empty<VvdVertex>();
		}

		var vertexCount = header.LodVertexCount.Count > 0 ? header.LodVertexCount[0] : 0;
		if (vertexCount <= 0)
		{
			return Array.Empty<VvdVertex>();
		}

		if (header.VertexDataOffset <= 0 || header.VertexDataOffset >= stream.Length)
		{
			throw new InvalidDataException("VVD vertexDataOffset is invalid.");
		}

		// Source mstudiovertex_t (VVD v4): 48 bytes.
		const int vertexSizeBytes = 48;
		var requiredBytes = (long)vertexCount * vertexSizeBytes;
		if ((long)header.VertexDataOffset + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VVD vertex data exceeds file length.");
		}

		stream.Seek(header.VertexDataOffset, SeekOrigin.Begin);

		var result = new List<VvdVertex>(capacity: vertexCount);
		for (var index = 0; index < vertexCount; index++)
		{
			var w0 = reader.ReadSingle();
			var w1 = reader.ReadSingle();
			var w2 = reader.ReadSingle();

			var b0 = reader.ReadByte();
			var b1 = reader.ReadByte();
			var b2 = reader.ReadByte();
			var boneCount = reader.ReadByte();

			var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var texCoord = new Vector2(reader.ReadSingle(), reader.ReadSingle());

			if (mdlVersion is >= 54 and <= 59)
			{
				// Newer variants store extra per-vertex data; ignore for now.
				reader.ReadSingle();
				reader.ReadSingle();
				reader.ReadSingle();
				reader.ReadSingle();
			}

			result.Add(new VvdVertex(
				BoneWeight: new VvdBoneWeight(w0, w1, w2, b0, b1, b2, boneCount),
				Position: position,
				Normal: normal,
				TexCoord: texCoord));
		}

		return result;
	}

	private static IReadOnlyList<VvdFixup> ReadFixups(Stream stream, BinaryReader reader, VvdHeader header)
	{
		if (header.FixupCount <= 0)
		{
			return Array.Empty<VvdFixup>();
		}

		if (header.FixupTableOffset <= 0 || header.FixupTableOffset >= stream.Length)
		{
			throw new InvalidDataException("VVD fixupTableOffset is invalid.");
		}

		const int fixupSizeBytes = 12;
		var requiredBytes = (long)header.FixupCount * fixupSizeBytes;
		if ((long)header.FixupTableOffset + requiredBytes > stream.Length)
		{
			throw new InvalidDataException("VVD fixup table exceeds file length.");
		}

		stream.Seek(header.FixupTableOffset, SeekOrigin.Begin);
		var result = new List<VvdFixup>(capacity: header.FixupCount);
		for (var i = 0; i < header.FixupCount; i++)
		{
			var lodIndex = reader.ReadInt32();
			var vertexIndex = reader.ReadInt32();
			var vertexCount = reader.ReadInt32();
			result.Add(new VvdFixup(lodIndex, vertexIndex, vertexCount));
		}

		return result;
	}

	private static IReadOnlyList<IReadOnlyList<VvdVertex>> BuildFixedVertexesByLod(
		VvdHeader header,
		IReadOnlyList<VvdVertex> vertexes,
		IReadOnlyList<VvdFixup> fixups)
	{
		if (header.FixupCount <= 0 || fixups.Count == 0)
		{
			return Array.Empty<IReadOnlyList<VvdVertex>>();
		}

		var lodCount = Math.Clamp(header.LodCount, 0, MaxLods);
		var result = new List<IReadOnlyList<VvdVertex>>(capacity: lodCount);
		for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
		{
			var fixedVertexes = new List<VvdVertex>();
			foreach (var fixup in fixups)
			{
				if (fixup.LodIndex < lodIndex)
				{
					continue;
				}

				for (var i = 0; i < fixup.VertexCount; i++)
				{
					var sourceIndex = fixup.VertexIndex + i;
					if ((uint)sourceIndex >= (uint)vertexes.Count)
					{
						break;
					}

					fixedVertexes.Add(vertexes[sourceIndex]);
				}
			}

			result.Add(fixedVertexes);
		}

		return result;
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
}
