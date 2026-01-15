using System.Text;

namespace Stunstick.Core.Mdl;

public static class MdlFileNameRewriter
{
	public static void RewriteInternalModelAndAniFileNames(string mdlPath, string internalMdlFileName)
	{
		if (string.IsNullOrWhiteSpace(mdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(mdlPath));
		}

		if (string.IsNullOrWhiteSpace(internalMdlFileName))
		{
			throw new ArgumentException("Internal MDL file name is required.", nameof(internalMdlFileName));
		}

		internalMdlFileName = NormalizeInternalPath(internalMdlFileName);
		if (internalMdlFileName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) == false)
		{
			throw new ArgumentException("Internal MDL file name must end with .mdl.", nameof(internalMdlFileName));
		}

		var internalAniFileName = "models/" + NormalizeInternalPath(Path.ChangeExtension(internalMdlFileName, ".ani") ?? string.Empty);

		using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
		if (stream.Length < 64)
		{
			throw new InvalidDataException("File is too small to be a valid MDL.");
		}

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
		using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

		var id = reader.ReadUInt32();
		if (id != MdlConstants.Idst)
		{
			throw new NotSupportedException("Unsupported MDL (expected IDST signature).");
		}

		var version = reader.ReadInt32();
		_ = reader.ReadInt32(); // checksum

		var fileSizeFieldOffset = version == 53 ? 0x50L : 0x4CL;

		// Rewrite the internal model name (used by tools for display and sometimes as a base for associated files).
		if (version == 53)
		{
			// Header layout: id, version, checksum, nameCopyOffset, name[64], length, ...
			WriteFixedNullPaddedAscii(stream, offset: 0x10, byteCount: 64, internalMdlFileName);

			// If a name copy exists, point it at a newly appended string.
			if (TryReadInt32At(stream, reader, offset: 0x0C, out var nameCopyOffset) && nameCopyOffset > 0)
			{
				AppendStringAndUpdateAbsoluteOffset(stream, writer, offsetFieldOffset: 0x0C, fileSizeFieldOffset, internalMdlFileName);
			}
		}
		else
		{
			// Header layout: id, version, checksum, name[64], length, ...
			WriteFixedNullPaddedAscii(stream, offset: 0x0C, byteCount: 64, internalMdlFileName);
		}

		// Versions 48/49/52 can include a name copy offset relative to a subheader block (base 0x198).
		if (version is 48 or 49 or 52 && TryReadInt32At(stream, reader, offset: 0x1AC, out var nameCopyRelativeOffset) && nameCopyRelativeOffset > 0)
		{
			AppendStringAndUpdateRelativeOffset(stream, writer, offsetFieldOffset: 0x1AC, relativeBase: 0x198, fileSizeFieldOffset, internalMdlFileName);
		}

		// Rewrite the internal anim block file name if present.
		var animBlockNameOffsetFieldOffset = version == 53 ? 0x160L : 0x15CL;
		if (TryReadInt32At(stream, reader, offset: animBlockNameOffsetFieldOffset, out var animBlockNameOffset) &&
			TryReadInt32At(stream, reader, offset: animBlockNameOffsetFieldOffset + 4, out var animBlockCount) &&
			animBlockCount > 0 &&
			animBlockNameOffset > 0)
		{
			AppendStringAndUpdateAbsoluteOffset(stream, writer, offsetFieldOffset: animBlockNameOffsetFieldOffset, fileSizeFieldOffset, internalAniFileName);
		}
	}

	private static string NormalizeInternalPath(string value)
	{
		return (value ?? string.Empty).Replace('\\', '/').Trim();
	}

	private static void WriteFixedNullPaddedAscii(FileStream stream, long offset, int byteCount, string value)
	{
		if (byteCount <= 0)
		{
			return;
		}

		var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
		if (bytes.Length >= byteCount)
		{
			bytes = bytes[..(byteCount - 1)];
		}

		stream.Seek(offset, SeekOrigin.Begin);
		stream.Write(bytes, 0, bytes.Length);
		stream.WriteByte(0);
		for (var i = bytes.Length + 1; i < byteCount; i++)
		{
			stream.WriteByte(0);
		}
	}

	private static bool TryReadInt32At(FileStream stream, BinaryReader reader, long offset, out int value)
	{
		if (offset < 0 || offset + 4 > stream.Length)
		{
			value = 0;
			return false;
		}

		stream.Seek(offset, SeekOrigin.Begin);
		value = reader.ReadInt32();
		return true;
	}

	private static void AppendStringAndUpdateAbsoluteOffset(
		FileStream stream,
		BinaryWriter writer,
		long offsetFieldOffset,
		long fileSizeFieldOffset,
		string value)
	{
		var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);

		stream.Seek(0, SeekOrigin.End);
		var newOffset = stream.Position;
		stream.Write(bytes, 0, bytes.Length);
		stream.WriteByte(0);

		stream.Seek(offsetFieldOffset, SeekOrigin.Begin);
		writer.Write(checked((int)newOffset));

		UpdateHeaderFileSize(stream, writer, fileSizeFieldOffset);
	}

	private static void AppendStringAndUpdateRelativeOffset(
		FileStream stream,
		BinaryWriter writer,
		long offsetFieldOffset,
		long relativeBase,
		long fileSizeFieldOffset,
		string value)
	{
		var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);

		stream.Seek(0, SeekOrigin.End);
		var newOffset = stream.Position;
		stream.Write(bytes, 0, bytes.Length);
		stream.WriteByte(0);

		stream.Seek(offsetFieldOffset, SeekOrigin.Begin);
		writer.Write(checked((int)(newOffset - relativeBase)));

		UpdateHeaderFileSize(stream, writer, fileSizeFieldOffset);
	}

	private static void UpdateHeaderFileSize(FileStream stream, BinaryWriter writer, long fileSizeFieldOffset)
	{
		var fileSize = stream.Length;
		if (fileSizeFieldOffset < 0 || fileSizeFieldOffset + 4 > fileSize)
		{
			return;
		}

		stream.Seek(fileSizeFieldOffset, SeekOrigin.Begin);
		writer.Write(checked((int)fileSize));
	}
}

