using Stunstick.App.Progress;
using System.Text;

namespace Stunstick.App.Unpack;

internal static class HfsUnpacker
{
	private const uint HfsCentralDirectorySignature = 0x02014648; // "HF" 01 02
	private const uint HfsEndOfCentralDirectorySignature = 0x06054648; // "HF" 05 06

	private const int DefaultBufferSize = 1024 * 256;

	private static readonly byte[] FileNameKey = Convert.FromBase64String(
		"""
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
""");

	private sealed record HfsEntry(
		string RelativePath,
		long DataOffset,
		long CompressedSize,
		uint Crc32
	);

	public static IReadOnlyList<PackageEntry> ListEntries(string packagePath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("Package path is required.", nameof(packagePath));
		}

		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("Package file not found.", packagePath);
		}

		using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

		var entries = ReadEntries(stream, reader, cancellationToken);
		return entries
			.Select(entry => new PackageEntry(entry.RelativePath, entry.CompressedSize, entry.Crc32))
			.ToArray();
	}

	public static async Task UnpackAsync(
		string packagePath,
		string outputDirectory,
		bool verifyCrc32,
		bool keepFullPath,
		IProgress<StunstickProgress>? progress,
		CancellationToken cancellationToken,
		IReadOnlySet<string>? onlyRelativePaths = null)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("Package path is required.", nameof(packagePath));
		}

		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
		}

		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("Package file not found.", packagePath);
		}

		Directory.CreateDirectory(outputDirectory);
		var outputRoot = EnsureTrailingSeparator(Path.GetFullPath(outputDirectory));

		await using var inputStream = new FileStream(
			packagePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: DefaultBufferSize,
			useAsync: true);
		using var reader = new BinaryReader(inputStream, Encoding.ASCII, leaveOpen: true);

		var entries = ReadEntries(inputStream, reader, cancellationToken);

		IEnumerable<HfsEntry> entriesToUnpackQuery = entries;
		if (onlyRelativePaths is not null)
		{
			entriesToUnpackQuery = entriesToUnpackQuery.Where(entry => onlyRelativePaths.Contains(entry.RelativePath));
		}

		var entriesToUnpack = entriesToUnpackQuery as IReadOnlyList<HfsEntry> ?? entriesToUnpackQuery.ToArray();
		var totalBytes = entriesToUnpack.Sum(entry => entry.CompressedSize);
		var reporter = new ProgressReporter(progress, operation: "Unpack", totalBytes: totalBytes);

		foreach (var entry in entriesToUnpack)
		{
			cancellationToken.ThrowIfCancellationRequested();

			reporter.SetCurrentItem(entry.RelativePath);
			reporter.SetMessage(null);

			var outputPathFileName = GetSafeOutputPath(outputRoot, entry.RelativePath, keepFullPath);
			var outputPath = Path.GetDirectoryName(outputPathFileName);
			if (!string.IsNullOrEmpty(outputPath))
			{
				Directory.CreateDirectory(outputPath);
			}

			await using var outputStream = new FileStream(
				outputPathFileName,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			inputStream.Seek(entry.DataOffset, SeekOrigin.Begin);

			if (verifyCrc32)
			{
				var crc = Stunstick.Core.Crc32.InitialValue;
				try
				{
					crc = await StreamCopy.CopyExactBytesAsync(
						inputStream,
						outputStream,
						entry.CompressedSize,
						crc,
						cancellationToken,
						onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
				}
				catch (EndOfStreamException ex)
				{
					throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.CompressedSize:N0} bytes). The HFS may be corrupted.", ex);
				}

				crc = Stunstick.Core.Crc32.Finalize(crc);
				if (crc != entry.Crc32)
				{
					throw new InvalidDataException($"CRC32 mismatch for \"{entry.RelativePath}\" (expected 0x{entry.Crc32:x8}, got 0x{crc:x8}).");
				}
			}
			else
			{
				try
				{
					await StreamCopy.CopyExactBytesAsync(
						inputStream,
						outputStream,
						entry.CompressedSize,
						cancellationToken,
						onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
				}
				catch (EndOfStreamException ex)
				{
					throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.CompressedSize:N0} bytes). The HFS may be corrupted.", ex);
				}
			}
		}

		reporter.Complete();
	}

	private static IReadOnlyList<HfsEntry> ReadEntries(FileStream stream, BinaryReader reader, CancellationToken cancellationToken)
	{
		stream.Seek(0, SeekOrigin.Begin);
		if (stream.Length < 4)
		{
			throw new InvalidDataException("File is too small to be a valid HFS.");
		}

		var headerSignature = reader.ReadUInt32();
		if (headerSignature != HfsCentralDirectorySignature)
		{
			throw new InvalidDataException("Not an HFS file (missing signature).");
		}

		var endOfCentralDirOffset = FindEndOfCentralDirectorySignatureOffset(stream);
		if (endOfCentralDirOffset < 0)
		{
			throw new InvalidDataException("HFS end-of-central-directory signature not found.");
		}

		stream.Seek(endOfCentralDirOffset, SeekOrigin.Begin);
		var endSignature = reader.ReadUInt32();
		if (endSignature != HfsEndOfCentralDirectorySignature)
		{
			throw new InvalidDataException("Invalid HFS end-of-central-directory signature.");
		}

		_ = reader.ReadUInt16(); // diskNumber
		_ = reader.ReadUInt16(); // diskWithCentralDirectoryStart
		_ = reader.ReadUInt16(); // centralDirectoryRecordCountOnThisDisk

		var entryCount = reader.ReadUInt16();
		var centralDirectorySize = reader.ReadUInt32();
		var centralDirectoryOffset = reader.ReadUInt32();

		if (centralDirectoryOffset <= 0 || centralDirectoryOffset >= stream.Length)
		{
			throw new InvalidDataException("Invalid HFS central directory offset.");
		}

		// commentLen + comment (ignored)
		var commentLength = reader.ReadUInt16();
		if (commentLength > 0)
		{
			if (stream.Position + commentLength > stream.Length)
			{
				throw new InvalidDataException("Invalid HFS comment length.");
			}

			stream.Seek(commentLength, SeekOrigin.Current);
		}

		if ((long)centralDirectoryOffset + centralDirectorySize > stream.Length)
		{
			throw new InvalidDataException("Invalid HFS central directory size.");
		}

		stream.Seek(centralDirectoryOffset, SeekOrigin.Begin);

		var entries = new List<HfsEntry>(capacity: entryCount);

		for (var i = 0; i < entryCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (stream.Position + 46 > stream.Length)
			{
				throw new EndOfStreamException("Unexpected end of stream while reading HFS central directory.");
			}

			var signature = reader.ReadUInt32();
			if (signature != HfsCentralDirectorySignature)
			{
				throw new InvalidDataException("Invalid HFS central directory entry signature.");
			}

			_ = reader.ReadUInt16(); // versionCreatedWith
			_ = reader.ReadUInt16(); // versionNeededToExtract
			_ = reader.ReadUInt16(); // generalPurposeBitFlag
			_ = reader.ReadUInt16(); // compressionMethod (usually Stored)
			_ = reader.ReadUInt16(); // lastModTime
			_ = reader.ReadUInt16(); // lastModDate

			var crc = reader.ReadUInt32();
			var compressedSize = reader.ReadUInt32();
			_ = reader.ReadUInt32(); // uncompressedSize (unused for now)

			var fileNameSize = reader.ReadUInt16();
			var extraFieldSize = reader.ReadUInt16();
			var commentSize = reader.ReadUInt16();

			_ = reader.ReadUInt16(); // diskNumber
			_ = reader.ReadUInt16(); // internalAttributes
			_ = reader.ReadUInt32(); // externalAttributes
			var localHeaderOffset = reader.ReadUInt32();

			if (fileNameSize > 0)
			{
				var fileNameOffset = stream.Position;
				var fileNameEncoded = reader.ReadBytes(fileNameSize);
				if (fileNameEncoded.Length != fileNameSize)
				{
					throw new EndOfStreamException("Unexpected end of stream while reading HFS file name.");
				}

				DecodeBlockWithKey(fileNameOffset, FileNameKey, fileNameEncoded);
				var decodedName = Encoding.ASCII.GetString(fileNameEncoded);
				var relativePath = NormalizeRelativePath(decodedName);
				if (relativePath.EndsWith("/", StringComparison.Ordinal))
				{
					// Directory entry; no payload.
					SkipCentralDirectoryVariableFields(stream, extraFieldSize, commentSize);
					continue;
				}

				relativePath = StripCompExtension(relativePath);
				SkipCentralDirectoryVariableFields(stream, extraFieldSize, commentSize);

				var savedPos = stream.Position;
				var dataOffset = GetLocalFileDataOffset(stream, reader, localHeaderOffset);
				stream.Seek(savedPos, SeekOrigin.Begin);

				entries.Add(new HfsEntry(relativePath, dataOffset, compressedSize, crc));
			}
			else
			{
				SkipCentralDirectoryVariableFields(stream, extraFieldSize, commentSize);
			}
		}

		return entries;
	}

	private static void SkipCentralDirectoryVariableFields(FileStream stream, ushort extraFieldSize, ushort commentSize)
	{
		var skip = (long)extraFieldSize + commentSize;
		if (skip > 0)
		{
			stream.Seek(skip, SeekOrigin.Current);
		}
	}

	private static long GetLocalFileDataOffset(FileStream stream, BinaryReader reader, uint localHeaderOffset)
	{
		if ((long)localHeaderOffset < 0 || (long)localHeaderOffset >= stream.Length)
		{
			throw new InvalidDataException("Invalid HFS local header offset.");
		}

		stream.Seek(localHeaderOffset, SeekOrigin.Begin);
		if (stream.Position + 30 > stream.Length)
		{
			throw new EndOfStreamException("Unexpected end of stream while reading HFS local header.");
		}

		var signature = reader.ReadUInt32();
		if (signature != HfsCentralDirectorySignature)
		{
			// Some files may use a distinct local header signature ("HF" 03 04).
			const uint altLocalSignature = 0x04034648; // "HF" 03 04
			if (signature != altLocalSignature)
			{
				throw new InvalidDataException("Invalid HFS local header signature.");
			}
		}

		_ = reader.ReadUInt16(); // versionNeededToExtract
		_ = reader.ReadUInt16(); // generalPurposeBitFlag
		_ = reader.ReadUInt16(); // compressionMethod
		_ = reader.ReadUInt16(); // lastModTime
		_ = reader.ReadUInt16(); // lastModDate
		_ = reader.ReadUInt32(); // crc
		var compressedSize = reader.ReadUInt32();
		_ = reader.ReadUInt32(); // uncompressedSize
		var fileNameSize = reader.ReadUInt16();
		var extraFieldSize = reader.ReadUInt16();

		var dataOffset = (long)localHeaderOffset + 30L + fileNameSize + extraFieldSize;

		if (dataOffset < 0 || dataOffset > stream.Length)
		{
			throw new InvalidDataException("Invalid HFS file data offset.");
		}

		if (compressedSize > 0 && dataOffset + compressedSize > stream.Length)
		{
			throw new InvalidDataException("Invalid HFS file data range.");
		}

		return dataOffset;
	}

	private static void DecodeBlockWithKey(long offset, byte[] key, byte[] blockOfBytes)
	{
		if (key.Length == 0)
		{
			return;
		}

		var mask = key.Length - 1;
		for (var i = 0; i < blockOfBytes.Length; i++)
		{
			var keyIndex = (int)((offset + i) & mask);
			blockOfBytes[i] = (byte)(blockOfBytes[i] ^ key[keyIndex]);
		}
	}

	private static long FindEndOfCentralDirectorySignatureOffset(FileStream stream)
	{
		var fileLength = stream.Length;
		if (fileLength < 4)
		{
			return -1;
		}

		const int maxSearchBytes = 1024 * 1024; // safety
		var searchSize = (int)Math.Min(fileLength, maxSearchBytes);
		var startOffset = fileLength - searchSize;

		stream.Seek(startOffset, SeekOrigin.Begin);

		var buffer = new byte[searchSize];
		ReadExact(stream, buffer);

		// Signature bytes, little endian: 48 46 05 06
		for (var i = buffer.Length - 4; i >= 0; i--)
		{
			if (buffer[i] == 0x48 &&
				buffer[i + 1] == 0x46 &&
				buffer[i + 2] == 0x05 &&
				buffer[i + 3] == 0x06)
			{
				return startOffset + i;
			}
		}

		return -1;
	}

	private static void ReadExact(Stream stream, byte[] buffer)
	{
		var totalRead = 0;
		while (totalRead < buffer.Length)
		{
			var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while searching for HFS footer.");
			}

			totalRead += read;
		}
	}

	private static string NormalizeRelativePath(string path)
	{
		var normalized = (path ?? string.Empty).Replace('\\', '/').Trim();
		normalized = normalized.TrimStart('/');
		while (normalized.StartsWith("./", StringComparison.Ordinal))
		{
			normalized = normalized[2..];
		}

		return normalized;
	}

	private static string StripCompExtension(string relativePath)
	{
		return relativePath.EndsWith(".comp", StringComparison.OrdinalIgnoreCase)
			? relativePath[..^".comp".Length]
			: relativePath;
	}

	private static string EnsureTrailingSeparator(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}

		return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
	}

	private static string GetSafeOutputPath(string outputRootDirectory, string relativePath, bool keepFullPath)
	{
		var relativeForOutput = keepFullPath ? relativePath : Path.GetFileName(relativePath);

		var normalizedRelativePath = (relativeForOutput ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
		normalizedRelativePath = normalizedRelativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		var candidate = Path.GetFullPath(Path.Combine(outputRootDirectory, normalizedRelativePath));

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		if (!candidate.StartsWith(outputRootDirectory, comparison))
		{
			throw new InvalidDataException($"Refusing to write outside output directory: \"{relativePath}\".");
		}

		return candidate;
	}
}
