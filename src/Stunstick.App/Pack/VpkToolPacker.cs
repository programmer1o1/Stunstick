using Stunstick.App.Progress;
using Stunstick.App.Toolchain;

namespace Stunstick.App.Pack;

internal static class VpkToolPacker
{
	private const string MultiFileDirectorySuffix = "_dir";

	public static async Task PackAsync(PackRequest request, ToolchainLauncher toolchainLauncher, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (toolchainLauncher is null)
		{
			throw new ArgumentNullException(nameof(toolchainLauncher));
		}

		var outputExtension = Path.GetExtension(request.OutputPackagePath).Trim();
		if (!string.Equals(outputExtension, ".vpk", StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException($"External VPK tool packing supports .vpk only (got: {outputExtension}).");
		}

		request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "VPK", Message: "Resolving VPK tool..."));

		var vpkToolPath = ResolveVpkToolPath(request);
		if (string.IsNullOrWhiteSpace(vpkToolPath))
		{
			throw new FileNotFoundException("VPK tool not found. Pass --vpk-tool or --game/--steam-appid.");
		}

		if (LooksLikeFilePath(vpkToolPath) && !File.Exists(vpkToolPath))
		{
			throw new FileNotFoundException("VPK tool not found.", vpkToolPath);
		}

		var args = new List<string>(capacity: 8);
		foreach (var token in ToolArgumentSplitter.Split(request.DirectOptions))
		{
			args.Add(token);
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		if (request.MultiFile)
		{
			await PackMultiFileAsync(request, toolchainLauncher, vpkToolPath, args, inputDirectoryFullPath, cancellationToken).ConfigureAwait(false);
			return;
		}

		await PackSingleFileAsync(request, toolchainLauncher, vpkToolPath, args, inputDirectoryFullPath, cancellationToken).ConfigureAwait(false);
	}

