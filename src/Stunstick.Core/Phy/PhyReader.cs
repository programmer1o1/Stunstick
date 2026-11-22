using System.Numerics;
using System.Text;
using System.Globalization;
using Stunstick.Core.Steam;

namespace Stunstick.Core.Phy;

public static class PhyReader
{
	private const int MinHeaderSizeBytes = 16;
	private const int MaxSolidCount = 4096;
	private const int MaxTriangleCount = 1_000_000;

	private static ReadOnlySpan<byte> IvpsSignature => "IVPS"u8;

	public static PhyFile Read(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return Read(stream, Path.GetFullPath(path));
	}

	public static PhyFile Read(Stream stream, string sourcePath)
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

		if (stream.Length < MinHeaderSizeBytes)
		{
			throw new InvalidDataException("File is too small to be a valid PHY.");
		}

		var headerStart = stream.Position;
		var headerSize = reader.ReadInt32();
		var headerId = reader.ReadInt32();
		var solidCount = reader.ReadInt32();
		var checksum = reader.ReadInt32();

		if (headerSize < MinHeaderSizeBytes || headerStart + headerSize > stream.Length)
		{
			throw new InvalidDataException("PHY header size is invalid.");
		}

		if (solidCount < 0 || solidCount > MaxSolidCount)
		{
			throw new InvalidDataException("PHY solidCount is invalid.");
		}

		var header = new PhyHeader(
			Size: headerSize,
			Id: headerId,
			SolidCount: solidCount,
			Checksum: checksum);

		stream.Seek(headerStart + headerSize, SeekOrigin.Begin);

		var solids = new List<PhySolid>(capacity: solidCount);
		for (var solidIndex = 0; solidIndex < solidCount; solidIndex++)
		{
			if (stream.Position + 4 > stream.Length)
			{
				break;
			}

			var solidSize = reader.ReadInt32();
			if (solidSize < 0)
			{
				throw new InvalidDataException("PHY solid size is invalid.");
			}

			if (solidSize == 0)
			{
				continue;
			}

			var solidStart = stream.Position;
			var solidEnd = solidStart + solidSize;
			if (solidEnd > stream.Length)
			{
				throw new InvalidDataException("PHY solid data exceeds file length.");
			}

			try
			{
				var solid = ReadSolid(stream, reader, solidSize, solidEnd);
				if (solid is not null)
				{
					solids.Add(solid);
				}
			}
			finally
			{
				stream.Seek(solidEnd, SeekOrigin.Begin);
			}
		}

