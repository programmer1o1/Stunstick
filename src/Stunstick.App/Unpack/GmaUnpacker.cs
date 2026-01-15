using Stunstick.App.Progress;
using System.Buffers.Binary;
using System.Text;

namespace Stunstick.App.Unpack;

internal static class GmaUnpacker
{
	private const byte SupportedVersion = 3;
	private const int DefaultBufferSize = 1024 * 256;
	private const int MaxNullTerminatedStringBytes = 1024 * 1024;

	private sealed record GmaFileEntry(
		string RelativePath,
		long Length,
		uint Crc32);

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

			var entries = ReadGmaHeaderAndFileList(inputStream, cancellationToken);
			if (onlyRelativePaths is null)
			{
				var expectedDataBytes = entries.Sum(entry => entry.Length);
				var remainingDataBytes = inputStream.Length - inputStream.Position;
				if (remainingDataBytes < expectedDataBytes)
				{
					throw new InvalidDataException($"GMA appears truncated (expected at least {expectedDataBytes:N0} data bytes, but only {remainingDataBytes:N0} remain).");
				}
			}

			var totalBytes = onlyRelativePaths is null
				? entries.Sum(entry => entry.Length)
				: entries.Where(entry => onlyRelativePaths.Contains(entry.RelativePath)).Sum(entry => entry.Length);
			var reporter = new ProgressReporter(progress, operation: "Unpack", totalBytes: totalBytes);

		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var shouldExtract = onlyRelativePaths is null || onlyRelativePaths.Contains(entry.RelativePath);
			if (!shouldExtract)
			{
				inputStream.Seek(entry.Length, SeekOrigin.Current);
				continue;
			}

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

				if (verifyCrc32)
				{
					var crc = Stunstick.Core.Crc32.InitialValue;
					try
					{
						crc = await StreamCopy.CopyExactBytesAsync(
							inputStream,
							outputStream,
							entry.Length,
							crc,
							cancellationToken,
							onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
					}
					catch (EndOfStreamException ex)
					{
						throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.Length:N0} bytes). The GMA may be corrupted.", ex);
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
							entry.Length,
							cancellationToken,
							onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
					}
					catch (EndOfStreamException ex)
					{
						throw new EndOfStreamException($"Unexpected end of stream while extracting \"{entry.RelativePath}\" ({entry.Length:N0} bytes). The GMA may be corrupted.", ex);
					}
				}
			}

			reporter.Complete();
	}

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
		var entries = ReadGmaHeaderAndFileList(stream, cancellationToken);
		return entries
			.Select(entry => new PackageEntry(entry.RelativePath, entry.Length, entry.Crc32))
			.ToArray();
	}

	private static List<GmaFileEntry> ReadGmaHeaderAndFileList(Stream stream, CancellationToken cancellationToken)
	{
		var signatureBytes = ReadExactBytes(stream, 4);
		if (signatureBytes[0] != (byte)'G' || signatureBytes[1] != (byte)'M' || signatureBytes[2] != (byte)'A' || signatureBytes[3] != (byte)'D')
		{
			throw new InvalidDataException("Not a GMA file (missing GMAD signature).");
		}

		var version = ReadExactByte(stream);
		if (version != SupportedVersion)
		{
			throw new NotSupportedException($"Unsupported GMA version: {version}.");
		}

		_ = ReadUInt64LittleEndian(stream); // steamid
		_ = ReadUInt64LittleEndian(stream); // timestamp

		long? headerStart = stream.CanSeek ? stream.Position : null;
		if (TryReadEntries(stream, cancellationToken, assumeNoRequiredList: false, out var entries))
		{
			return entries;
		}

		if (headerStart is not null)
		{
			stream.Position = headerStart.Value;
			if (TryReadEntries(stream, cancellationToken, assumeNoRequiredList: true, out entries))
			{
				return entries;
			}
		}

		throw new InvalidDataException("Failed to parse GMA header.");
	}

	private static bool TryReadEntries(Stream stream, CancellationToken cancellationToken, bool assumeNoRequiredList, out List<GmaFileEntry> entries)
	{
		entries = null!;
		try
		{
			if (!assumeNoRequiredList)
			{
				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var requiredContent = ReadNullTerminatedString(stream);
					if (string.IsNullOrEmpty(requiredContent))
					{
						break;
					}
				}
			}
			else
			{
				var peek = stream.ReadByte();
				if (peek < 0)
				{
					throw new EndOfStreamException("Unexpected end of stream.");
				}

				if (peek != 0)
				{
					stream.Position -= 1;
				}
			}

			_ = ReadNullTerminatedString(stream); // addon name
			_ = ReadNullTerminatedString(stream); // addon description
			_ = ReadNullTerminatedString(stream); // addon author
			_ = ReadUInt32LittleEndian(stream); // addon version

			entries = new List<GmaFileEntry>();
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var fileNumber = ReadUInt32LittleEndian(stream);
				if (fileNumber == 0)
				{
					break;
				}

				var relativePath = ReadNullTerminatedString(stream);
				if (string.IsNullOrWhiteSpace(relativePath))
				{
					throw new InvalidDataException($"Invalid GMA entry path for file number {fileNumber}.");
				}

				var length = checked((long)ReadUInt64LittleEndian(stream));
				var crc = ReadUInt32LittleEndian(stream);
				entries.Add(new GmaFileEntry(relativePath, length, crc));
			}

			if (stream.CanSeek)
			{
				var remainingDataBytes = stream.Length - stream.Position;
				var expectedDataBytes = entries.Sum(entry => entry.Length);
				if (expectedDataBytes > remainingDataBytes)
				{
					return false;
				}
			}

			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return false;
		}
	}

	private static string ReadNullTerminatedString(Stream stream)
	{
		var bytes = new List<byte>(capacity: 64);
		while (true)
		{
			var b = ReadExactByte(stream);
			if (b == 0)
			{
				break;
			}

			bytes.Add(b);
			if (bytes.Count > MaxNullTerminatedStringBytes)
			{
				throw new InvalidDataException("Null-terminated string is too large.");
			}
		}

		return Encoding.UTF8.GetString(bytes.ToArray());
	}

	private static byte ReadExactByte(Stream stream)
	{
		var value = stream.ReadByte();
		if (value < 0)
		{
			throw new EndOfStreamException("Unexpected end of stream.");
		}

		return (byte)value;
	}

	private static byte[] ReadExactBytes(Stream stream, int length)
	{
		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
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

	private static uint ReadUInt32LittleEndian(Stream stream)
	{
		var bytes = ReadExactBytes(stream, 4);
		return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
	}

	private static ulong ReadUInt64LittleEndian(Stream stream)
	{
		var bytes = ReadExactBytes(stream, 8);
		return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
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
		normalizedRelativePath = normalizedRelativePath.Replace('\\', Path.DirectorySeparatorChar);
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
