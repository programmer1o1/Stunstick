using Stunstick.App.Progress;
using System.Text;

namespace Stunstick.App.Unpack;

internal static class ApkUnpacker
{
	private const uint ApkId = 0x00002357;
	private const int DefaultBufferSize = 1024 * 256;
	private const int MaxNullTerminatedStringBytes = 1024 * 1024;

	private sealed record ApkDirectoryEntry(
		string RelativePath,
		long DataOffset,
		long DataSize
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
		var entries = ReadDirectoryEntries(stream, reader, cancellationToken);
		return entries
			.Select(entry => new PackageEntry(entry.RelativePath, entry.DataSize))
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

		var entries = ReadDirectoryEntries(inputStream, reader, cancellationToken);

		IEnumerable<ApkDirectoryEntry> entriesToUnpackQuery = entries;
		if (onlyRelativePaths is not null)
		{
			entriesToUnpackQuery = entriesToUnpackQuery.Where(entry => onlyRelativePaths.Contains(entry.RelativePath));
		}

		var entriesToUnpack = entriesToUnpackQuery as IReadOnlyList<ApkDirectoryEntry> ?? entriesToUnpackQuery.ToArray();
		var totalBytes = entriesToUnpack.Sum(entry => entry.DataSize);
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
						entry.DataSize,
						crc,
						cancellationToken,
						onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
				}
				catch (EndOfStreamException ex)
				{
					throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.DataSize:N0} bytes). The APK may be corrupted.", ex);
				}

				_ = Stunstick.Core.Crc32.Finalize(crc); // APK has no embedded CRC table; best-effort hash only.
			}
			else
			{
				try
				{
					await StreamCopy.CopyExactBytesAsync(
						inputStream,
						outputStream,
						entry.DataSize,
						cancellationToken,
						onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
				}
				catch (EndOfStreamException ex)
				{
					throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.DataSize:N0} bytes). The APK may be corrupted.", ex);
				}
			}
		}

		reporter.Complete();
	}

	private static IReadOnlyList<ApkDirectoryEntry> ReadDirectoryEntries(FileStream stream, BinaryReader reader, CancellationToken cancellationToken)
	{
		stream.Seek(0, SeekOrigin.Begin);

		if (stream.Length < 16)
		{
			throw new InvalidDataException("File is too small to be a valid APK.");
		}

		var id = reader.ReadUInt32();
		var offsetOfFiles = reader.ReadUInt32();
		var fileCount = reader.ReadUInt32();
		var directoryOffset = reader.ReadUInt32();

		if (id != ApkId)
		{
			throw new InvalidDataException("Not an APK file (missing signature).");
		}

		if (directoryOffset <= 0 || directoryOffset >= stream.Length)
		{
			throw new InvalidDataException("Invalid APK directory offset.");
		}

		stream.Seek(directoryOffset, SeekOrigin.Begin);

		var entries = new List<ApkDirectoryEntry>(capacity: (int)Math.Min(fileCount, 65535u));

		var fileLength = stream.Length;
		for (var i = 0u; i < fileCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (stream.Position + 4 > fileLength)
			{
				throw new EndOfStreamException("Unexpected end of stream while reading APK directory.");
			}

			var pathFileNameSize = reader.ReadUInt32();
			var pathFileName = ReadNullTerminatedString(reader);
			var relativePath = NormalizeRelativePath(pathFileName);
			if (string.IsNullOrWhiteSpace(relativePath))
			{
				throw new InvalidDataException($"Invalid APK entry path at index {i}.");
			}

			if (stream.Position + 16 > fileLength)
			{
				throw new EndOfStreamException("Unexpected end of stream while reading APK directory entry.");
			}

			var dataOffset = reader.ReadUInt32();
			var dataSize = reader.ReadUInt32();
			_ = reader.ReadUInt32(); // offsetOfNextDirectoryEntry (unused)
			_ = reader.ReadUInt32(); // unknown (unused)

			if (dataOffset < offsetOfFiles)
			{
				// Some APKs store directory entries after the header and file data after offsetOfFiles.
				// Treat odd values as best-effort, but still refuse obvious out-of-range data.
			}

			if ((long)dataOffset < 0 || (long)dataOffset > fileLength)
			{
				throw new InvalidDataException($"Invalid data offset for \"{relativePath}\".");
			}

			if ((long)dataOffset + dataSize > fileLength)
			{
				throw new InvalidDataException($"Invalid data range for \"{relativePath}\" (offset 0x{dataOffset:x8}, size {dataSize:N0}).");
			}

			// pathFileNameSize does not include the null terminator; keep as a sanity check only.
			if (pathFileNameSize > 0 && pathFileName.Length > 0 && pathFileNameSize != (uint)Encoding.ASCII.GetByteCount(pathFileName))
			{
				// Ignore mismatch; some files may use a different encoding or include the null.
			}

			entries.Add(new ApkDirectoryEntry(relativePath, dataOffset, dataSize));
		}

		return entries;
	}

	private static string ReadNullTerminatedString(BinaryReader reader)
	{
		var bytes = new List<byte>(capacity: 64);
		while (true)
		{
			var value = reader.ReadByte();
			if (value == 0)
			{
				break;
			}

			bytes.Add(value);
			if (bytes.Count > MaxNullTerminatedStringBytes)
			{
				throw new InvalidDataException("Null-terminated string is too large.");
			}
		}

		return Encoding.ASCII.GetString(bytes.ToArray());
	}

	private static string NormalizeRelativePath(string pathFileName)
	{
		var normalized = (pathFileName ?? string.Empty).Replace('\\', '/').Trim();
		normalized = normalized.TrimStart('/');
		return normalized;
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
