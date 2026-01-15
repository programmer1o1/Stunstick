using System.Text;

namespace Stunstick.Core.Mdl;

public static class MdlV53EmbeddedSectionsReader
{
	private const int MinHeaderScanOffset = 0x180;
	private const int MaxHeaderScanOffset = 0x1C0;

	public static bool TryRead(Stream mdlStream, out MdlEmbeddedSections sections)
	{
		if (mdlStream is null)
		{
			throw new ArgumentNullException(nameof(mdlStream));
		}

		if (!mdlStream.CanRead || !mdlStream.CanSeek)
		{
			throw new ArgumentException("Stream must be readable and seekable.", nameof(mdlStream));
		}

		var originalPosition = mdlStream.Position;
		try
		{
			using var reader = new BinaryReader(mdlStream, Encoding.ASCII, leaveOpen: true);
			var fileLength = mdlStream.Length;

			for (var offset = MinHeaderScanOffset; offset <= MaxHeaderScanOffset; offset += 4)
			{
				mdlStream.Seek(offset, SeekOrigin.Begin);

				var vtxOffset = reader.ReadInt32();
				var vvdOffset = reader.ReadInt32();
				var vvcOffset = reader.ReadInt32();
				var phyOffset = reader.ReadInt32();

				var vtxSize = reader.ReadInt32();
				var vvdSize = reader.ReadInt32();
				var vvcSize = reader.ReadInt32();
				var phySize = reader.ReadInt32();

				if (!IsPlausibleSection(vtxOffset, vtxSize, fileLength) ||
					!IsPlausibleSection(vvdOffset, vvdSize, fileLength) ||
					!IsPlausibleSection(vvcOffset, vvcSize, fileLength, allowZero: true) ||
					!IsPlausibleSection(phyOffset, phySize, fileLength, allowZero: true))
				{
					continue;
				}

				if (!HasAsciiSignature(mdlStream, vvdOffset, "IDSV"))
				{
					continue;
				}

				if (!IsPlausibleVtxHeader(mdlStream, vtxOffset, vtxSize))
				{
					continue;
				}

				if (phyOffset > 0 && !IsPlausiblePhyHeader(mdlStream, phyOffset, phySize))
				{
					// Some MDLs appear to embed the physics data in a layout that isn't a standalone PHY file.
					// Don't fail embedded VTX/VVD support for that; just skip PHY for now.
					phyOffset = 0;
					phySize = 0;
				}

				sections = new MdlEmbeddedSections(
					VtxOffset: vtxOffset,
					VtxSize: vtxSize,
					VvdOffset: vvdOffset,
					VvdSize: vvdSize,
					VvcOffset: vvcOffset,
					VvcSize: vvcSize,
					PhyOffset: phyOffset,
					PhySize: phySize);
				return true;
			}

			sections = default!;
			return false;
		}
		finally
		{
			mdlStream.Seek(originalPosition, SeekOrigin.Begin);
		}
	}

	private static bool IsPlausiblePhyHeader(Stream stream, int phyOffset, int phySize)
	{
		if (phySize < 16)
		{
			return false;
		}

		var originalPosition = stream.Position;
		try
		{
			stream.Seek(phyOffset, SeekOrigin.Begin);
			using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

			var headerSize = reader.ReadInt32();
			_ = reader.ReadInt32(); // id
			var solidCount = reader.ReadInt32();
			_ = reader.ReadInt32(); // checksum

			if (headerSize < 16 || headerSize > phySize)
			{
				return false;
			}

			if (solidCount < 0 || solidCount > 4096)
			{
				return false;
			}

			// If there are solids, ensure the first solid size field is within bounds.
			if (solidCount > 0)
			{
				var solidsStart = (long)phyOffset + headerSize;
				var solidsEnd = (long)phyOffset + phySize;
				if (solidsStart + 4 > solidsEnd)
				{
					return false;
				}

				stream.Seek(solidsStart, SeekOrigin.Begin);
				var firstSolidSize = reader.ReadInt32();
				if (firstSolidSize < 0 || solidsStart + 4 + firstSolidSize > solidsEnd)
				{
					return false;
				}
			}

			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			stream.Seek(originalPosition, SeekOrigin.Begin);
		}
	}

	private static bool IsPlausibleSection(int offset, int size, long fileLength, bool allowZero = false)
	{
		if (offset == 0 && size == 0 && allowZero)
		{
			return true;
		}

		if (offset <= 0 || size <= 0)
		{
			return false;
		}

		if (offset >= fileLength)
		{
			return false;
		}

		var end = (long)offset + size;
		if (end > fileLength)
		{
			return false;
		}

		return true;
	}

	private static bool HasAsciiSignature(Stream stream, int offset, string signature)
	{
		var originalPosition = stream.Position;
		try
		{
			stream.Seek(offset, SeekOrigin.Begin);
			var bytes = new byte[4];
			var read = stream.Read(bytes, 0, bytes.Length);
			if (read != 4)
			{
				return false;
			}

			var text = Encoding.ASCII.GetString(bytes);
			return string.Equals(text, signature, StringComparison.Ordinal);
		}
		finally
		{
			stream.Seek(originalPosition, SeekOrigin.Begin);
		}
	}

	private static bool IsPlausibleVtxHeader(Stream stream, int vtxOffset, int vtxSize)
	{
		if (vtxSize < 0x24)
		{
			return false;
		}

		var originalPosition = stream.Position;
		try
		{
			stream.Seek(vtxOffset, SeekOrigin.Begin);
			using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

			var version = reader.ReadInt32();
			if (version is < 5 or > 20)
			{
				return false;
			}

			var vertexCacheSize = reader.ReadInt32();
			if (vertexCacheSize <= 0)
			{
				return false;
			}

			_ = reader.ReadUInt16(); // maxBonesPerStrip
			_ = reader.ReadUInt16(); // maxBonesPerTri
			var maxBonesPerVertex = reader.ReadInt32();
			if (maxBonesPerVertex <= 0 || maxBonesPerVertex > 1024)
			{
				return false;
			}

			_ = reader.ReadInt32(); // checksum
			var lodCount = reader.ReadInt32();
			if (lodCount < 0 || lodCount > 32)
			{
				return false;
			}

			_ = reader.ReadInt32(); // materialReplacementListOffset
			var bodyPartCount = reader.ReadInt32();
			var bodyPartOffset = reader.ReadInt32();
			if (bodyPartCount < 0 || bodyPartCount > 4096)
			{
				return false;
			}
			if (bodyPartCount > 0 && (bodyPartOffset <= 0 || bodyPartOffset >= vtxSize))
			{
				return false;
			}

			return true;
		}
		finally
		{
			stream.Seek(originalPosition, SeekOrigin.Begin);
		}
	}
}