		return new PhyFile(
			SourcePath: sourcePath,
			Header: header,
			Solids: solids)
		{
			Collision = ReadCollisionMetadata(stream, reader)
		};
	}

	private static PhyCollisionMetadata? ReadCollisionMetadata(Stream stream, BinaryReader reader)
	{
		var remaining = stream.Length - stream.Position;
		if (remaining <= 0 || remaining > 8 * 1024 * 1024)
		{
			return null;
		}

		var text = Encoding.ASCII.GetString(reader.ReadBytes((int)remaining));
		var terminator = text.IndexOf('\0');
		if (terminator >= 0)
		{
			text = text[..terminator];
		}

		VdfObject root;
		try
		{
			root = VdfParser.ParseWithRepeatedKeys(text);
		}
		catch (InvalidDataException)
		{
			return null;
		}

		root.TryGetObject("editparams", out var edit);
		var solids = new List<PhyCollisionSolidMetadata>();
		var limits = new List<PhyJointLimit>();
		var pairs = new List<(int First, int Second)>();
		bool? selfCollisions = null;
		foreach (var (name, value) in root.OrderedEntries!)
		{
			if (value is not VdfObject block)
			{
				continue;
			}

			switch (name)
			{
				case "solid" when ReadIndex(block, "index") is int index:
					solids.Add(new PhyCollisionSolidMetadata(index, ReadString(block, "name") ?? string.Empty,
						ReadNumber(block, "mass"), ReadNumber(block, "massbias"), ReadNumber(block, "inertia"),
						ReadNumber(block, "damping"), ReadNumber(block, "rotdamping")));
					break;
				case "ragdollconstraint" when ReadIndex(block, "child") is int child:
					foreach (var axis in "xyz")
					{
						if (ReadNumber(block, axis + "min") is double min &&
							ReadNumber(block, axis + "max") is double max)
						{
							limits.Add(new PhyJointLimit(child, axis, min, max, ReadNumber(block, axis + "friction") ?? 0));
						}
					}
					break;
				case "collisionrules":
					var selfCollisionValue = ReadString(block, "selfcollisions");
					selfCollisions = selfCollisionValue switch { "0" => false, "1" => true, _ => selfCollisions };
					foreach (var entry in block.OrderedEntries!)
					{
						if (entry.Key == "collisionpair" && entry.Value is VdfString pair)
						{
							var indexes = pair.Value.Split(',');
							if (indexes.Length == 2 &&
								int.TryParse(indexes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) &&
								int.TryParse(indexes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var second))
							{
								pairs.Add((first, second));
							}
						}
					}
					break;
			}
		}

		return edit is null && solids.Count == 0 && limits.Count == 0
			? null
			: new PhyCollisionMetadata(ReadNumber(edit, "totalmass"), ReadString(edit, "rootname"),
				ReadString(edit, "concave") == "1", solids, limits, selfCollisions, pairs);
	}

	private static int? ReadIndex(VdfObject block, string key) =>
		int.TryParse(ReadString(block, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0
			? index
			: null;

	private static double? ReadNumber(VdfObject? block, string key) =>
		double.TryParse(ReadString(block, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
			? value
			: null;

	private static string? ReadString(VdfObject? block, string key) =>
		block is not null && block.TryGetString(key, out var value) ? value : null;

	private static PhySolid? ReadSolid(Stream stream, BinaryReader reader, int solidSize, long solidEnd)
	{
		var phyDataStart = stream.Position;
		if (phyDataStart + 4 > solidEnd)
		{
			return null;
		}

		// Peek VPHY and reset position (matches old Crowbar logic).
		var signature = ReadFixedString(reader, 4);
		stream.Seek(phyDataStart, SeekOrigin.Begin);

		if (string.Equals(signature, "VPHY", StringComparison.Ordinal))
		{
			if (!SkipPhyDataVersion48(reader, stream, solidEnd))
			{
				return null;
			}
		}
		else
		{
			if (!SkipPhyDataVersion37(reader, stream, solidEnd))
			{
				return null;
			}
		}

		var ivpsStart = stream.Position;
		if (ivpsStart + 4 > solidEnd)
		{
			return null;
		}

		var ivps = ReadFixedString(reader, 4);
		if (!string.Equals(ivps, "IVPS", StringComparison.Ordinal))
		{
			if (!TrySeekToSignature(stream, startOffset: phyDataStart, endOffset: solidEnd, signature: IvpsSignature, out var foundOffset))
			{
				return null;
			}

			stream.Seek(foundOffset + 4, SeekOrigin.Begin);
		}

		var ledgeStart = stream.Position;
		if (signature == "VPHY" && ledgeStart == phyDataStart + 76)
		{
			var packedSolid = ReadInlineVertexSolid(reader, solidSize, solidEnd, ledgeStart);
			if (packedSolid is not null)
			{
				return packedSolid;
			}
			stream.Position = ledgeStart;
		}

		// Port of Crowbar's SourcePhyFile.ReadSourceCollisionData for the data we need (convex meshes + vertices).
		// Important: Crowbar drives the face-section loop using vertexDataStreamPosition, which comes from vertexDataOffset.
		var convexMeshes = new List<PhyConvexMesh>();

		var usedVertexIndexes = new List<ushort>();

		var vertexDataStreamPosition = stream.Position + solidSize;
		while (stream.Position < vertexDataStreamPosition)
		{
			var faceSectionStart = stream.Position;
			if (faceSectionStart + 16 > solidEnd)
			{
				break;
			}

			var vertexDataOffset = reader.ReadInt32();
			vertexDataStreamPosition = faceSectionStart + vertexDataOffset;
			if (vertexDataStreamPosition <= faceSectionStart || vertexDataStreamPosition > solidEnd)
			{
				break;
			}

			var boneIndex = reader.ReadInt32() - 1;
			var flags = reader.ReadInt32();
			var triangleCount = reader.ReadInt32();
			if (triangleCount < 0 || triangleCount > MaxTriangleCount)
			{
				break;
			}

			// Each triangle entry is 16 bytes.
			var bytesNeeded = (long)triangleCount * 16;
			if (stream.Position + bytesNeeded > solidEnd)
			{
				break;
			}

			var faces = new List<PhyFace>(capacity: triangleCount);
			for (var i = 0; i < triangleCount; i++)
			{
				var face = ReadFace(reader);
				faces.Add(face);
				AddUnique(usedVertexIndexes, face.VertexIndex0);
				AddUnique(usedVertexIndexes, face.VertexIndex1);
				AddUnique(usedVertexIndexes, face.VertexIndex2);
			}

			convexMeshes.Add(new PhyConvexMesh(
				BoneIndex: boneIndex,
				Flags: flags,
				Faces: faces));
		}

		if (convexMeshes.Count == 0)
		{
			return null;
		}

		if (vertexDataStreamPosition < 0 || vertexDataStreamPosition + 16 > solidEnd)
		{
			return null;
		}

		stream.Seek(vertexDataStreamPosition, SeekOrigin.Begin);

		var vertexCount = usedVertexIndexes.Count;
		if (vertexCount <= 0)
		{
			return null;
		}

		// Vertex entries are 16 bytes: x,y,z,w (float32). Crowbar reads vertexCount entries (not solidEnd / 16).
		var bytesAvailable = solidEnd - vertexDataStreamPosition;
		var maxVerticesByBytes = (int)Math.Min(int.MaxValue, bytesAvailable / 16);
		if (vertexCount > maxVerticesByBytes)
		{
			vertexCount = maxVerticesByBytes;
		}

		var vertices = new List<Vector3>(capacity: vertexCount);
		for (var i = 0; i < vertexCount; i++)
		{
			vertices.Add(ReadVertex(reader));
		}

		return new PhySolid(
			Size: solidSize,
			Vertices: vertices,
			ConvexMeshes: convexMeshes);
	}

	private static PhySolid? ReadInlineVertexSolid(BinaryReader reader, int solidSize, long solidEnd, long ledgeStart)
	{
		const int surfaceSize = 48;
		const int elementSize = 16;
		const int treeNodeSize = 28;
		var stream = reader.BaseStream;
		var surfaceStart = ledgeStart - surfaceSize;
		stream.Position = surfaceStart + 32;
		var treeStart = surfaceStart + reader.ReadInt32();
		if (treeStart <= ledgeStart || treeStart + treeNodeSize > solidEnd)
		{
			return null;
		}

		// Portable MDLForge writes each convex's points directly after its faces.
		// Valve-style shared point buffers continue through the legacy reader.
		var meshes = new List<PhyConvexMesh>();
		var vertices = new List<Vector3>();
		stream.Position = ledgeStart;
		while (stream.Position < treeStart)
		{
			if (stream.Position + elementSize > treeStart)
			{
				return null;
			}
			var start = stream.Position;
			var pointOffset = reader.ReadInt32();
			var boneIndex = reader.ReadInt32() - 1;
			var flags = reader.ReadInt32();
			var triangleCount = reader.ReadUInt16();
			_ = reader.ReadUInt16();
			var ledgeSize = (int)((uint)flags >> 8) * elementSize;
			var pointCount = (ledgeSize - pointOffset) / elementSize;
			if ((flags & 7) != 4 || pointOffset != (triangleCount + 1) * elementSize ||
				pointCount <= 0 || start + ledgeSize > treeStart || vertices.Count + pointCount > ushort.MaxValue + 1)
			{
				return null;
			}

			var faces = new List<PhyFace>(triangleCount);
			for (var i = 0; i < triangleCount; i++)
			{
				var face = ReadFace(reader);
				if (face.VertexIndex0 >= pointCount || face.VertexIndex1 >= pointCount || face.VertexIndex2 >= pointCount)
				{
					return null;
				}
				faces.Add(new PhyFace((ushort)(vertices.Count + face.VertexIndex0),
					(ushort)(vertices.Count + face.VertexIndex1), (ushort)(vertices.Count + face.VertexIndex2)));
			}
			for (var i = 0; i < pointCount; i++)
			{
				vertices.Add(ReadVertex(reader));
			}
			meshes.Add(new PhyConvexMesh(boneIndex, flags, faces));
		}

		return new PhySolid(solidSize, vertices, meshes);
	}

	private static PhyFace ReadFace(BinaryReader reader)
	{
		_ = reader.ReadUInt32();
		return new PhyFace((ushort)reader.ReadUInt32(), (ushort)reader.ReadUInt32(), (ushort)reader.ReadUInt32());
	}

	private static Vector3 ReadVertex(BinaryReader reader)
	{
		var point = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		_ = reader.ReadSingle(); // Padding.
		return point;
	}

	private static void AddUnique(List<ushort> list, ushort value)
	{
		// Crowbar uses List(Of Integer) + Contains; keep behavior (order does not matter, only count).
		for (var i = 0; i < list.Count; i++)
		{
			if (list[i] == value)
			{
				return;
			}
		}

		list.Add(value);
	}

	private static bool SkipPhyDataVersion37(BinaryReader reader, Stream stream, long solidEnd)
	{
		// Matches old Crowbar: fixed 11 int32 values.
		const int bytes = 11 * 4;
		if (stream.Position + bytes > solidEnd)
		{
			return false;
		}

		for (var i = 0; i < 11; i++)
		{
			_ = reader.ReadInt32();
		}

		return true;
	}

	private static bool SkipPhyDataVersion48(BinaryReader reader, Stream stream, long solidEnd)
	{
		// Matches old Crowbar: VPHY + fixed fields up to IVPS.
		const int bytes = 72;
		if (stream.Position + bytes > solidEnd)
		{
			return false;
		}

		// "VPHY"
		_ = reader.ReadBytes(4);

		_ = reader.ReadUInt16(); // version?
		_ = reader.ReadUInt16(); // model type?
		_ = reader.ReadInt32();  // surface size?

		_ = reader.ReadInt32();
		_ = reader.ReadInt32();
		_ = reader.ReadInt32();
		_ = reader.ReadInt32();

		for (var i = 0; i < 11; i++)
		{
			_ = reader.ReadInt32();
		}

		return true;
	}

	private static string ReadFixedString(BinaryReader reader, int length)
	{
		var chars = reader.ReadChars(length);
		return new string(chars);
	}

	private static bool TrySeekToSignature(Stream stream, long startOffset, long endOffset, ReadOnlySpan<byte> signature, out long foundOffset)
	{
		foundOffset = -1;

		if (signature.Length == 0)
		{
			return false;
		}

		if (endOffset - startOffset < signature.Length)
		{
			return false;
		}

		var originalPosition = stream.Position;
		try
		{
			stream.Seek(startOffset, SeekOrigin.Begin);

			const int bufferSize = 8192;
			var overlap = signature.Length - 1;
			var buffer = new byte[bufferSize + overlap];

			var carry = 0;
			var position = startOffset;

			while (position < endOffset)
			{
				var toRead = (int)Math.Min(bufferSize, endOffset - position);
				var read = stream.Read(buffer, carry, toRead);
				if (read <= 0)
				{
					break;
				}

				var total = carry + read;
				var span = buffer.AsSpan(0, total);

				for (var i = 0; i <= total - signature.Length; i++)
				{
					if (span.Slice(i, signature.Length).SequenceEqual(signature))
					{
						foundOffset = position - carry + i;
						return true;
					}
				}

				carry = Math.Min(overlap, total);
				if (carry > 0)
				{
					span.Slice(total - carry, carry).CopyTo(buffer.AsSpan(0, carry));
				}

				position += read;
			}

			return false;
		}
		finally
		{
			stream.Seek(originalPosition, SeekOrigin.Begin);
		}
	}
}
