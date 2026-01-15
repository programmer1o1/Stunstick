using System.Text;

namespace Stunstick.Core.Vpk;

public static class VpkDirectoryTreeWriter
{
	public static byte[] Build(IReadOnlyList<VpkDirectoryTreeEntry> entries, Encoding? textEncoding = null)
	{
		using var stream = new MemoryStream();
		Write(stream, entries, textEncoding);
		return stream.ToArray();
	}

	public static void Write(Stream outputStream, IReadOnlyList<VpkDirectoryTreeEntry> entries, Encoding? textEncoding = null)
	{
		if (outputStream is null)
		{
			throw new ArgumentNullException(nameof(outputStream));
		}

		if (!outputStream.CanWrite)
		{
			throw new ArgumentException("Stream must be writable.", nameof(outputStream));
		}

		textEncoding ??= Encoding.ASCII;

		using var writer = new BinaryWriter(outputStream, textEncoding, leaveOpen: true);

		var parsedEntries = new List<ParsedEntry>(entries.Count);
		foreach (var entry in entries)
		{
			parsedEntries.Add(Parse(entry));
		}

		parsedEntries.Sort(ParsedEntryComparer.Instance);

		string? currentExtension = null;
		string? currentDirectory = null;

		foreach (var parsed in parsedEntries)
		{
			if (!string.Equals(currentExtension, parsed.Extension, StringComparison.Ordinal))
			{
				if (currentExtension is not null)
				{
					WriteEmptyString(writer); // end file list
					WriteEmptyString(writer); // end directory list
				}

				WriteNullTerminatedString(writer, textEncoding, parsed.Extension);
				currentExtension = parsed.Extension;
				currentDirectory = null;
			}

			if (!string.Equals(currentDirectory, parsed.DirectoryPath, StringComparison.Ordinal))
			{
				if (currentDirectory is not null)
				{
					WriteEmptyString(writer); // end file list
				}

				WriteNullTerminatedString(writer, textEncoding, parsed.DirectoryPath);
				currentDirectory = parsed.DirectoryPath;
			}

			WriteNullTerminatedString(writer, textEncoding, parsed.FileName);

			var preloadData = parsed.Entry.PreloadData;
			if (parsed.Entry.PreloadBytes != preloadData.Length)
			{
				throw new InvalidDataException($"Preload bytes mismatch for \"{parsed.Entry.RelativePath}\" (declared {parsed.Entry.PreloadBytes}, actual {preloadData.Length}).");
			}

			writer.Write(parsed.Entry.Crc32);
			writer.Write(parsed.Entry.PreloadBytes);
			writer.Write(parsed.Entry.ArchiveIndex);
			writer.Write(parsed.Entry.EntryOffset);
			writer.Write(parsed.Entry.EntryLength);
			writer.Write((ushort)0xFFFF);

			if (parsed.Entry.PreloadBytes > 0)
			{
				writer.BaseStream.Write(preloadData.Span);
			}
		}

		if (currentExtension is not null)
		{
			WriteEmptyString(writer); // end file list
			WriteEmptyString(writer); // end directory list
		}

		WriteEmptyString(writer); // end extension list
	}

	private sealed record ParsedEntry(
		string Extension,
		string DirectoryPath,
		string FileName,
		VpkDirectoryTreeEntry Entry
	);

	private static ParsedEntry Parse(VpkDirectoryTreeEntry entry)
	{
		if (string.IsNullOrWhiteSpace(entry.RelativePath))
		{
			throw new ArgumentException("Relative path is required.", nameof(entry));
		}

		var normalizedPath = entry.RelativePath.Replace('\\', '/').TrimStart('/');
		if (normalizedPath == ".." || normalizedPath.StartsWith("../", StringComparison.Ordinal) || normalizedPath.Contains("/../", StringComparison.Ordinal) || normalizedPath.EndsWith("/..", StringComparison.Ordinal))
		{
			throw new InvalidDataException($"Invalid VPK relative path: \"{entry.RelativePath}\".");
		}

		var lastSlash = normalizedPath.LastIndexOf('/');
		var filePart = lastSlash >= 0 ? normalizedPath[(lastSlash + 1)..] : normalizedPath;
		var directoryPath = lastSlash >= 0 ? normalizedPath[..lastSlash] : string.Empty;
		if (string.IsNullOrEmpty(directoryPath))
		{
			directoryPath = " ";
		}

		var lastDot = filePart.LastIndexOf('.');
		if (lastDot <= 0 || lastDot == filePart.Length - 1)
		{
			throw new InvalidDataException($"VPK entry must include a file extension: \"{entry.RelativePath}\".");
		}

		var fileName = filePart[..lastDot];
		var extension = filePart[(lastDot + 1)..];

		return new ParsedEntry(
			Extension: extension,
			DirectoryPath: directoryPath,
			FileName: fileName,
			Entry: entry);
	}

	private static void WriteNullTerminatedString(BinaryWriter writer, Encoding encoding, string value)
	{
		if (value is null)
		{
			throw new ArgumentNullException(nameof(value));
		}

		if (value.Contains('\0'))
		{
			throw new InvalidDataException("VPK strings cannot contain null characters.");
		}

		var bytes = encoding.GetBytes(value);

		writer.Write(bytes);
		writer.Write((byte)0);
	}

	private static void WriteEmptyString(BinaryWriter writer)
	{
		writer.Write((byte)0);
	}

	private sealed class ParsedEntryComparer : IComparer<ParsedEntry>
	{
		public static readonly ParsedEntryComparer Instance = new();

		public int Compare(ParsedEntry? x, ParsedEntry? y)
		{
			if (ReferenceEquals(x, y))
			{
				return 0;
			}

			if (x is null)
			{
				return -1;
			}

			if (y is null)
			{
				return 1;
			}

			var extension = StringComparer.Ordinal.Compare(x.Extension, y.Extension);
			if (extension != 0)
			{
				return extension;
			}

			var directory = StringComparer.Ordinal.Compare(x.DirectoryPath, y.DirectoryPath);
			if (directory != 0)
			{
				return directory;
			}

			return StringComparer.Ordinal.Compare(x.FileName, y.FileName);
		}
	}
}
