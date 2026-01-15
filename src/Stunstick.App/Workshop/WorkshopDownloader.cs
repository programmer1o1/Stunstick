using Stunstick.App.Progress;
using Stunstick.Core.Compression;
using Stunstick.Core.Steam;
using System.Net;
using System.IO.Compression;

namespace Stunstick.App.Workshop;

internal static class WorkshopDownloader
{
	private const uint GarrysModAppId = 4000;
	private static readonly HttpClient DownloadClient = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.All
	})
	{
		Timeout = TimeSpan.FromMinutes(30)
	};

	public static async Task<WorkshopDownloadResult> DownloadFromCacheAsync(WorkshopDownloadRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.IdOrLink))
		{
			throw new ArgumentException("Workshop ID/link is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.OutputDirectory))
		{
			throw new ArgumentException("Output directory is required.", nameof(request));
		}

		if (!WorkshopIdParser.TryParsePublishedFileId(request.IdOrLink, out var publishedFileId))
		{
			throw new InvalidDataException("Could not parse a Workshop item ID from the given input.");
		}

		var steamRoot = SteamInstallLocator.FindSteamRoot(request.SteamRoot);

		WorkshopItemDetails? details = null;

		WorkshopCacheLocator.WorkshopCacheHit? cacheHit = null;
		if (steamRoot is not null && request.AppId != 0)
		{
			var contentDirForRequestedAppId = WorkshopCacheLocator.FindContentDirectory(steamRoot, request.AppId, publishedFileId);
			if (contentDirForRequestedAppId is not null)
			{
				cacheHit = new WorkshopCacheLocator.WorkshopCacheHit(request.AppId, contentDirForRequestedAppId);
			}
		}

		if (cacheHit is null && request.FetchDetails)
		{
			request.Progress?.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Fetching details..."));
			details = await SteamWorkshopClient.TryGetPublishedFileDetailsAsync(publishedFileId, cancellationToken).ConfigureAwait(false);

			if (details?.ConsumerAppId is uint consumerAppId && consumerAppId != 0 && consumerAppId != request.AppId)
			{
				var contentDirForConsumerApp = steamRoot is null ? null : WorkshopCacheLocator.FindContentDirectory(steamRoot, consumerAppId, publishedFileId);
				if (contentDirForConsumerApp is not null)
				{
					cacheHit = new WorkshopCacheLocator.WorkshopCacheHit(consumerAppId, contentDirForConsumerApp);
				}
			}
		}

		cacheHit ??= steamRoot is null ? null : WorkshopCacheLocator.FindContentDirectoryAnyApp(steamRoot, publishedFileId);
		if (cacheHit is null)
		{
			if (details is null)
			{
				request.Progress?.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Fetching details..."));
				details = await SteamWorkshopClient.TryGetPublishedFileDetailsAsync(publishedFileId, cancellationToken).ConfigureAwait(false);
			}

			if (!string.IsNullOrWhiteSpace(details?.FileUrl))
			{
				return await DownloadFromWebAsync(request, publishedFileId, details!, cancellationToken).ConfigureAwait(false);
			}

			if (request.UseSteamworksWhenNotCached)
			{
				var steamPipeHit = await TryDownloadWithSteamPipeAsync(request, publishedFileId, details, cancellationToken).ConfigureAwait(false);
				if (steamPipeHit is not null)
				{
					cacheHit = steamPipeHit.Value;
				}
			}

			if (cacheHit is null && request.UseSteamCmdWhenNotCached)
			{
				cacheHit = await TryDownloadWithSteamCmdAsync(request, publishedFileId, details, cancellationToken).ConfigureAwait(false);
			}

			if (cacheHit is null)
			{
				if (steamRoot is null)
				{
					throw new DirectoryNotFoundException("Steam root not found. Provide a Steam root folder, or enable Steamworks/SteamCMD fallback.");
				}

				throw new DirectoryNotFoundException($"Workshop item not found in local cache: PublishedFileID {publishedFileId}. Subscribe/download in Steam first.");
			}
		}

		var appId = cacheHit.Value.AppId;
		var contentPath = cacheHit.Value.ContentDirectory;

		Directory.CreateDirectory(request.OutputDirectory);

		var selection = SelectBestPayload(contentPath, request.ConvertToExpectedFileOrFolder);

		if (request.FetchDetails && details is null)
		{
			request.Progress?.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Fetching details..."));
			details = await SteamWorkshopClient.TryGetPublishedFileDetailsAsync(publishedFileId, cancellationToken).ConfigureAwait(false);
		}

		var namingOptions = request.NamingOptions ?? new WorkshopDownloadNamingOptions();
		var contentNameBase = selection.IsDirectory ? null : Path.GetFileNameWithoutExtension(selection.SourcePath);
		var baseName = WorkshopNaming.BuildOutputBaseName(publishedFileId, details, namingOptions, contentNameBase);

		var outputPath = GetOutputPath(request.OutputDirectory, baseName, selection);
		EnsureOutputDoesNotExist(outputPath, request.OverwriteExisting);

		if (selection.IsDirectory)
		{
			await CopyDirectoryAsync(
				sourceDirectory: selection.SourcePath,
				outputDirectory: outputPath,
				progress: request.Progress,
				cancellationToken).ConfigureAwait(false);

			return new WorkshopDownloadResult(
				PublishedFileId: publishedFileId,
				AppId: appId,
				SourcePath: selection.SourcePath,
				OutputPath: outputPath,
				OutputType: WorkshopDownloadOutputType.Directory,
				Details: details);
		}

		await CopyFileAsync(
			sourceFile: selection.SourcePath,
			outputFile: outputPath,
			progress: request.Progress,
			cancellationToken).ConfigureAwait(false);

		outputPath = await MaybeConvertDownloadedFileAsync(request, appId, outputPath, cancellationToken).ConfigureAwait(false);

		return new WorkshopDownloadResult(
			PublishedFileId: publishedFileId,
			AppId: appId,
			SourcePath: selection.SourcePath,
			OutputPath: outputPath,
			OutputType: WorkshopDownloadOutputType.File,
			Details: details);
	}

	private static async Task<WorkshopCacheLocator.WorkshopCacheHit?> TryDownloadWithSteamPipeAsync(WorkshopDownloadRequest request, ulong publishedFileId, WorkshopItemDetails? details, CancellationToken cancellationToken)
	{
		var appId = details?.ConsumerAppId ?? request.AppId;
		if (appId == 0)
		{
			return null;
		}

		request.Progress?.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Downloading via Steamworks..."));

		var result = await SteamPipeClient.DownloadAsync(
			appId,
			publishedFileId,
			request.SteamPipePath,
			request.Progress,
			request.Output,
			cancellationToken).ConfigureAwait(false);

		return new WorkshopCacheLocator.WorkshopCacheHit(result.AppId, result.InstallFolder);
	}

	private static async Task<WorkshopCacheLocator.WorkshopCacheHit?> TryDownloadWithSteamCmdAsync(WorkshopDownloadRequest request, ulong publishedFileId, WorkshopItemDetails? details, CancellationToken cancellationToken)
	{
		var appId = details?.ConsumerAppId ?? request.AppId;
		if (appId == 0)
		{
			return null;
		}

		var steamCmdPath = FindSteamCmdExecutable(request.SteamCmdPath);
		if (steamCmdPath is null)
		{
			return null;
		}

		var installDir = GetSteamCmdInstallDirectory(request.SteamCmdInstallDirectory);
		Directory.CreateDirectory(installDir);

		request.Progress?.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Downloading via SteamCMD..."));

		await RunSteamCmdWorkshopDownloadAsync(
			steamCmdPath,
			installDir,
			request.SteamCmdUsername,
			appId,
			publishedFileId,
			request.Output,
			request.SteamCmdPromptAsync,
			cancellationToken).ConfigureAwait(false);

		var hit = WorkshopCacheLocator.FindContentDirectory(installDir, appId, publishedFileId);
		if (hit is not null)
		{
			return new WorkshopCacheLocator.WorkshopCacheHit(appId, hit);
		}

		return WorkshopCacheLocator.FindContentDirectoryAnyApp(installDir, publishedFileId);
	}

	private static string GetSteamCmdInstallDirectory(string? installDirectoryOverride)
	{
		if (!string.IsNullOrWhiteSpace(installDirectoryOverride))
		{
			return Path.GetFullPath(installDirectoryOverride);
		}

		return Path.Combine(Path.GetTempPath(), "StunstickSteamCmd");
	}

	private static string? FindSteamCmdExecutable(string? steamCmdPathOverride)
	{
		if (!string.IsNullOrWhiteSpace(steamCmdPathOverride))
		{
			var fullPath = Path.GetFullPath(steamCmdPathOverride);
			if (File.Exists(fullPath))
			{
				return fullPath;
			}

			if (Directory.Exists(fullPath))
			{
				foreach (var candidate in GetSteamCmdFileNameCandidates())
				{
					var nested = Path.Combine(fullPath, candidate);
					if (File.Exists(nested))
					{
						return nested;
					}
				}
			}
		}

		var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			foreach (var candidate in GetSteamCmdFileNameCandidates())
			{
				var found = Path.Combine(dir, candidate);
				if (File.Exists(found))
				{
					return found;
				}
			}
		}

		return null;
	}

	private static IReadOnlyList<string> GetSteamCmdFileNameCandidates()
	{
		if (OperatingSystem.IsWindows())
		{
			return new[] { "steamcmd.exe", "steamcmd" };
		}

		return new[] { "steamcmd", "steamcmd.sh" };
	}

	private static async Task RunSteamCmdWorkshopDownloadAsync(
		string steamCmdPath,
		string installDir,
		string? username,
		uint appId,
		ulong publishedFileId,
		IProgress<string>? output,
		Func<SteamCmdPrompt, CancellationToken, Task<string?>>? promptAsync,
		CancellationToken cancellationToken)
	{
		var loginName = string.IsNullOrWhiteSpace(username) ? "anonymous" : username!;

		var workingDirectory = string.Empty;
		try
		{
			var candidate = Path.GetDirectoryName(steamCmdPath);
			if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
			{
				workingDirectory = candidate;
			}
		}
		catch
		{
		}

		var args = new[]
		{
			"+force_install_dir", installDir,
			"+login", loginName,
			"+workshop_download_item", appId.ToString(), publishedFileId.ToString(),
			"validate",
			"+quit"
		};

		var exitCode = await SteamCmdRunner.RunAsync(
			steamCmdPath,
			args,
			workingDirectory,
			output,
			promptAsync,
			cancellationToken).ConfigureAwait(false);

		if (exitCode != 0)
		{
			throw new InvalidDataException($"SteamCMD failed with exit code {exitCode}.");
		}
	}

	private static async Task<WorkshopDownloadResult> DownloadFromWebAsync(WorkshopDownloadRequest request, ulong publishedFileId, WorkshopItemDetails details, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(details.FileUrl))
		{
			throw new InvalidDataException("Workshop item does not provide a downloadable file URL.");
		}

		var uri = new Uri(details.FileUrl, UriKind.Absolute);
		var fileName = !string.IsNullOrWhiteSpace(details.FileName)
			? Path.GetFileName(details.FileName)
			: Path.GetFileName(uri.LocalPath);

		var extension = Path.GetExtension(fileName);
		var contentNameBase = Path.GetFileNameWithoutExtension(fileName);

		var namingOptions = request.NamingOptions ?? new WorkshopDownloadNamingOptions();
		var baseName = WorkshopNaming.BuildOutputBaseName(publishedFileId, details, namingOptions, contentNameBase);

		var outDirFullPath = Path.GetFullPath(request.OutputDirectory);
		Directory.CreateDirectory(outDirFullPath);

		var outputPath = Path.Combine(outDirFullPath, baseName + extension);
		EnsureOutputDoesNotExist(outputPath, request.OverwriteExisting);

		var outputFullPath = Path.GetFullPath(outputPath);
		var outputDir = Path.GetDirectoryName(outputFullPath);
		if (!string.IsNullOrWhiteSpace(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}

		long totalBytes = 0;
		if (details.FileSizeBytes is long sizeFromDetails && sizeFromDetails > 0)
		{
			totalBytes = sizeFromDetails;
		}

		var reporter = new ProgressReporter(request.Progress, operation: "Workshop Download", totalBytes: totalBytes);
		reporter.SetCurrentItem(Path.GetFileName(outputFullPath));
		reporter.SetMessage("Downloading via web...");

		using var response = await DownloadClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		var contentLength = response.Content.Headers.ContentLength;
		if (totalBytes <= 0 && contentLength is long length && length > 0)
		{
			totalBytes = length;
			reporter = new ProgressReporter(request.Progress, operation: "Workshop Download", totalBytes: totalBytes);
			reporter.SetCurrentItem(Path.GetFileName(outputFullPath));
			reporter.SetMessage("Downloading via web...");
		}

		await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		await using var outputStream = new FileStream(outputFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1024 * 256, useAsync: true);

		var buffer = new byte[1024 * 256];
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
			if (read <= 0)
			{
				break;
			}

			await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			reporter.AddCompletedBytes(read);
		}

		reporter.Complete();

		var appId = details.ConsumerAppId ?? request.AppId;
		outputFullPath = await MaybeConvertDownloadedFileAsync(request, appId, outputFullPath, cancellationToken).ConfigureAwait(false);

		return new WorkshopDownloadResult(
			PublishedFileId: publishedFileId,
			AppId: appId,
			SourcePath: uri.ToString(),
			OutputPath: outputFullPath,
			OutputType: WorkshopDownloadOutputType.File,
			Details: details);
	}

	private static async Task<string> MaybeConvertDownloadedFileAsync(WorkshopDownloadRequest request, uint appId, string outputFile, CancellationToken cancellationToken)
	{
		if (!request.ConvertToExpectedFileOrFolder)
		{
			return outputFile;
		}

		var fullPath = Path.GetFullPath(outputFile);
		if (!File.Exists(fullPath))
		{
			return outputFile;
		}

		// Generic: unzip common .zip payloads into a folder next to the archive.
		if (string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
		{
			var targetFolder = Path.Combine(Path.GetDirectoryName(fullPath) ?? ".", Path.GetFileNameWithoutExtension(fullPath));
			try
			{
				EnsureOutputDoesNotExist(targetFolder, request.OverwriteExisting);
				Directory.CreateDirectory(targetFolder);

				var reporter = new ProgressReporter(request.Progress, operation: "Workshop Download", totalBytes: new FileInfo(fullPath).Length);
				reporter.SetCurrentItem(Path.GetFileName(fullPath));
				reporter.SetMessage("Extracting .zip...");

				await Task.Run(() => ZipFile.ExtractToDirectory(fullPath, targetFolder, overwriteFiles: request.OverwriteExisting), cancellationToken).ConfigureAwait(false);
				reporter.Complete();

				try { File.Delete(fullPath); } catch { }

				return targetFolder;
			}
			catch
			{
				// fall through to original file if extraction fails
			}
		}

		// Garry's Mod specific transform: .lzma -> .gma
		if (appId != GarrysModAppId)
		{
			return outputFile;
		}

		if (LooksLikeGmaFile(fullPath))
		{
			return outputFile;
		}

		if (!LooksLikeLzmaStream(fullPath))
		{
			return outputFile;
		}

		var gmaPath = GetDecompressedGmaPath(fullPath);
		try
		{
			EnsureOutputDoesNotExist(gmaPath, request.OverwriteExisting);

			var reporter = new ProgressReporter(request.Progress, operation: "Workshop Download", totalBytes: new FileInfo(fullPath).Length);
			reporter.SetCurrentItem(Path.GetFileName(fullPath));
			reporter.SetMessage("Decompressing .lzma -> .gma...");

			var lastIn = 0L;
			var lzmaProgress = new Progress<(long InBytes, long OutBytes)>(p =>
			{
				var delta = p.InBytes - lastIn;
				if (delta > 0)
				{
					reporter.AddCompletedBytes(delta);
					lastIn = p.InBytes;
				}
			});

			await LzmaDecompressor.DecompressFileAsync(fullPath, gmaPath, cancellationToken, lzmaProgress).ConfigureAwait(false);
			reporter.Complete();

			try
			{
				File.Delete(fullPath);
			}
			catch
			{
			}

			return gmaPath;
		}
		catch
		{
			try
			{
				if (File.Exists(gmaPath))
				{
					File.Delete(gmaPath);
				}
			}
			catch
			{
			}

			return outputFile;
		}
	}

	private static bool LooksLikeGmaFile(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			var buffer = new byte[4];
			var read = stream.Read(buffer, 0, buffer.Length);
			return read == 4 &&
				buffer[0] == (byte)'G' &&
				buffer[1] == (byte)'M' &&
				buffer[2] == (byte)'A' &&
				buffer[3] == (byte)'D';
		}
		catch
		{
			return false;
		}
	}

	private static bool LooksLikeLzmaStream(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (stream.Length < 13)
			{
				return false;
			}

			var prop0 = stream.ReadByte();
			if (prop0 < 0 || prop0 >= 9 * 5 * 5)
			{
				return false;
			}

			var dictBytes = new byte[4];
			if (stream.Read(dictBytes, 0, dictBytes.Length) != dictBytes.Length)
			{
				return false;
			}

			var dict = (uint)(dictBytes[0] | (dictBytes[1] << 8) | (dictBytes[2] << 16) | (dictBytes[3] << 24));
			return dict != 0;
		}
		catch
		{
			return false;
		}
	}

	private static string GetDecompressedGmaPath(string outputFile)
	{
		var fullPath = Path.GetFullPath(outputFile);
		var candidate = Path.ChangeExtension(fullPath, ".gma");
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		if (!string.Equals(fullPath, candidate, comparison))
		{
			return candidate;
		}

		var dir = Path.GetDirectoryName(fullPath) ?? ".";
		var baseName = Path.GetFileNameWithoutExtension(fullPath);
		return Path.Combine(dir, baseName + "_decompressed.gma");
	}

	private static (bool IsDirectory, string SourcePath, string? Extension) SelectBestCachePayload(string contentDir, bool convertToExpectedFileOrFolder)
	{
		if (!convertToExpectedFileOrFolder)
		{
			return (IsDirectory: true, SourcePath: contentDir, Extension: null);
		}

		var topDirectories = Directory.EnumerateDirectories(contentDir, "*", SearchOption.TopDirectoryOnly).ToArray();
		var topFiles = Directory.EnumerateFiles(contentDir, "*", SearchOption.TopDirectoryOnly).ToArray();

		if (topDirectories.Length == 0 && topFiles.Length == 1)
		{
			var sourceFile = topFiles[0];
			var ext = Path.GetExtension(sourceFile);
			return (IsDirectory: false, SourcePath: sourceFile, Extension: ext);
		}

		return (IsDirectory: true, SourcePath: contentDir, Extension: null);
	}

	private static (bool IsDirectory, string SourcePath, string? Extension) SelectBestPayload(string contentPath, bool convertToExpectedFileOrFolder)
	{
		var fullPath = Path.GetFullPath(contentPath);
		if (Directory.Exists(fullPath))
		{
			return SelectBestCachePayload(fullPath, convertToExpectedFileOrFolder);
		}

		if (File.Exists(fullPath))
		{
			return (IsDirectory: false, SourcePath: fullPath, Extension: Path.GetExtension(fullPath));
		}

		throw new DirectoryNotFoundException($"Workshop content not found at: \"{fullPath}\"");
	}

	private static string GetOutputPath(string outputDirectory, string baseName, (bool IsDirectory, string SourcePath, string? Extension) payload)
	{
		var outDirFullPath = Path.GetFullPath(outputDirectory);

		if (payload.IsDirectory)
		{
			return Path.Combine(outDirFullPath, baseName);
		}

		var extension = payload.Extension ?? Path.GetExtension(payload.SourcePath);
		return Path.Combine(outDirFullPath, baseName + extension);
	}

	private static void EnsureOutputDoesNotExist(string outputPath, bool overwriteExisting)
	{
		var fullPath = Path.GetFullPath(outputPath);

		if (File.Exists(fullPath))
		{
			if (!overwriteExisting)
			{
				throw new InvalidDataException($"Output file already exists: \"{fullPath}\"");
			}

			File.Delete(fullPath);
			return;
		}

		if (Directory.Exists(fullPath))
		{
			if (!overwriteExisting)
			{
				throw new InvalidDataException($"Output folder already exists: \"{fullPath}\"");
			}

			Directory.Delete(fullPath, recursive: true);
		}
	}

	private static async Task CopyFileAsync(string sourceFile, string outputFile, IProgress<StunstickProgress>? progress, CancellationToken cancellationToken)
	{
		var sourceFullPath = Path.GetFullPath(sourceFile);
		if (!File.Exists(sourceFullPath))
		{
			throw new FileNotFoundException("Source file not found.", sourceFullPath);
		}

		var totalBytes = new FileInfo(sourceFullPath).Length;
		var reporter = new ProgressReporter(progress, operation: "Workshop Download", totalBytes: totalBytes);
		reporter.SetCurrentItem(Path.GetFileName(sourceFullPath));

		var outputFullPath = Path.GetFullPath(outputFile);
		var outputDir = Path.GetDirectoryName(outputFullPath);
		if (!string.IsNullOrWhiteSpace(outputDir))
		{
			Directory.CreateDirectory(outputDir);
		}

		await using var input = new FileStream(sourceFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1024 * 256, useAsync: true);
		await using var output = new FileStream(outputFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1024 * 256, useAsync: true);

		var buffer = new byte[1024 * 256];
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
			if (read <= 0)
			{
				break;
			}

			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
			reporter.AddCompletedBytes(read);
		}

		reporter.Complete();
	}

	private static async Task CopyDirectoryAsync(string sourceDirectory, string outputDirectory, IProgress<StunstickProgress>? progress, CancellationToken cancellationToken)
	{
		var sourceFullPath = Path.GetFullPath(sourceDirectory);
		if (!Directory.Exists(sourceFullPath))
		{
			throw new DirectoryNotFoundException($"Source folder not found: \"{sourceFullPath}\"");
		}

		var files = Directory.EnumerateFiles(sourceFullPath, "*", SearchOption.AllDirectories).ToArray();
		long totalBytes = 0;
		foreach (var file in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				totalBytes = checked(totalBytes + new FileInfo(file).Length);
			}
			catch
			{
			}
		}

		var reporter = new ProgressReporter(progress, operation: "Workshop Download", totalBytes: totalBytes);

		var outputFullPath = Path.GetFullPath(outputDirectory);
		Directory.CreateDirectory(outputFullPath);

		foreach (var sourceFilePath in files)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relative = Path.GetRelativePath(sourceFullPath, sourceFilePath);
			if (relative.StartsWith("..", StringComparison.Ordinal))
			{
				continue;
			}

			reporter.SetCurrentItem(relative);

			var destinationPath = Path.Combine(outputFullPath, relative);
			var destinationDir = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrWhiteSpace(destinationDir))
			{
				Directory.CreateDirectory(destinationDir);
			}

			await using var input = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1024 * 256, useAsync: true);
			await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 1024 * 256, useAsync: true);

			var buffer = new byte[1024 * 256];
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
				if (read <= 0)
				{
					break;
				}

				await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
				reporter.AddCompletedBytes(read);
			}
		}

		reporter.Complete();
	}
}
