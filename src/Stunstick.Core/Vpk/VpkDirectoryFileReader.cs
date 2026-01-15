using System.Buffers.Binary;
using System.Text;

namespace Stunstick.Core.Vpk;

public static class VpkDirectoryFileReader
{
	public static VpkDirectoryFile Read(Stream directoryFileStream, Encoding? textEncoding = null)
	{
		if (directoryFileStream is null)
		{
			throw new ArgumentNullException(nameof(directoryFileStream));
		}

		if (!directoryFileStream.CanSeek)
		{
			throw new ArgumentException("Stream must be seekable.", nameof(directoryFileStream));
		}

		textEncoding ??= Encoding.Default;

		directoryFileStream.Seek(0, SeekOrigin.Begin);

		var header12 = ReadExactBytes(directoryFileStream, 12);
		var signature = BinaryPrimitives.ReadUInt32LittleEndian(header12.AsSpan(0, 4));
		if (!VpkConstants.IsSupportedSignature(signature))
		{
			throw new InvalidDataException("Not a VPK/FPX directory file (missing signature).");
		}

		var version = BinaryPrimitives.ReadUInt32LittleEndian(header12.AsSpan(4, 4));
		var treeSize = BinaryPrimitives.ReadUInt32LittleEndian(header12.AsSpan(8, 4));

		byte[] headerBytes = header12;
		uint fileDataSectionSize = 0;
		uint archiveMd5SectionSize = 0;
		uint otherMd5SectionSize = 0;
		uint signatureSectionSize = 0;

		if (version == 2)
		{
			var extra = ReadExactBytes(directoryFileStream, 16);
			headerBytes = new byte[28];
			Buffer.BlockCopy(header12, 0, headerBytes, 0, header12.Length);
			Buffer.BlockCopy(extra, 0, headerBytes, header12.Length, extra.Length);

			fileDataSectionSize = BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(0, 4));
			archiveMd5SectionSize = BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(4, 4));
			otherMd5SectionSize = BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(8, 4));
			signatureSectionSize = BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(12, 4));
		}
		else if (version is not 1)
		{
			throw new NotSupportedException($"Unsupported VPK version: {version}.");
		}

		var treeOffset = headerBytes.Length;
		var treeBytes = ReadExactBytes(directoryFileStream, checked((int)treeSize));

		var header = new VpkHeader(
			Signature: signature,
			Version: version,
			DirectoryTreeSize: treeSize,
			FileDataSectionSize: fileDataSectionSize,
			ArchiveMd5SectionSize: archiveMd5SectionSize,
			OtherMd5SectionSize: otherMd5SectionSize,
			SignatureSectionSize: signatureSectionSize);

		var entries = ParseDirectoryTree(treeBytes, treeOffset, textEncoding);
		var directory = new VpkDirectory(header, DirectoryTreeOffset: treeOffset, Entries: entries);

		VpkV2Metadata? v2Metadata = null;
		if (version == 2)
		{
			var archiveMd5Offset = checked(treeOffset + treeSize + fileDataSectionSize);
			directoryFileStream.Seek(archiveMd5Offset, SeekOrigin.Begin);
			var archiveMd5Bytes = ReadExactBytes(directoryFileStream, checked((int)archiveMd5SectionSize));
			var archiveMd5Entries = ParseArchiveMd5Section(archiveMd5Bytes);

			var otherMd5Offset = checked(archiveMd5Offset + archiveMd5SectionSize);
			directoryFileStream.Seek(otherMd5Offset, SeekOrigin.Begin);
			var otherMd5Bytes = ReadExactBytes(directoryFileStream, checked((int)otherMd5SectionSize));
			var otherMd5 = ParseOtherMd5Section(otherMd5Bytes);

			var signatureOffset = checked(otherMd5Offset + otherMd5SectionSize);
			directoryFileStream.Seek(signatureOffset, SeekOrigin.Begin);
			var signatureBytes = ReadExactBytes(directoryFileStream, checked((int)signatureSectionSize));
			var signatureSection = ParseSignatureSection(signatureBytes);

			v2Metadata = new VpkV2Metadata(
				FileDataSectionSize: fileDataSectionSize,
				ArchiveMd5SectionBytes: archiveMd5Bytes,
				ArchiveMd5Entries: archiveMd5Entries,
				OtherMd5: otherMd5,
				SignatureSectionBytes: signatureBytes,
				SignatureSection: signatureSection);
		}

		return new VpkDirectoryFile(
			HeaderBytes: headerBytes,
			Directory: directory,
			DirectoryTreeBytes: treeBytes,
			V2Metadata: v2Metadata);
	}

	private static List<VpkEntry> ParseDirectoryTree(byte[] treeBytes, long treeOffset, Encoding encoding)
	{
		using var treeStream = new MemoryStream(treeBytes, writable: false);
		using var reader = new BinaryReader(treeStream, encoding, leaveOpen: true);

		var entries = new List<VpkEntry>();

		while (true)
		{
			var extension = ReadNullTerminatedString(reader, encoding);
			if (string.IsNullOrEmpty(extension))
			{
				break;
			}

			while (true)
			{
				var directoryPath = ReadNullTerminatedString(reader, encoding);
				if (string.IsNullOrEmpty(directoryPath))
				{
					break;
				}

				while (true)
				{
					var fileName = ReadNullTerminatedString(reader, encoding);
					if (string.IsNullOrEmpty(fileName))
					{
						break;
					}

					var crc32 = reader.ReadUInt32();
					var preloadBytes = reader.ReadUInt16();
					var archiveIndex = reader.ReadUInt16();
					var entryOffset = reader.ReadUInt32();
					var entryLength = reader.ReadUInt32();
					var terminator = reader.ReadUInt16();
					if (terminator != 0xFFFF)
					{
						throw new InvalidDataException("Invalid VPK directory entry terminator.");
					}

					var preloadDataOffset = treeOffset + reader.BaseStream.Position;
					if (preloadBytes > 0)
					{
						reader.BaseStream.Seek(preloadBytes, SeekOrigin.Current);
					}

					var relativePath = BuildRelativePath(directoryPath, fileName, extension);
					entries.Add(new VpkEntry(
						RelativePath: relativePath,
						Crc32: crc32,
						PreloadBytes: preloadBytes,
						ArchiveIndex: archiveIndex,
						EntryOffset: entryOffset,
						EntryLength: entryLength,
						PreloadDataOffset: preloadDataOffset));
				}
			}
		}

		if (reader.BaseStream.Position != treeBytes.Length)
		{
			throw new InvalidDataException("VPK directory tree size mismatch.");
		}

		return entries;
	}

	private static List<VpkArchiveMd5Entry> ParseArchiveMd5Section(byte[] archiveMd5Bytes)
	{
		var entries = new List<VpkArchiveMd5Entry>();
		if (archiveMd5Bytes.Length == 0)
		{
			return entries;
		}

		if (archiveMd5Bytes.Length % 28 != 0)
		{
			throw new InvalidDataException("Invalid VPK archive MD5 section size.");
		}

		var span = archiveMd5Bytes.AsSpan();
		for (var offset = 0; offset < archiveMd5Bytes.Length; offset += 28)
		{
			var archiveIndex = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
			var archiveOffset = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 4, 4));
			var length = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset + 8, 4));
			var md5 = span.Slice(offset + 12, 16).ToArray();
			entries.Add(new VpkArchiveMd5Entry(
				ArchiveIndex: archiveIndex,
				ArchiveOffset: archiveOffset,
				Length: length,
				Md5: md5));
		}

		return entries;
	}

	private static VpkOtherMd5? ParseOtherMd5Section(byte[] otherMd5Bytes)
	{
		if (otherMd5Bytes.Length == 0)
		{
			return null;
		}

		if (otherMd5Bytes.Length < 48)
		{
			throw new InvalidDataException("Invalid VPK other MD5 section size.");
		}

		return new VpkOtherMd5(
			TreeChecksum: otherMd5Bytes.AsSpan(0, 16).ToArray(),
			ArchiveMd5SectionChecksum: otherMd5Bytes.AsSpan(16, 16).ToArray(),
			WholeFileChecksum: otherMd5Bytes.AsSpan(32, 16).ToArray());
	}

	private static VpkSignatureSection? ParseSignatureSection(ReadOnlyMemory<byte> signatureSectionBytes)
	{
		if (signatureSectionBytes.IsEmpty)
		{
			return null;
		}

		var span = signatureSectionBytes.Span;
		if (span.Length < 8)
		{
			return null;
		}

		var publicKeySize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4));
		if (publicKeySize > int.MaxValue)
		{
			return null;
		}

		var publicKeyLength = (int)publicKeySize;
		var signatureSizeOffset = checked(4 + publicKeyLength);
		if (signatureSizeOffset + 4 > span.Length)
		{
			return null;
		}

		var signatureSize = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(signatureSizeOffset, 4));
		if (signatureSize > int.MaxValue)
		{
			return null;
		}

		var signatureOffset = checked(signatureSizeOffset + 4);
		var signatureLength = (int)signatureSize;
		if (signatureOffset + signatureLength > span.Length)
		{
			return null;
		}

		return new VpkSignatureSection(
			PublicKey: signatureSectionBytes.Slice(4, publicKeyLength),
			Signature: signatureSectionBytes.Slice(signatureOffset, signatureLength));
	}

	private static string BuildRelativePath(string directoryPath, string fileName, string extension)
	{
		var file = $"{fileName}.{extension}";
		if (directoryPath == " ")
		{
			return file;
		}

		return $"{directoryPath}/{file}";
	}

	private static string ReadNullTerminatedString(BinaryReader reader, Encoding encoding)
	{
		var bytes = new List<byte>(capacity: 32);
		while (true)
		{
			var b = reader.ReadByte();
			if (b == 0)
			{
				break;
			}

			bytes.Add(b);
		}

		return bytes.Count == 0 ? string.Empty : encoding.GetString(bytes.ToArray());
	}

	private static byte[] ReadExactBytes(Stream stream, int length)
	{
		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		if (length == 0)
		{
			return Array.Empty<byte>();
		}

		var buffer = new byte[length];
		var offset = 0;
		while (offset < length)
		{
			var read = stream.Read(buffer, offset, length - offset);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream.");
			}

			offset += read;
		}

		return buffer;
	}
}
