using Stunstick.App.Progress;
using Stunstick.Core.Vpk;
using System.Buffers.Binary;
using System.Text;

namespace Stunstick.App.Unpack;

internal static class VpkUnpacker
{
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

		cancellationToken.ThrowIfCancellationRequested();

		var directoryFilePath = ResolveDirectoryFilePath(packagePath);
		using var directoryStream = new FileStream(directoryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

		var directoryFile = VpkDirectoryFileReader.Read(directoryStream, textEncoding: Encoding.Default);
		cancellationToken.ThrowIfCancellationRequested();
		return directoryFile.Directory.Entries
			.Select(entry => new PackageEntry(
				RelativePath: entry.RelativePath,
				SizeBytes: (long)entry.PreloadBytes + (long)entry.EntryLength,
				Crc32: entry.Crc32))
			.ToArray();
	}

	public static async Task UnpackAsync(
		string packagePath,
		string outputDirectory,
		bool verifyCrc32,
		bool verifyMd5,
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

		var directoryFilePath = ResolveDirectoryFilePath(packagePath);

		Directory.CreateDirectory(outputDirectory);
		var outputRoot = EnsureTrailingSeparator(Path.GetFullPath(outputDirectory));

		await using var directoryStream = new FileStream(
			directoryFilePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: 1024 * 256,
			useAsync: true);

		var directoryFile = VpkDirectoryFileReader.Read(directoryStream, textEncoding: Encoding.Default);
		var directory = directoryFile.Directory;

		IEnumerable<VpkEntry> entriesToUnpackQuery = directory.Entries;
		if (onlyRelativePaths is not null)
		{
			entriesToUnpackQuery = entriesToUnpackQuery.Where(entry => onlyRelativePaths.Contains(entry.RelativePath));
		}

		var entriesToUnpack = entriesToUnpackQuery as IReadOnlyList<VpkEntry> ?? entriesToUnpackQuery.ToArray();

		var totalBytes = entriesToUnpack.Sum(entry => (long)entry.PreloadBytes + (long)entry.EntryLength);
		var reporter = new ProgressReporter(progress, operation: "Unpack", totalBytes: totalBytes);

		var archiveStreams = new Dictionary<ushort, FileStream>();
		try
		{
			if (verifyMd5 && directoryFile.V2Metadata is not null)
			{
				await VerifyV2Md5Async(directoryFilePath, directoryFile, cancellationToken);
			}

			foreach (var entry in entriesToUnpack)
			{
				cancellationToken.ThrowIfCancellationRequested();
				reporter.SetCurrentItem(entry.RelativePath);

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
					bufferSize: 1024 * 256,
					useAsync: true);

				uint crc = 0;
				if (verifyCrc32)
				{
					crc = Stunstick.Core.Crc32.InitialValue;
				}

				// Preload bytes (stored inline in the directory file).
				if (entry.PreloadBytes > 0)
				{
					directoryStream.Seek(entry.PreloadDataOffset, SeekOrigin.Begin);
					if (verifyCrc32)
					{
						crc = await StreamCopy.CopyExactBytesAsync(
							directoryStream,
							outputStream,
							entry.PreloadBytes,
							crc,
							cancellationToken,
							onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
					}
					else
					{
						await StreamCopy.CopyExactBytesAsync(
							directoryStream,
							outputStream,
							entry.PreloadBytes,
							cancellationToken,
							onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
					}
				}

				// Remaining bytes.
				if (entry.EntryLength > 0)
				{
					if (entry.ArchiveIndex == VpkConstants.DirectoryArchiveIndex)
					{
						var absoluteOffset = directory.DataSectionOffset + entry.EntryOffset;
						directoryStream.Seek(absoluteOffset, SeekOrigin.Begin);
						if (verifyCrc32)
						{
							crc = await StreamCopy.CopyExactBytesAsync(
								directoryStream,
								outputStream,
								entry.EntryLength,
								crc,
								cancellationToken,
								onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
						}
						else
						{
							await StreamCopy.CopyExactBytesAsync(
								directoryStream,
								outputStream,
								entry.EntryLength,
								cancellationToken,
								onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
						}
					}
					else
					{
						if (!archiveStreams.TryGetValue(entry.ArchiveIndex, out var archiveStream))
						{
							var archivePath = GetArchiveFilePath(directoryFilePath, entry.ArchiveIndex);
							if (!File.Exists(archivePath))
							{
								throw new FileNotFoundException($"Missing VPK archive file for index {entry.ArchiveIndex:000}.", archivePath);
							}

								archiveStream = new FileStream(
									archivePath,
									FileMode.Open,
									FileAccess.Read,
									FileShare.ReadWrite,
									bufferSize: 1024 * 256,
									useAsync: true);

							archiveStreams[entry.ArchiveIndex] = archiveStream;
						}

						archiveStream.Seek(entry.EntryOffset, SeekOrigin.Begin);
						if (verifyCrc32)
						{
							crc = await StreamCopy.CopyExactBytesAsync(
								archiveStream,
								outputStream,
								entry.EntryLength,
								crc,
								cancellationToken,
								onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
						}
						else
						{
							await StreamCopy.CopyExactBytesAsync(
								archiveStream,
								outputStream,
								entry.EntryLength,
								cancellationToken,
								onBytesCopied: bytes => reporter.AddCompletedBytes(bytes));
						}
					}
				}

				if (verifyCrc32)
				{
					crc = Stunstick.Core.Crc32.Finalize(crc);
					if (crc != entry.Crc32)
					{
						throw new InvalidDataException($"CRC32 mismatch for \"{entry.RelativePath}\" (expected 0x{entry.Crc32:x8}, got 0x{crc:x8}).");
					}
				}
			}

			reporter.Complete();
		}
		finally
		{
			foreach (var stream in archiveStreams.Values)
			{
				await stream.DisposeAsync();
			}
		}
	}

	private static async Task VerifyV2Md5Async(string directoryFilePath, Stunstick.Core.Vpk.VpkDirectoryFile directoryFile, CancellationToken cancellationToken)
	{
		var v2 = directoryFile.V2Metadata;
		if (v2 is null)
		{
			return;
		}

		foreach (var record in v2.ArchiveMd5Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var archivePath = record.ArchiveIndex == VpkConstants.DirectoryArchiveIndex
				? directoryFilePath
				: record.ArchiveIndex <= ushort.MaxValue
					? GetArchiveFilePath(directoryFilePath, (ushort)record.ArchiveIndex)
					: throw new InvalidDataException($"Unsupported archive index in MD5 section: {record.ArchiveIndex}.");

			if (!File.Exists(archivePath))
			{
				throw new FileNotFoundException($"Missing VPK archive file for MD5 index {record.ArchiveIndex:000}.", archivePath);
			}

			var computed = await Stunstick.Core.Hashing.Md5Async(archivePath, record.ArchiveOffset, record.Length, cancellationToken);
			if (!computed.AsSpan().SequenceEqual(record.Md5))
			{
				throw new InvalidDataException($"MD5 mismatch for archive {record.ArchiveIndex:000} at offset 0x{record.ArchiveOffset:x} length {record.Length}.");
			}
		}

		if (v2.OtherMd5 is not null)
		{
			var treeMd5 = Stunstick.Core.Hashing.Md5(directoryFile.DirectoryTreeBytes.Span);
			if (!treeMd5.AsSpan().SequenceEqual(v2.OtherMd5.TreeChecksum))
			{
				throw new InvalidDataException("VPK directory tree MD5 mismatch.");
			}

			var archiveSectionMd5 = Stunstick.Core.Hashing.Md5(v2.ArchiveMd5SectionBytes.Span);
			if (!archiveSectionMd5.AsSpan().SequenceEqual(v2.OtherMd5.ArchiveMd5SectionChecksum))
			{
				throw new InvalidDataException("VPK archive MD5 section MD5 mismatch.");
			}

			var computedWholeFile = await Stunstick.Core.Hashing.Md5PrefixAsync(
				directoryFilePath,
				bytesToHash: directoryFile.HeaderBytes.Length + directoryFile.DirectoryTreeBytes.Length + v2.FileDataSectionSize + (long)v2.ArchiveMd5SectionBytes.Length,
				cancellationToken);

			if (!computedWholeFile.AsSpan().SequenceEqual(v2.OtherMd5.WholeFileChecksum))
			{
				throw new InvalidDataException("VPK whole-file MD5 mismatch.");
			}
		}
	}

	internal static string ResolveDirectoryFilePath(string packagePath)
	{
		// If the input already looks like a directory VPK (has signature), use it.
			try
			{
				using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				if (stream.Length >= 4)
				{
					var buffer = new byte[4];
					var read = stream.Read(buffer, 0, buffer.Length);
					var signature = read == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : 0;
					if (read == 4 && VpkConstants.IsSupportedSignature(signature))
					{
						return packagePath;
					}
				}
		}
		catch
		{
			// Fall through to name-based resolution.
		}

		var directoryPath = Path.GetDirectoryName(packagePath) ?? ".";
		var extension = Path.GetExtension(packagePath);
		var baseName = Path.GetFileNameWithoutExtension(packagePath);
		var directorySuffix = string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase) ? "fdr" : "dir";
		var lastUnderscore = baseName.LastIndexOf('_');
		if (lastUnderscore < 0)
		{
			throw new InvalidOperationException($"VPK directory file not found (expected a *_{directorySuffix}{extension} alongside the archive).");
		}

		var dirFileName = baseName[..(lastUnderscore + 1)] + directorySuffix + extension;
		var dirPathFileName = Path.Combine(directoryPath, dirFileName);
		if (!File.Exists(dirPathFileName))
		{
			throw new FileNotFoundException("VPK directory file not found.", dirPathFileName);
		}

		return dirPathFileName;
	}

	private static string GetArchiveFilePath(string directoryFilePath, ushort archiveIndex)
	{
		var directoryPath = Path.GetDirectoryName(directoryFilePath) ?? ".";
		var extension = Path.GetExtension(directoryFilePath);
		var baseName = Path.GetFileNameWithoutExtension(directoryFilePath);

		var prefix = baseName;
		if (baseName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
		{
			prefix = baseName[..^"_dir".Length];
		}
		else if (baseName.EndsWith("_fdr", StringComparison.OrdinalIgnoreCase))
		{
			prefix = baseName[..^"_fdr".Length];
		}

		var archiveFileName = $"{prefix}_{archiveIndex:000}{extension}";
		return Path.Combine(directoryPath, archiveFileName);
	}

	private static string EnsureTrailingSeparator(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}

		return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
	}

	private static string GetSafeOutputPath(string outputRootDirectory, string vpkRelativePath, bool keepFullPath)
	{
		var relativeForOutput = keepFullPath
			? vpkRelativePath
			: Path.GetFileName(vpkRelativePath);

		var normalizedRelativePath = (relativeForOutput ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
		normalizedRelativePath = normalizedRelativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		var candidate = Path.GetFullPath(Path.Combine(outputRootDirectory, normalizedRelativePath));

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		if (!candidate.StartsWith(outputRootDirectory, comparison))
		{
			throw new InvalidDataException($"Refusing to write outside output directory: \"{vpkRelativePath}\".");
		}

		return candidate;
	}
}
