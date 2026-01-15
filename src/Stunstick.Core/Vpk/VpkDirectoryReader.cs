using System.Text;

namespace Stunstick.Core.Vpk;

public static class VpkDirectoryReader
{
	public static VpkDirectory Read(Stream directoryFileStream, Encoding? textEncoding = null)
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

		using var reader = new BinaryReader(directoryFileStream, textEncoding, leaveOpen: true);

		var signature = reader.ReadUInt32();
		if (!VpkConstants.IsSupportedSignature(signature))
		{
			throw new InvalidDataException("Not a VPK/FPX directory file (missing signature).");
		}

		var version = reader.ReadUInt32();
		var treeSize = reader.ReadUInt32();

		uint fileDataSectionSize = 0;
		uint archiveMd5SectionSize = 0;
		uint otherMd5SectionSize = 0;
		uint signatureSectionSize = 0;

		if (version == 2)
		{
			fileDataSectionSize = reader.ReadUInt32();
			archiveMd5SectionSize = reader.ReadUInt32();
			otherMd5SectionSize = reader.ReadUInt32();
			signatureSectionSize = reader.ReadUInt32();
		}
		else if (version is not 1)
		{
			throw new NotSupportedException($"Unsupported VPK version: {version}.");
		}

		var header = new VpkHeader(
			Signature: signature,
			Version: version,
			DirectoryTreeSize: treeSize,
			FileDataSectionSize: fileDataSectionSize,
			ArchiveMd5SectionSize: archiveMd5SectionSize,
			OtherMd5SectionSize: otherMd5SectionSize,
			SignatureSectionSize: signatureSectionSize);

		var treeOffset = directoryFileStream.Position;
		var entries = ReadDirectoryTree(reader, textEncoding);

		return new VpkDirectory(header, DirectoryTreeOffset: treeOffset, Entries: entries);
	}

	private static List<VpkEntry> ReadDirectoryTree(BinaryReader reader, Encoding encoding)
	{
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

					var preloadDataOffset = reader.BaseStream.Position;
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

		return entries;
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
}