	private static async Task PackSingleFileAsync(
		PackRequest request,
		ToolchainLauncher toolchainLauncher,
		string vpkToolPath,
		List<string> args,
		string inputDirectoryFullPath,
		CancellationToken cancellationToken)
	{
		var inputParentDirectory = Path.GetDirectoryName(inputDirectoryFullPath) ?? ".";
		var inputFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectoryFullPath));
		if (string.IsNullOrWhiteSpace(inputFolderName))
		{
			throw new InvalidDataException("Input folder name could not be determined.");
		}

		args.Add(inputFolderName);

		var stdout = CreateLineProgress(request.Progress, prefix: "OUT");
		var stderr = CreateLineProgress(request.Progress, prefix: "ERR");

		request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "VPK", Message: "Running VPK tool..."));

		var exitCode = await toolchainLauncher.LaunchExternalToolAsync(
			toolPath: vpkToolPath,
			toolArguments: args,
			wineOptions: request.WineOptions ?? new WineOptions(),
			steamRootOverride: request.SteamRoot,
			waitForExit: true,
			cancellationToken,
			workingDirectory: inputParentDirectory,
			standardOutput: stdout,
			standardError: stderr).ConfigureAwait(false);

		if (exitCode != 0)
		{
			throw new InvalidDataException($"VPK tool failed with exit code {exitCode}.");
		}

		var producedPath = Path.Combine(inputParentDirectory, inputFolderName + ".vpk");
		if (!File.Exists(producedPath))
		{
			throw new FileNotFoundException("VPK tool did not produce the expected output file.", producedPath);
		}

		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);
		var outputDirectory = Path.GetDirectoryName(outputFullPath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		MoveFileOverwrite(producedPath, outputFullPath);
	}

	private static async Task PackMultiFileAsync(
		PackRequest request,
		ToolchainLauncher toolchainLauncher,
		string vpkToolPath,
		List<string> args,
		string inputDirectoryFullPath,
		CancellationToken cancellationToken)
	{
		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);
		var outputExtension = Path.GetExtension(outputFullPath);
		var baseName = Path.GetFileNameWithoutExtension(outputFullPath);
		if (!baseName.EndsWith(MultiFileDirectorySuffix, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Multi-file output path must end with \"{MultiFileDirectorySuffix}{outputExtension}\" (example: \"pak01{MultiFileDirectorySuffix}{outputExtension}\").");
		}

		var prefix = baseName[..^MultiFileDirectorySuffix.Length];

		var inputParentDirectory = Path.GetDirectoryName(inputDirectoryFullPath) ?? ".";
		var fileListPath = Path.Combine(inputParentDirectory, $"stunstick-vpk-filelist-{Guid.NewGuid():N}.txt");

		try
		{
			request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "VPK", Message: "Preparing file list..."));

			await WriteVpkFileListAsync(inputDirectoryFullPath, fileListPath, cancellationToken).ConfigureAwait(false);

			args.Add("-M");
			args.Add("a");
			args.Add(prefix);
			args.Add($"@..{Path.DirectorySeparatorChar}{Path.GetFileName(fileListPath)}");

			var stdout = CreateLineProgress(request.Progress, prefix: "OUT");
			var stderr = CreateLineProgress(request.Progress, prefix: "ERR");

			request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "VPK", Message: "Running VPK tool..."));

			var exitCode = await toolchainLauncher.LaunchExternalToolAsync(
				toolPath: vpkToolPath,
				toolArguments: args,
				wineOptions: request.WineOptions ?? new WineOptions(),
				steamRootOverride: request.SteamRoot,
				waitForExit: true,
				cancellationToken,
				workingDirectory: inputDirectoryFullPath,
				standardOutput: stdout,
				standardError: stderr).ConfigureAwait(false);

			if (exitCode != 0)
			{
				throw new InvalidDataException($"VPK tool failed with exit code {exitCode}.");
			}

			var outputDirectory = Path.GetDirectoryName(outputFullPath);
			if (!string.IsNullOrWhiteSpace(outputDirectory))
			{
				Directory.CreateDirectory(outputDirectory);
			}

			var directoryFileName = Path.GetFileName(outputFullPath);
			var movedAny = false;
			foreach (var sourcePath in Directory.EnumerateFiles(inputDirectoryFullPath, $"{prefix}_???.vpk", SearchOption.TopDirectoryOnly))
			{
				var fileName = Path.GetFileName(sourcePath);
				var targetPath = string.Equals(fileName, directoryFileName, StringComparison.OrdinalIgnoreCase)
					? outputFullPath
					: Path.Combine(outputDirectory ?? ".", fileName);
				MoveFileOverwrite(sourcePath, targetPath);
				movedAny = true;
			}

			if (!movedAny)
			{
				throw new FileNotFoundException("VPK tool did not produce any multi-file outputs.");
			}

			if (!File.Exists(outputFullPath))
			{
				throw new FileNotFoundException("VPK tool did not produce the expected multi-file directory output.", outputFullPath);
			}
		}
		finally
		{
			try
			{
				if (File.Exists(fileListPath))
				{
					File.Delete(fileListPath);
				}
			}
			catch
			{
			}
		}
	}

	private static async Task WriteVpkFileListAsync(string inputDirectoryFullPath, string outputFilePath, CancellationToken cancellationToken)
	{
		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var entries = Directory.EnumerateFiles(inputDirectoryFullPath, "*", SearchOption.AllDirectories)
			.Select(path => Path.GetFullPath(path))
			.OrderBy(path => path, comparer)
			.Select(path => Path.GetRelativePath(inputDirectoryFullPath, path))
			.ToArray();

		await File.WriteAllLinesAsync(outputFilePath, entries, cancellationToken).ConfigureAwait(false);
	}

	private static IProgress<string>? CreateLineProgress(IProgress<StunstickProgress>? progress, string prefix)
	{
		if (progress is null)
		{
			return null;
		}

		var normalized = string.IsNullOrWhiteSpace(prefix) ? "OUT" : prefix.Trim();
		return new Progress<string>(line =>
		{
			var text = string.IsNullOrWhiteSpace(line) ? string.Empty : line.TrimEnd();
			progress.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "VPK", Message: $"[{normalized}] {text}"));
		});
	}

	private static void MoveFileOverwrite(string sourcePath, string targetPath)
	{
		var sourceFullPath = Path.GetFullPath(sourcePath);
		var targetFullPath = Path.GetFullPath(targetPath);

		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		if (string.Equals(sourceFullPath, targetFullPath, comparison))
		{
			return;
		}

		if (File.Exists(targetFullPath))
		{
			File.Delete(targetFullPath);
		}

		File.Move(sourceFullPath, targetFullPath);
	}

	private static string? ResolveVpkToolPath(PackRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.VpkToolPath))
		{
			return request.VpkToolPath;
		}

		string? gameDirectory = request.GameDirectory;
		if (string.IsNullOrWhiteSpace(gameDirectory) && request.SteamAppId is not null)
		{
			var preset = ToolchainDiscovery.FindSteamPreset(request.SteamAppId.Value, request.SteamRoot);
			if (preset is not null)
			{
				if (!string.IsNullOrWhiteSpace(preset.VpkToolPath))
				{
					return preset.VpkToolPath;
				}

				gameDirectory = preset.GameDirectory;
			}
		}

		if (!string.IsNullOrWhiteSpace(gameDirectory))
		{
			var vpk = ToolchainDiscovery.FindVpkPath(gameDirectory);
			if (!string.IsNullOrWhiteSpace(vpk))
			{
				return vpk;
			}
		}

		return ToolchainDiscovery.FindVpkPathWithSteamHints(request.SteamRoot);
	}

	private static bool LooksLikeFilePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		return path.Contains(Path.DirectorySeparatorChar) ||
			   path.Contains(Path.AltDirectorySeparatorChar) ||
			   Path.IsPathRooted(path);
	}
}
