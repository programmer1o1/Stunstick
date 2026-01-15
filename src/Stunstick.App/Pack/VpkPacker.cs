using Stunstick.Core;
using Stunstick.Core.Vpk;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Stunstick.App.Progress;

namespace Stunstick.App.Pack;

internal static class VpkPacker
{
	private const int DefaultBufferSize = 1024 * 256;
	private const long DefaultMaxArchiveSizeBytes = 1024L * 1024L * 1024L;

	public static Task PackAsync(PackRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		return request.MultiFile
			? PackMultiFileAsync(request, cancellationToken)
			: PackSingleFileAsync(request, cancellationToken);
	}

	private static async Task PackSingleFileAsync(PackRequest request, CancellationToken cancellationToken)
	{
		ValidateVpkVersion(request.VpkVersion);

		if (request.VpkVersion == 2 && request.IncludeMd5Sections)
		{
			await PackSingleFileV2WithMd5Async(request, cancellationToken);
			return;
		}

		var outputExtension = Path.GetExtension(request.OutputPackagePath).ToLowerInvariant();
		if (outputExtension is not ".vpk" and not ".fpx")
		{
			throw new NotSupportedException($"Unsupported package type: {outputExtension}");
		}

		var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		var inputRoot = EnsureTrailingSeparator(inputDirectoryFullPath);
		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		var files = CollectFiles(
			inputDirectoryFullPath,
			inputRoot,
			outputFullPath,
			archivePathFilter: null,
			comparison,
			cancellationToken);

		files.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));

		var preloadBytes = GetValidatedPreloadBytes(request);
		ApplyPreloadBytes(files, preloadBytes);

		var signatureSectionBytes = BuildSignatureSectionBytes(request);

		AssignOffsetsSingleFile(files);

		var totalBytes = files.Sum(file => (long)file.Length);
		var progress = new ProgressReporter(request.Progress, operation: "Pack", totalBytes: totalBytes);

		var placeholderTreeEntries = files
			.Select(file => new VpkDirectoryTreeEntry(
				RelativePath: file.RelativePath,
				Crc32: 0,
				PreloadBytes: file.PreloadBytes,
				ArchiveIndex: VpkConstants.DirectoryArchiveIndex,
				EntryOffset: file.EntryOffset,
				EntryLength: file.EntryLength,
				PreloadData: file.PreloadBytes == 0 ? ReadOnlyMemory<byte>.Empty : new byte[file.PreloadBytes]))
			.ToArray();

		var treeEncoding = Encoding.ASCII;
		var placeholderTree = VpkDirectoryTreeWriter.Build(placeholderTreeEntries, textEncoding: treeEncoding);
		if (placeholderTree.LongLength > uint.MaxValue)
		{
			throw new NotSupportedException("VPK directory tree is too large.");
		}

		var signature = outputExtension == ".fpx" ? VpkConstants.FpxSignature : VpkConstants.VpkSignature;
		var fileDataSectionSize = SumEntryLengths(files);
		var header = BuildHeader(
			signature,
			request.VpkVersion,
			treeSize: (uint)placeholderTree.Length,
			fileDataSectionSize: fileDataSectionSize,
			archiveMd5SectionSize: 0,
			otherMd5SectionSize: 0,
			signatureSectionSize: request.VpkVersion == 2 ? (uint)signatureSectionBytes.Length : 0);

		await using var outputStream = new FileStream(
			request.OutputPackagePath,
			FileMode.Create,
			FileAccess.ReadWrite,
			FileShare.None,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		await outputStream.WriteAsync(header, cancellationToken);
		await outputStream.WriteAsync(placeholderTree, cancellationToken);

		var buffer = new byte[DefaultBufferSize];
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			progress.SetCurrentItem(file.RelativePath);

			await using var inputStream = new FileStream(
				file.SourcePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			var crc = Crc32.InitialValue;
			if (file.PreloadBytes > 0)
			{
				file.PreloadData = new byte[file.PreloadBytes];
				await ReadExactAsync(inputStream, file.PreloadData, cancellationToken);
				progress.AddCompletedBytes(file.PreloadBytes);
				crc = Crc32.Update(crc, file.PreloadData);
			}

			uint bytesWritten = 0;

			while (true)
			{
				var read = await inputStream.ReadAsync(buffer, cancellationToken);
				if (read <= 0)
				{
					break;
				}

				crc = Crc32.Update(crc, buffer.AsSpan(0, read));
				progress.AddCompletedBytes(read);
				await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				bytesWritten += (uint)read;
			}

			if (bytesWritten != file.EntryLength)
			{
				throw new InvalidDataException($"Unexpected file length while packing \"{file.SourcePath}\" (expected {file.EntryLength}, wrote {bytesWritten}).");
			}

			file.Crc32 = Crc32.Finalize(crc);
		}

		var finalTreeEntries = files
			.Select(file => new VpkDirectoryTreeEntry(
				RelativePath: file.RelativePath,
				Crc32: file.Crc32,
				PreloadBytes: file.PreloadBytes,
				ArchiveIndex: VpkConstants.DirectoryArchiveIndex,
				EntryOffset: file.EntryOffset,
				EntryLength: file.EntryLength,
				PreloadData: file.PreloadBytes == 0 ? ReadOnlyMemory<byte>.Empty : file.PreloadData))
			.ToArray();

		var finalTree = VpkDirectoryTreeWriter.Build(finalTreeEntries, textEncoding: treeEncoding);
		if (finalTree.Length != placeholderTree.Length)
		{
			throw new InvalidOperationException("VPK directory tree size mismatch while finalizing pack output.");
		}

		outputStream.Seek(header.Length, SeekOrigin.Begin);
		await outputStream.WriteAsync(finalTree, cancellationToken);

		if (signatureSectionBytes.Length > 0)
		{
			outputStream.Seek(0, SeekOrigin.End);
			await outputStream.WriteAsync(signatureSectionBytes, cancellationToken);
		}

		progress.Complete();
		await outputStream.FlushAsync(cancellationToken);
	}

	private static async Task PackSingleFileV2WithMd5Async(PackRequest request, CancellationToken cancellationToken)
	{
		var outputExtension = Path.GetExtension(request.OutputPackagePath).ToLowerInvariant();
		if (outputExtension is not ".vpk" and not ".fpx")
		{
			throw new NotSupportedException($"Unsupported package type: {outputExtension}");
		}

		var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		var inputRoot = EnsureTrailingSeparator(inputDirectoryFullPath);
		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		var files = CollectFiles(
			inputDirectoryFullPath,
			inputRoot,
			outputFullPath,
			archivePathFilter: null,
			comparison,
			cancellationToken);

		files.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));

		var signatureSectionBytes = BuildSignatureSectionBytes(request);

		var preloadBytes = GetValidatedPreloadBytes(request);
		ApplyPreloadBytes(files, preloadBytes);
		AssignOffsetsSingleFile(files);

		var totalBytes = files.Sum(file => (long)file.Length) + files.Sum(file => (long)file.EntryLength);
		var progress = new ProgressReporter(request.Progress, operation: "Pack", totalBytes: totalBytes);
		progress.SetMessage("Hashing");

		var buffer = new byte[DefaultBufferSize];
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			progress.SetCurrentItem(file.RelativePath);
			file.Crc32 = await ComputePreloadAndCrc32Async(file, buffer, progress, cancellationToken);
		}

		progress.SetMessage("Writing");

		var treeEncoding = Encoding.ASCII;
		var treeEntries = files
			.Select(file => new VpkDirectoryTreeEntry(
				RelativePath: file.RelativePath,
				Crc32: file.Crc32,
				PreloadBytes: file.PreloadBytes,
				ArchiveIndex: VpkConstants.DirectoryArchiveIndex,
				EntryOffset: file.EntryOffset,
				EntryLength: file.EntryLength,
				PreloadData: file.PreloadBytes == 0 ? ReadOnlyMemory<byte>.Empty : file.PreloadData))
			.ToArray();

		var tree = VpkDirectoryTreeWriter.Build(treeEntries, textEncoding: treeEncoding);
		if (tree.LongLength > uint.MaxValue)
		{
			throw new NotSupportedException("VPK directory tree is too large.");
		}

		var signature = outputExtension == ".fpx" ? VpkConstants.FpxSignature : VpkConstants.VpkSignature;

		var fileDataSectionSize = SumEntryLengths(files);
		var header = BuildHeader(
			signature,
			version: 2,
			treeSize: (uint)tree.Length,
			fileDataSectionSize: fileDataSectionSize,
			archiveMd5SectionSize: 0,
			otherMd5SectionSize: 48,
			signatureSectionSize: (uint)signatureSectionBytes.Length);

		var treeMd5 = Hashing.Md5(tree);
		var archiveSectionMd5 = Hashing.Md5(Array.Empty<byte>());

		await using var outputStream = new FileStream(
			request.OutputPackagePath,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		using var wholeFileHasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

		await outputStream.WriteAsync(header, cancellationToken);
		wholeFileHasher.AppendData(header);

		await outputStream.WriteAsync(tree, cancellationToken);
		wholeFileHasher.AppendData(tree);

		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (file.EntryLength == 0)
			{
				continue;
			}

			progress.SetCurrentItem(file.RelativePath);

			await using var inputStream = new FileStream(
				file.SourcePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			if (file.PreloadBytes > 0)
			{
				inputStream.Seek(file.PreloadBytes, SeekOrigin.Begin);
			}

			uint bytesWritten = 0;
			while (true)
			{
				var read = await inputStream.ReadAsync(buffer, cancellationToken);
				if (read <= 0)
				{
					break;
				}

				progress.AddCompletedBytes(read);
				wholeFileHasher.AppendData(buffer, 0, read);
				await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				bytesWritten += (uint)read;
			}

			if (bytesWritten != file.EntryLength)
			{
				throw new InvalidDataException($"Unexpected file length while packing \"{file.SourcePath}\" (expected {file.EntryLength}, wrote {bytesWritten}).");
			}
		}

		var wholeFileMd5 = wholeFileHasher.GetHashAndReset();

		var otherMd5Section = new byte[48];
		treeMd5.CopyTo(otherMd5Section, 0);
		archiveSectionMd5.CopyTo(otherMd5Section, 16);
		wholeFileMd5.CopyTo(otherMd5Section, 32);

		await outputStream.WriteAsync(otherMd5Section, cancellationToken);

		if (signatureSectionBytes.Length > 0)
		{
			await outputStream.WriteAsync(signatureSectionBytes, cancellationToken);
		}

		progress.Complete();
		await outputStream.FlushAsync(cancellationToken);
	}

	private static async Task PackMultiFileAsync(PackRequest request, CancellationToken cancellationToken)
	{
		ValidateVpkVersion(request.VpkVersion);

		var outputExtension = Path.GetExtension(request.OutputPackagePath).ToLowerInvariant();
		if (outputExtension is not ".vpk" and not ".fpx")
		{
			throw new NotSupportedException($"Unsupported package type: {outputExtension}");
		}

		var expectedDirectorySuffix = outputExtension == ".fpx" ? "_fdr" : "_dir";
		var baseName = Path.GetFileNameWithoutExtension(request.OutputPackagePath);
		if (!baseName.EndsWith(expectedDirectorySuffix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Multi-file output path must end with \"{expectedDirectorySuffix}{outputExtension}\" (example: \"pak01{expectedDirectorySuffix}{outputExtension}\").");
		}

		var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var maxArchiveSizeBytes = request.MaxArchiveSizeBytes ?? DefaultMaxArchiveSizeBytes;
		if (maxArchiveSizeBytes <= 0 || maxArchiveSizeBytes > uint.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(request.MaxArchiveSizeBytes), "Max archive size must be between 1 byte and 4 GiB.");
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		var inputRoot = EnsureTrailingSeparator(inputDirectoryFullPath);
		var directoryFileFullPath = Path.GetFullPath(request.OutputPackagePath);

		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		var prefix = baseName[..^expectedDirectorySuffix.Length];
		var archivePathFilter = new ArchivePathFilter(
			OutputDirectoryFullPath: Path.GetFullPath(outputDirectory ?? "."),
			Prefix: prefix,
			Extension: outputExtension);

		var files = CollectFiles(
			inputDirectoryFullPath,
			inputRoot,
			directoryFileFullPath,
			archivePathFilter,
			comparison,
			cancellationToken);

		files.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));

		var preloadBytes = GetValidatedPreloadBytes(request);
		ApplyPreloadBytes(files, preloadBytes);

		var signatureSectionBytes = BuildSignatureSectionBytes(request);

		var totalBytes = files.Sum(file => (long)file.Length);
		var progress = new ProgressReporter(request.Progress, operation: "Pack", totalBytes: totalBytes);

		var archives = AssignOffsetsMultiFile(files, maxArchiveSizeBytes);

		var buffer = new byte[DefaultBufferSize];
		foreach (var archive in archives)
		{
			cancellationToken.ThrowIfCancellationRequested();

			archive.Path = GetArchiveFilePath(request.OutputPackagePath, (ushort)archive.ArchiveIndex);
			var archiveDirectory = Path.GetDirectoryName(archive.Path);
			if (!string.IsNullOrWhiteSpace(archiveDirectory))
			{
				Directory.CreateDirectory(archiveDirectory);
			}

			await using var archiveStream = new FileStream(
				archive.Path,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			IncrementalHash? md5 = request.VpkVersion == 2 && request.IncludeMd5Sections
				? IncrementalHash.CreateHash(HashAlgorithmName.MD5)
				: null;

			try
			{
				foreach (var file in archive.Files)
				{
					cancellationToken.ThrowIfCancellationRequested();
					progress.SetCurrentItem(file.RelativePath);

					await using var inputStream = new FileStream(
						file.SourcePath,
						FileMode.Open,
						FileAccess.Read,
						FileShare.ReadWrite,
						bufferSize: DefaultBufferSize,
						useAsync: true);

					var crc = Crc32.InitialValue;

					if (file.PreloadBytes > 0)
					{
						file.PreloadData = new byte[file.PreloadBytes];
						await ReadExactAsync(inputStream, file.PreloadData, cancellationToken);
						progress.AddCompletedBytes(file.PreloadBytes);
						crc = Crc32.Update(crc, file.PreloadData);
					}

					uint bytesWritten = 0;

					while (true)
					{
						var read = await inputStream.ReadAsync(buffer, cancellationToken);
						if (read <= 0)
						{
							break;
						}

						crc = Crc32.Update(crc, buffer.AsSpan(0, read));
						progress.AddCompletedBytes(read);
						md5?.AppendData(buffer, 0, read);
						await archiveStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
						bytesWritten += (uint)read;
					}

					if (bytesWritten != file.EntryLength)
					{
						throw new InvalidDataException($"Unexpected file length while packing \"{file.SourcePath}\" (expected {file.EntryLength}, wrote {bytesWritten}).");
					}

					file.Crc32 = Crc32.Finalize(crc);
				}
			}
			finally
			{
				if (md5 is not null)
				{
					archive.Md5 = md5.GetHashAndReset();
					md5.Dispose();
				}
			}

			archive.Length = (uint)archiveStream.Position;
		}

		foreach (var file in files.Where(file => file.EntryLength == 0 && file.PreloadBytes > 0))
		{
			cancellationToken.ThrowIfCancellationRequested();
			progress.SetCurrentItem(file.RelativePath);

			await using var inputStream = new FileStream(
				file.SourcePath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			var crc = Crc32.InitialValue;
			file.PreloadData = new byte[file.PreloadBytes];
			await ReadExactAsync(inputStream, file.PreloadData, cancellationToken);
			progress.AddCompletedBytes(file.PreloadBytes);
			crc = Crc32.Update(crc, file.PreloadData);
			file.Crc32 = Crc32.Finalize(crc);
		}

		var treeEncoding = Encoding.ASCII;
		var treeEntries = files
			.Select(file => new VpkDirectoryTreeEntry(
				RelativePath: file.RelativePath,
				Crc32: file.Crc32,
				PreloadBytes: file.PreloadBytes,
				ArchiveIndex: file.ArchiveIndex,
				EntryOffset: file.EntryOffset,
				EntryLength: file.EntryLength,
				PreloadData: file.PreloadBytes == 0 ? ReadOnlyMemory<byte>.Empty : file.PreloadData))
			.ToArray();

		var tree = VpkDirectoryTreeWriter.Build(treeEntries, textEncoding: treeEncoding);
		if (tree.LongLength > uint.MaxValue)
		{
			throw new NotSupportedException("VPK directory tree is too large.");
		}

		var signature = outputExtension == ".fpx" ? VpkConstants.FpxSignature : VpkConstants.VpkSignature;

		var archiveMd5Section = request.VpkVersion == 2 && request.IncludeMd5Sections
			? BuildArchiveMd5Section(archives)
			: Array.Empty<byte>();

		var otherMd5Section = request.VpkVersion == 2 && request.IncludeMd5Sections
			? BuildOtherMd5Section(headerPrefixBytes: null, tree, archiveMd5Section)
			: Array.Empty<byte>();

		var header = BuildHeader(
			signature,
			request.VpkVersion,
			treeSize: (uint)tree.Length,
			fileDataSectionSize: 0,
			archiveMd5SectionSize: (uint)archiveMd5Section.Length,
			otherMd5SectionSize: (uint)otherMd5Section.Length,
			signatureSectionSize: request.VpkVersion == 2 ? (uint)signatureSectionBytes.Length : 0);

		if (request.VpkVersion == 2 && request.IncludeMd5Sections)
		{
			otherMd5Section = BuildOtherMd5Section(header, tree, archiveMd5Section);
		}

		await using var directoryStream = new FileStream(
			request.OutputPackagePath,
			FileMode.Create,
			FileAccess.Write,
			FileShare.None,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		await directoryStream.WriteAsync(header, cancellationToken);
		await directoryStream.WriteAsync(tree, cancellationToken);

		if (request.VpkVersion == 2 && request.IncludeMd5Sections)
		{
			await directoryStream.WriteAsync(archiveMd5Section, cancellationToken);
			await directoryStream.WriteAsync(otherMd5Section, cancellationToken);
		}

		if (signatureSectionBytes.Length > 0)
		{
			await directoryStream.WriteAsync(signatureSectionBytes, cancellationToken);
		}

		progress.Complete();
	}

	private static async Task<uint> ComputeCrc32Async(string path, byte[] buffer, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		var crc = Crc32.InitialValue;
		while (true)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read <= 0)
			{
				break;
			}

			crc = Crc32.Update(crc, buffer.AsSpan(0, read));
		}

		return Crc32.Finalize(crc);
	}

	private static uint SumEntryLengths(IReadOnlyList<FileToPack> files)
	{
		long sum = 0;
		foreach (var file in files)
		{
			sum += file.EntryLength;
		}

		if (sum > uint.MaxValue)
		{
			throw new NotSupportedException("VPK output is too large (must fit in 32-bit offsets).");
		}

		return (uint)sum;
	}

	private static ushort GetValidatedPreloadBytes(PackRequest request)
	{
		if (request.PreloadBytes < 0 || request.PreloadBytes > ushort.MaxValue)
		{
			throw new ArgumentOutOfRangeException(nameof(request.PreloadBytes), $"Preload bytes must be between 0 and {ushort.MaxValue}.");
		}

		return (ushort)request.PreloadBytes;
	}

	private static byte[] BuildSignatureSectionBytes(PackRequest request)
	{
		if (request.VpkSignaturePublicKey is null && request.VpkSignature is null)
		{
			return Array.Empty<byte>();
		}

		if (request.VpkVersion != 2)
		{
			throw new NotSupportedException("VPK signature blocks are only supported for VPK v2.");
		}

		if (request.VpkSignaturePublicKey is null || request.VpkSignature is null)
		{
			throw new ArgumentException("Both VpkSignaturePublicKey and VpkSignature must be provided.");
		}

		return BuildSignatureSection(request.VpkSignaturePublicKey, request.VpkSignature);
	}

	private static byte[] BuildSignatureSection(byte[] publicKey, byte[] signature)
	{
		var bytes = new byte[8 + publicKey.Length + signature.Length];
		var span = bytes.AsSpan();

		BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0, 4), (uint)publicKey.Length);
		publicKey.CopyTo(bytes, 4);

		var signatureLengthOffset = 4 + publicKey.Length;
		BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(signatureLengthOffset, 4), (uint)signature.Length);
		signature.CopyTo(bytes, signatureLengthOffset + 4);

		return bytes;
	}

	private static void ApplyPreloadBytes(IReadOnlyList<FileToPack> files, ushort preloadBytes)
	{
		foreach (var file in files)
		{
			file.PreloadBytes = file.Length <= preloadBytes ? (ushort)file.Length : preloadBytes;
			file.EntryLength = file.Length - file.PreloadBytes;
		}
	}

	private static async Task<uint> ComputePreloadAndCrc32Async(FileToPack file, byte[] buffer, ProgressReporter progress, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(
			file.SourcePath,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		var crc = Crc32.InitialValue;

		if (file.PreloadBytes > 0)
		{
			file.PreloadData = new byte[file.PreloadBytes];
			await ReadExactAsync(stream, file.PreloadData, cancellationToken);
			progress.AddCompletedBytes(file.PreloadBytes);
			crc = Crc32.Update(crc, file.PreloadData);
		}

		while (true)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read <= 0)
			{
				break;
			}

			progress.AddCompletedBytes(read);
			crc = Crc32.Update(crc, buffer.AsSpan(0, read));
		}

		return Crc32.Finalize(crc);
	}

	private static async Task ReadExactAsync(Stream input, byte[] buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await input.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while reading preload bytes.");
			}

			offset += read;
		}
	}

	private static void ValidateVpkVersion(uint version)
	{
		if (version is not 1 and not 2)
		{
			throw new NotSupportedException($"Unsupported VPK version: {version}.");
		}
	}

	private static byte[] BuildHeader(
		uint signature,
		uint version,
		uint treeSize,
		uint fileDataSectionSize,
		uint archiveMd5SectionSize,
		uint otherMd5SectionSize,
		uint signatureSectionSize)
	{
		if (version == 1)
		{
			var header = new byte[12];
			BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), signature);
			BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 1);
			BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), treeSize);
			return header;
		}

		var v2Header = new byte[28];
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(0, 4), signature);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(4, 4), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(8, 4), treeSize);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(12, 4), fileDataSectionSize);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(16, 4), archiveMd5SectionSize);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(20, 4), otherMd5SectionSize);
		BinaryPrimitives.WriteUInt32LittleEndian(v2Header.AsSpan(24, 4), signatureSectionSize);
		return v2Header;
	}

	private static byte[] BuildArchiveMd5Section(IReadOnlyList<ArchiveToWrite> archives)
	{
		var bytes = new byte[archives.Count * 28];
		var span = bytes.AsSpan();
		var offset = 0;

		foreach (var archive in archives)
		{
			if (archive.Md5 is null || archive.Md5.Length != 16)
			{
				throw new InvalidOperationException($"Missing MD5 for archive {archive.ArchiveIndex:000}.");
			}

			BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset, 4), archive.ArchiveIndex);
			BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset + 4, 4), 0);
			BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(offset + 8, 4), archive.Length);
			archive.Md5.AsSpan().CopyTo(span.Slice(offset + 12, 16));
			offset += 28;
		}

		return bytes;
	}

	private static byte[] BuildOtherMd5Section(byte[]? headerPrefixBytes, byte[] treeBytes, byte[] archiveMd5SectionBytes)
	{
		if (headerPrefixBytes is null)
		{
			return new byte[48];
		}

		var treeMd5 = Hashing.Md5(treeBytes);
		var archiveSectionMd5 = Hashing.Md5(archiveMd5SectionBytes);

		using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		hasher.AppendData(headerPrefixBytes);
		hasher.AppendData(treeBytes);
		hasher.AppendData(archiveMd5SectionBytes);
		var wholeFileMd5 = hasher.GetHashAndReset();

		var other = new byte[48];
		treeMd5.CopyTo(other, 0);
		archiveSectionMd5.CopyTo(other, 16);
		wholeFileMd5.CopyTo(other, 32);
		return other;
	}

	private static List<FileToPack> CollectFiles(
		string inputDirectoryFullPath,
		string inputRoot,
		string outputFullPath,
		ArchivePathFilter? archivePathFilter,
		StringComparison comparison,
		CancellationToken cancellationToken)
	{
		var relativePathComparer = OperatingSystem.IsWindows()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;

		var seenRelativePaths = new HashSet<string>(relativePathComparer);
		var files = new List<FileToPack>();

		foreach (var filePath in Directory.EnumerateFiles(inputDirectoryFullPath, "*", SearchOption.AllDirectories))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var fullPath = Path.GetFullPath(filePath);
			if (!fullPath.StartsWith(inputRoot, comparison))
			{
				throw new InvalidDataException($"Refusing to pack file outside input directory: \"{filePath}\".");
			}

			if (string.Equals(fullPath, outputFullPath, comparison))
			{
				continue;
			}

			if (archivePathFilter is not null && archivePathFilter.ShouldExclude(fullPath, comparison))
			{
				continue;
			}

			var fileInfo = new FileInfo(fullPath);
			if (fileInfo.Length > uint.MaxValue)
			{
				throw new NotSupportedException($"File too large for VPK (must fit in 32-bit length): \"{fullPath}\".");
			}

			var relativePath = Path.GetRelativePath(inputDirectoryFullPath, fullPath);
			relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
			relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');
			relativePath = relativePath.TrimStart('/');

			if (!seenRelativePaths.Add(relativePath))
			{
				throw new InvalidDataException($"Duplicate VPK entry path: \"{relativePath}\".");
			}

			files.Add(new FileToPack(fullPath, relativePath, (uint)fileInfo.Length));
		}

		return files;
	}

	private static void AssignOffsetsSingleFile(IReadOnlyList<FileToPack> files)
	{
		var offset = 0u;
		foreach (var file in files)
		{
			checked
			{
				file.ArchiveIndex = VpkConstants.DirectoryArchiveIndex;
				file.EntryOffset = offset;
				offset += file.EntryLength;
			}
		}
	}

	private static List<ArchiveToWrite> AssignOffsetsMultiFile(IReadOnlyList<FileToPack> files, long maxArchiveSizeBytes)
	{
		var maxArchiveSize = (uint)maxArchiveSizeBytes;
		var archives = new List<ArchiveToWrite>();

		ushort archiveIndex = 0;
		uint archiveOffset = 0;
		var currentFiles = new List<FileToPack>();

		void FinishArchive()
		{
			if (currentFiles.Count == 0)
			{
				return;
			}

			archives.Add(new ArchiveToWrite(
				archiveIndex,
				currentFiles.ToArray(),
				string.Empty));

			currentFiles = new List<FileToPack>();
		}

		foreach (var file in files)
		{
			if (file.EntryLength == 0)
			{
				file.ArchiveIndex = VpkConstants.DirectoryArchiveIndex;
				file.EntryOffset = 0;
				continue;
			}

			if (file.EntryLength > maxArchiveSize)
			{
				throw new InvalidDataException($"File \"{file.RelativePath}\" ({file.EntryLength} bytes) exceeds max archive size ({maxArchiveSize} bytes).");
			}

			if (archiveOffset > 0 && maxArchiveSize - archiveOffset < file.EntryLength)
			{
				FinishArchive();
				archiveIndex = checked((ushort)(archiveIndex + 1));
				archiveOffset = 0;
			}

			if (archiveIndex == VpkConstants.DirectoryArchiveIndex)
			{
				throw new InvalidOperationException("Too many archive files (archive index overflow).");
			}

			file.ArchiveIndex = archiveIndex;
			file.EntryOffset = archiveOffset;

			currentFiles.Add(file);
			archiveOffset = checked(archiveOffset + file.EntryLength);
		}

		FinishArchive();

		return archives;
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

		private sealed class FileToPack
		{
			public FileToPack(string sourcePath, string relativePath, uint length)
			{
				SourcePath = sourcePath;
				RelativePath = relativePath;
				Length = length;
				EntryLength = length;
			}

			public string SourcePath { get; }
			public string RelativePath { get; }
			public uint Length { get; }
			public ushort PreloadBytes { get; set; }
			public byte[] PreloadData { get; set; } = Array.Empty<byte>();
			public ushort ArchiveIndex { get; set; }
			public uint EntryOffset { get; set; }
			public uint EntryLength { get; set; }
			public uint Crc32 { get; set; }
		}

	private sealed class ArchiveToWrite
	{
		public ArchiveToWrite(uint archiveIndex, FileToPack[] files, string path)
		{
			ArchiveIndex = archiveIndex;
			Files = files;
			Path = path;
		}

		public uint ArchiveIndex { get; }
		public FileToPack[] Files { get; }
		public string Path { get; set; }
		public uint Length { get; set; }
		public byte[]? Md5 { get; set; }
	}

	private sealed record ArchivePathFilter(string OutputDirectoryFullPath, string Prefix, string Extension)
	{
		public bool ShouldExclude(string fullPath, StringComparison comparison)
		{
			if (!fullPath.StartsWith(EnsureTrailingSeparator(OutputDirectoryFullPath), comparison))
			{
				return false;
			}

			var fileName = Path.GetFileName(fullPath);
			if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var withoutExtension = fileName[..^Extension.Length];
			if (!withoutExtension.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var suffix = withoutExtension[Prefix.Length..];
			if (suffix.StartsWith("_dir", StringComparison.OrdinalIgnoreCase) || suffix.StartsWith("_fdr", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (suffix.Length > 1 && suffix[0] == '_' && suffix[1..].All(char.IsDigit))
			{
				return true;
			}

			return false;
		}
	}
}
