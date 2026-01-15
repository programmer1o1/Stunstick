using Stunstick.App;
using Stunstick.App.Inspect;
using Stunstick.App.Toolchain;
using Stunstick.App.Workshop;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

static int PrintUsage(TextWriter writer)
{
	writer.WriteLine("Stunstick");
	writer.WriteLine("Fork by programmer1o1");
	writer.WriteLine();
	writer.WriteLine("Usage:");
	writer.WriteLine("  stunstick decompile --mdl <pathOrFolder> --out <dir> [--recursive] [--mdl-version <n>] [--anims|--no-anims] [--anims-same-folder] [--declare-sequence-qci] [--vta] [--strip-smd-material-path] [--mixed-case-qc|--lowercase-qc] [--qc-group-qci] [--qc-skinfamily-multi-line] [--qc-texturegroup-all-materials] [--no-qc-definebones] [--non-valve-uv|--valve-uv] [--flat-output] [--prefix-mesh-with-model-name] [--stricter-format|--no-stricter-format] [--no-physics-indent] [--debug-info] [--no-qc] [--no-ref-smds] [--no-lods] [--no-physics] [--no-texture-bmps] [--no-vrd] [--log]");
	writer.WriteLine("  stunstick unpack --in <path> --out <dir> [--verify] [--verify-md5] [--log]");
	writer.WriteLine("  stunstick pack --in <dir> --out <path> [--multi-file] [--split-mb <mb>] [--preload-bytes <n>] [--vpk-version <1|2>] [--with-md5] [--vpk-tool <path>] [--gmad <path>] [--game <dir>] [--steam-appid <id>] [--steam-root <dir>] [--wine-prefix <dir>] [--wine <cmd>] [--opts <text>] [--gma-tags <csv>] [--ignore-whitelist-warnings] [--log]");
	writer.WriteLine("  stunstick pack --batch --in <parentDir> --out <dir> [--type <vpk|fpx|gma>] [--multi-file] [--split-mb <mb>] [--preload-bytes <n>] [--vpk-version <1|2>] [--with-md5] [--vpk-tool <path>] [--gmad <path>] [--game <dir>] [--steam-appid <id>] [--steam-root <dir>] [--wine-prefix <dir>] [--wine <cmd>] [--opts <text>] [--gma-tags <csv>] [--ignore-whitelist-warnings] [--log]");
	writer.WriteLine("  stunstick inspect <path> [--no-hash] [--mdl-version <n>]");
	writer.WriteLine("  stunstick compile --qc <pathOrFolder> [--recursive] [--studiomdl <path>] [--game <dir>] [--steam-appid <id>] [--steam-root <dir>] [--wine-prefix <dir>] [--wine <cmd>] [--no-nop4] [--no-verbose] [--definebones] [--definebones-write-qci] [--definebones-qci <name>] [--definebones-overwrite-qci] [--definebones-modify-qc] [--opts <text>] [--copy-to <dir>] [--log]");
	writer.WriteLine("  stunstick view --mdl <path> [--hlmv <path>] [--game <dir>] [--steam-appid <id>] [--steam-root <dir>] [--wine-prefix <dir>] [--wine <cmd>] [--replacement]");
	writer.WriteLine("  stunstick download --id <idOrLink> --out <dir> [--appid <id>] [--steam-root <dir>] [--with-title] [--no-id] [--fetch-details] [--append-updated] [--keep-spaces] [--no-convert] [--overwrite] [--steamworks] [--steampipe <path>] [--steamcmd-fallback] [--steamcmd <path>] [--steamcmd-user <name>] [--steamcmd-install <dir>]");
	writer.WriteLine("  stunstick publish --appid <id> --content <path> --preview <file> --title <text> --description <text> --change-note <text> [--published-id <id>] [--visibility <public|friends|private|unlisted>] [--tags <csv>] [--content-type <text>] [--content-tags <csv>] [--pack-vpk] [--steamworks] [--steampipe <path>] [--steamcmd-user <name>] [--vdf <path>] [--steamcmd <path>]");
	writer.WriteLine("  stunstick delete --appid <id> --published-id <id> [--steampipe <path>]");
	writer.WriteLine("  stunstick workshop list --appid <id> [--page <n>] [--steampipe <path>]");
	writer.WriteLine("  stunstick workshop quota --appid <id> [--steampipe <path>]");
	writer.WriteLine("  stunstick steam list [--steam-root <dir>]");
	writer.WriteLine();
	writer.WriteLine("Notes:");
	writer.WriteLine("  - On macOS/Linux, .exe tools run via Wine by default; native binaries are used when available.");
	writer.WriteLine("  - Packing to .gma uses the built-in GMA writer (requires addon.json). Pass --gmad or --opts to run external GMAD instead.");
	writer.WriteLine("  - Unpack supports .vpk/.fpx/.gma/.apk/.hfs. --verify-md5 applies to VPK v2 MD5 sections only.");
	writer.WriteLine("  - Decompile writes QC + SMDs when matching .vvd/.vtx files exist next to the .mdl (or are embedded in v53 MDLs).");
	writer.WriteLine("  - Workshop download uses local cache when available; on cache miss it will try web download (Steam file_url) and optionally Steamworks (--steamworks) or SteamCMD (--steamcmd-fallback).");
	return 2;
}

static Task<string?> PromptSteamCmdAsync(SteamCmdPrompt prompt, CancellationToken cancellationToken)
{
	if (cancellationToken.IsCancellationRequested)
	{
		return Task.FromResult<string?>(null);
	}

	var message = string.IsNullOrWhiteSpace(prompt?.Message) ? "SteamCMD input:" : prompt!.Message.TrimEnd();
	Console.Write($"{message} ");

	if (prompt?.Kind == SteamCmdPromptKind.Password)
	{
		var password = ReadSecretLine(maskInput: true);
		return Task.FromResult<string?>(password);
	}

	var line = Console.ReadLine();
	return Task.FromResult(line);
}

static string ReadSecretLine(bool maskInput)
{
	var sb = new StringBuilder();

	while (true)
	{
		var key = Console.ReadKey(intercept: true);
		if (key.Key == ConsoleKey.Enter)
		{
			Console.WriteLine();
			break;
		}

		if (key.Key == ConsoleKey.Backspace)
		{
			if (sb.Length > 0)
			{
				sb.Length--;
				if (maskInput)
				{
					Console.Write("\b \b");
				}
			}

			continue;
		}

		if (char.IsControl(key.KeyChar))
		{
			continue;
		}

		sb.Append(key.KeyChar);
		if (maskInput)
		{
			Console.Write('*');
		}
	}

	return sb.ToString();
}

static string? GetOptionValue(string[] args, string optionName)
{
	for (var index = 0; index < args.Length; index++)
	{
		if (string.Equals(args[index], optionName, StringComparison.Ordinal))
		{
			var values = new List<string>();
			for (var valueIndex = index + 1; valueIndex < args.Length; valueIndex++)
			{
				if (args[valueIndex].StartsWith("--", StringComparison.Ordinal))
				{
					break;
				}

				values.Add(args[valueIndex]);
			}

			return values.Count == 0 ? null : string.Join(" ", values).Trim();
		}
	}

	return null;
}

static string? GetPositionalValue(string[] args, int startIndex)
{
	if (startIndex >= args.Length)
	{
		return null;
	}

	var values = new List<string>();
	for (var index = startIndex; index < args.Length; index++)
	{
		if (args[index].StartsWith("--", StringComparison.Ordinal))
		{
			break;
		}

		values.Add(args[index]);
	}

	return values.Count == 0 ? null : string.Join(" ", values).Trim();
}

static bool HasFlag(string[] args, string flagName)
{
	return args.Any(arg => string.Equals(arg, flagName, StringComparison.Ordinal));
}

static IReadOnlyList<string> ParseCsvOrLines(string? input)
{
	if (string.IsNullOrWhiteSpace(input))
	{
		return Array.Empty<string>();
	}

	var parts = input
		.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		.Where(p => !string.IsNullOrWhiteSpace(p))
		.Select(p => p.Trim())
		.Distinct(StringComparer.OrdinalIgnoreCase)
		.ToArray();

	return parts.Length == 0 ? Array.Empty<string>() : parts;
}

static int PrintError(string message)
{
	Console.Error.WriteLine(message);
	Console.Error.WriteLine();
	return 1;
}

static PackBatchOutputType ParsePackBatchOutputType(string? type)
{
	var text = (type ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
	return text switch
	{
		"fpx" => PackBatchOutputType.Fpx,
		"gma" => PackBatchOutputType.Gma,
		"" or "vpk" => PackBatchOutputType.Vpk,
		_ => throw new InvalidDataException("Invalid --type value (expected vpk, fpx, or gma).")
	};
}

static string GetPackBatchOutputPath(string inputDirectory, string outputDirectory, PackBatchOutputType type, bool multiFile)
{
	var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory));
	if (string.IsNullOrWhiteSpace(name))
	{
		name = "package";
	}

	if (type == PackBatchOutputType.Gma)
	{
		return Path.Combine(outputDirectory, $"{name}.gma");
	}

	var extension = type == PackBatchOutputType.Fpx ? ".fpx" : ".vpk";
	if (!multiFile)
	{
		return Path.Combine(outputDirectory, $"{name}{extension}");
	}

	var dirSuffix = type == PackBatchOutputType.Fpx ? "_fdr" : "_dir";
	return Path.Combine(outputDirectory, $"{name}{dirSuffix}{extension}");
}

static string? ResolveGameDirectory(string? gameDir, uint? steamAppId, string? steamRoot)
{
	if (!string.IsNullOrWhiteSpace(gameDir))
	{
		return gameDir;
	}

	if (steamAppId is null)
	{
		return null;
	}

	var preset = ToolchainDiscovery.FindSteamPreset(steamAppId.Value, string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot);
	if (preset is null)
	{
		return null;
	}

	return ToolchainDiscovery.FindPreferredGameDirectory(preset.GameDirectory, preset.AppId) ?? preset.GameDirectory;
}

static void EnsureGmaAddonJson(string inputDirectory, string? tagsText)
{
	var addonJsonPath = Path.Combine(inputDirectory, "addon.json");
	if (File.Exists(addonJsonPath))
	{
		return;
	}

	var title = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory));
	if (string.IsNullOrWhiteSpace(title))
	{
		title = "addon";
	}

	var tags = ParseCsvOrLines(tagsText);
	var ignore = Array.Empty<string>();

	var json = JsonSerializer.Serialize(
		new
		{
			title,
			description = "",
			author = "",
			version = 1,
			tags,
			ignore
		},
		new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() });

	File.WriteAllText(addonJsonPath, json);
	Console.WriteLine($"Pack: wrote addon.json: {addonJsonPath}");
}

static string? TryReadQcModelNamePath(string qcPathFileName)
{
	try
	{
		var text = File.ReadAllText(qcPathFileName);

		var match = System.Text.RegularExpressions.Regex.Match(
			text,
			pattern: @"(?im)^\s*\$modelname\s+(?:""([^""]+)""|(\S+))",
			options: System.Text.RegularExpressions.RegexOptions.CultureInvariant);

		if (!match.Success)
		{
			return null;
		}

		return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
	}
	catch
	{
		return null;
	}
}

static string? TryGetCompiledModelPath(string qcPathFileName, string? gameDirectory)
{
	if (string.IsNullOrWhiteSpace(qcPathFileName) || !File.Exists(qcPathFileName))
	{
		return null;
	}

	var modelNamePath = TryReadQcModelNamePath(qcPathFileName);
	if (string.IsNullOrWhiteSpace(modelNamePath))
	{
		return null;
	}

	var normalized = modelNamePath.Replace('\\', '/').Trim();

	if (Path.IsPathRooted(normalized))
	{
		var rootedCandidates = new List<string> { normalized };
		if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
		{
			rootedCandidates.Add(normalized + ".mdl");
		}

		return rootedCandidates.FirstOrDefault(File.Exists);
	}

	var rootsToCheck = new List<string>();
	if (!string.IsNullOrWhiteSpace(gameDirectory))
	{
		rootsToCheck.Add(gameDirectory);
	}

	try
	{
		var qcDir = Path.GetDirectoryName(Path.GetFullPath(qcPathFileName));
		if (!string.IsNullOrWhiteSpace(qcDir))
		{
			rootsToCheck.Add(qcDir);
		}
	}
	catch
	{
	}

	var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
	rootsToCheck = rootsToCheck.Distinct(comparer).ToList();
	if (rootsToCheck.Count == 0)
	{
		return null;
	}

	var relative = normalized.Replace('/', Path.DirectorySeparatorChar);
	var relativeCandidates = new List<string> { relative };
	if (string.IsNullOrWhiteSpace(Path.GetExtension(relative)))
	{
		relativeCandidates.Add(relative + ".mdl");
	}

	foreach (var root in rootsToCheck)
	{
		foreach (var relativeCandidate in relativeCandidates)
		{
			var direct = Path.Combine(root, relativeCandidate);
			if (File.Exists(direct))
			{
				return direct;
			}

			var inModels = Path.Combine(root, "models", relativeCandidate);
			if (File.Exists(inModels))
			{
				return inModels;
			}
		}
	}

	return null;
}

static void CopyCompileOutputs(string qcPathFileName, string gameDirectory, string outputRoot, CancellationToken cancellationToken)
{
	if (string.IsNullOrWhiteSpace(outputRoot))
	{
		throw new ArgumentException("Copy output folder is required.", nameof(outputRoot));
	}

	var modelNamePath = TryReadQcModelNamePath(qcPathFileName);
	if (string.IsNullOrWhiteSpace(modelNamePath))
	{
		throw new InvalidDataException($"No $modelname found in: \"{qcPathFileName}\"");
	}

	var compiledMdlPath = TryGetCompiledModelPath(qcPathFileName, gameDirectory);
	if (string.IsNullOrWhiteSpace(compiledMdlPath) || !File.Exists(compiledMdlPath))
	{
		throw new FileNotFoundException("Compiled MDL not found (ensure QC has $modelname and a valid Game Dir).", compiledMdlPath);
	}

	var outputModelsRoot = GetCompileCopyModelsRoot(outputRoot);
	var modelsSubpath = GetModelsSubpath(modelNamePath);
	var targetFolder = string.IsNullOrWhiteSpace(modelsSubpath) ? outputModelsRoot : Path.Combine(outputModelsRoot, modelsSubpath);

	Directory.CreateDirectory(targetFolder);

	var sourceFolder = Path.GetDirectoryName(compiledMdlPath);
	if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
	{
		throw new DirectoryNotFoundException($"Source folder not found: \"{sourceFolder}\"");
	}

	var baseName = Path.GetFileNameWithoutExtension(compiledMdlPath);
	if (string.IsNullOrWhiteSpace(baseName))
	{
		throw new InvalidDataException($"Could not determine compiled model base name: \"{compiledMdlPath}\"");
	}

	var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".ani",
		".mdl",
		".phy",
		".vtx",
		".vvd"
	};

	var copied = 0;
	foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, $"{baseName}.*", SearchOption.TopDirectoryOnly))
	{
		cancellationToken.ThrowIfCancellationRequested();

		var ext = Path.GetExtension(sourceFile);
		if (!allowedExtensions.Contains(ext))
		{
			continue;
		}

		var destFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
		File.Copy(sourceFile, destFile, overwrite: true);
		copied++;
	}

	Console.WriteLine($"Copied {copied} file(s) to: {targetFolder}");
}

static string GetCompileCopyModelsRoot(string outputRoot)
{
	var trimmed = Path.TrimEndingDirectorySeparator(outputRoot);
	var lastSegment = Path.GetFileName(trimmed);
	if (string.Equals(lastSegment, "models", StringComparison.OrdinalIgnoreCase))
	{
		return outputRoot;
	}

	return Path.Combine(outputRoot, "models");
}

static string GetModelsSubpath(string modelNamePath)
{
	if (string.IsNullOrWhiteSpace(modelNamePath))
	{
		return string.Empty;
	}

	var normalized = modelNamePath.Replace('\\', '/').Trim();
	var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
	if (parts.Length <= 1)
	{
		return string.Empty;
	}

	var directoryCount = parts.Length - 1;
	var modelsIndex = -1;
	for (var i = 0; i < directoryCount; i++)
	{
		if (string.Equals(parts[i], "models", StringComparison.OrdinalIgnoreCase))
		{
			modelsIndex = i;
		}
	}

	var start = modelsIndex >= 0 ? modelsIndex + 1 : 0;
	if (start >= directoryCount)
	{
		return string.Empty;
	}

	return Path.Combine(parts[start..directoryCount]);
}

if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
{
	return PrintUsage(Console.Out);
}

if (HasFlag(args, "--version"))
{
	Console.WriteLine("Stunstick.Cli 0.1 (scaffold)");
	return 0;
}

var app = new StunstickApplication(new ToolchainLauncher(new SystemProcessLauncher()));
var cancellationToken = CancellationToken.None;

	try
	{
		var command = args[0];

		if (string.Equals(command, "steam", StringComparison.Ordinal))
		{
			var subcommand = args.Length >= 2 ? args[1] : string.Empty;
			if (!string.Equals(subcommand, "list", StringComparison.Ordinal))
			{
				return PrintUsage(Console.Out);
			}

			var steamRoot = GetOptionValue(args, "--steam-root");
			var presets = ToolchainDiscovery.DiscoverSteamPresets(steamRoot);
			if (presets.Count == 0)
			{
				Console.WriteLine("No Steam installs found (or no apps detected).");
				return 0;
			}

			foreach (var preset in presets)
			{
				Console.WriteLine($"{preset.AppId}: {preset.Name}");
				Console.WriteLine($"  GameDir: {preset.GameDirectory}");
				if (!string.IsNullOrWhiteSpace(preset.StudioMdlPath))
				{
					Console.WriteLine($"  StudioMDL: {preset.StudioMdlPath}");
				}
				if (!string.IsNullOrWhiteSpace(preset.HlmvPath))
				{
					Console.WriteLine($"  HLMV: {preset.HlmvPath}");
				}
			}

			return 0;
		}

		if (string.Equals(command, "workshop", StringComparison.Ordinal))
		{
			var subcommand = args.Length >= 2 ? args[1] : string.Empty;
			var appIdText = GetOptionValue(args, "--appid");
			var steamPipePath = GetOptionValue(args, "--steampipe");

			if (string.IsNullOrWhiteSpace(subcommand) || string.IsNullOrWhiteSpace(appIdText))
			{
				return PrintUsage(Console.Out);
			}

			if (!uint.TryParse(appIdText, out var appId) || appId == 0)
			{
				return PrintError("Invalid --appid value.");
			}

			if (string.Equals(subcommand, "list", StringComparison.Ordinal))
			{
				uint page = 1;
				var pageText = GetOptionValue(args, "--page");
				if (!string.IsNullOrWhiteSpace(pageText) && (!uint.TryParse(pageText, out page) || page == 0))
				{
					return PrintError("Invalid --page value (expected >= 1).");
				}

				var result = await app.ListWorkshopPublishedItemsAsync(
					new WorkshopListRequest(
						AppId: appId,
						Page: page,
						Output: new Progress<string>(Console.WriteLine),
						SteamPipePath: steamPipePath),
					cancellationToken);

				Console.WriteLine($"AppId: {result.AppId}");
				Console.WriteLine($"Page: {result.Page}  Returned: {result.Returned}  Total: {result.TotalMatching}");
				foreach (var item in result.Items)
				{
					var title = string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title.Trim();
					var updated = item.UpdatedAtUtc.HasValue ? item.UpdatedAtUtc.Value.ToString("u") : string.Empty;
					var updatedText = string.IsNullOrWhiteSpace(updated) ? string.Empty : $"  Updated: {updated}";
					Console.WriteLine($"{item.PublishedFileId}: {title}{updatedText}");
				}

				return 0;
			}

			if (string.Equals(subcommand, "quota", StringComparison.Ordinal))
			{
				var result = await app.GetWorkshopQuotaAsync(
					new WorkshopQuotaRequest(
						AppId: appId,
						Output: new Progress<string>(Console.WriteLine),
						SteamPipePath: steamPipePath),
					cancellationToken);

				Console.WriteLine($"AppId: {result.AppId}");
				Console.WriteLine($"Used: {result.UsedBytes} bytes");
				Console.WriteLine($"Total: {result.TotalBytes} bytes");
				Console.WriteLine($"Available: {result.AvailableBytes} bytes");
				return 0;
			}

			return PrintUsage(Console.Out);
		}

			if (string.Equals(command, "download", StringComparison.Ordinal))
			{
				var idOrLink = GetOptionValue(args, "--id");
				var outputDirectory = GetOptionValue(args, "--out");
			if (string.IsNullOrWhiteSpace(idOrLink) || string.IsNullOrWhiteSpace(outputDirectory))
			{
				return PrintUsage(Console.Out);
			}

			uint appId = 4000;
			var appIdText = GetOptionValue(args, "--appid");
			if (!string.IsNullOrWhiteSpace(appIdText))
			{
				if (!uint.TryParse(appIdText, out appId) || appId == 0)
				{
					return PrintError("Invalid --appid value.");
				}
			}

				var steamRoot = GetOptionValue(args, "--steam-root");
				var steamworksFallback = HasFlag(args, "--steamworks");
				var steamPipePath = GetOptionValue(args, "--steampipe");
				var steamCmdFallback = HasFlag(args, "--steamcmd-fallback");
				var steamCmdPath = GetOptionValue(args, "--steamcmd");
				var steamCmdUser = GetOptionValue(args, "--steamcmd-user");
				var steamCmdInstall = GetOptionValue(args, "--steamcmd-install");

				var fetchDetails = HasFlag(args, "--fetch-details") || HasFlag(args, "--with-title") || HasFlag(args, "--append-updated");
				var naming = new WorkshopDownloadNamingOptions(
					IncludeTitle: HasFlag(args, "--with-title"),
					IncludeId: !HasFlag(args, "--no-id"),
				AppendUpdatedTimestamp: HasFlag(args, "--append-updated"),
				ReplaceSpacesWithUnderscores: !HasFlag(args, "--keep-spaces"));

				var result = await app.DownloadWorkshopItemAsync(
					new WorkshopDownloadRequest(
						IdOrLink: idOrLink,
						OutputDirectory: outputDirectory,
						AppId: appId,
						SteamRoot: steamRoot,
						ConvertToExpectedFileOrFolder: !HasFlag(args, "--no-convert"),
						FetchDetails: fetchDetails,
						OverwriteExisting: HasFlag(args, "--overwrite"),
						NamingOptions: naming,
						Output: new Progress<string>(Console.WriteLine),
						UseSteamworksWhenNotCached: steamworksFallback,
						UseSteamCmdWhenNotCached: steamCmdFallback,
						SteamCmdPath: steamCmdPath,
						SteamCmdInstallDirectory: steamCmdInstall,
						SteamCmdUsername: steamCmdUser,
						SteamCmdPromptAsync: PromptSteamCmdAsync,
						SteamPipePath: steamPipePath),
					cancellationToken);

			Console.WriteLine($"Downloaded to: {result.OutputPath}");
			if (!string.IsNullOrWhiteSpace(result.Details?.Title))
			{
				Console.WriteLine($"Title: {result.Details!.Title}");
			}
			return 0;
		}

		if (string.Equals(command, "publish", StringComparison.Ordinal))
		{
			var appIdText = GetOptionValue(args, "--appid");
			var contentFolder = GetOptionValue(args, "--content");
			var previewFile = GetOptionValue(args, "--preview");
			var title = GetOptionValue(args, "--title");
			var description = GetOptionValue(args, "--description");
			var changeNote = GetOptionValue(args, "--change-note");
			var steamCmdUser = GetOptionValue(args, "--steamcmd-user");
			var useSteamworks = HasFlag(args, "--steamworks");
			var packVpk = HasFlag(args, "--pack-vpk");
			var steamPipePath = GetOptionValue(args, "--steampipe");

			if (string.IsNullOrWhiteSpace(appIdText) ||
				string.IsNullOrWhiteSpace(contentFolder) ||
				string.IsNullOrWhiteSpace(previewFile) ||
				string.IsNullOrWhiteSpace(title) ||
				string.IsNullOrWhiteSpace(description) ||
				string.IsNullOrWhiteSpace(changeNote) ||
				(!useSteamworks && string.IsNullOrWhiteSpace(steamCmdUser)))
			{
				return PrintUsage(Console.Out);
			}

			if (!uint.TryParse(appIdText, out var appId) || appId == 0)
			{
				return PrintError("Invalid --appid value.");
			}

			ulong publishedFileId = 0;
			var publishedIdText = GetOptionValue(args, "--published-id");
			if (!string.IsNullOrWhiteSpace(publishedIdText))
			{
				if (!ulong.TryParse(publishedIdText, out publishedFileId))
				{
					return PrintError("Invalid --published-id value.");
				}
			}

			var visibilityText = GetOptionValue(args, "--visibility") ?? "public";
			var visibility = visibilityText.ToLowerInvariant() switch
			{
				"public" => WorkshopPublishVisibility.Public,
				"friends" or "friendsonly" => WorkshopPublishVisibility.FriendsOnly,
				"private" => WorkshopPublishVisibility.Private,
				"unlisted" => WorkshopPublishVisibility.Unlisted,
				_ => (WorkshopPublishVisibility?)null
			};

			if (visibility is null)
			{
				return PrintError("Invalid --visibility value (expected public|friends|private|unlisted).");
			}

			IReadOnlyList<string>? tags = null;
			var tagsText = GetOptionValue(args, "--tags");
			if (!string.IsNullOrWhiteSpace(tagsText))
			{
				tags = tagsText
					.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(tag => !string.IsNullOrWhiteSpace(tag))
					.ToArray();
			}

			var contentType = GetOptionValue(args, "--content-type");
			IReadOnlyList<string>? contentTags = null;
			var contentTagsText = GetOptionValue(args, "--content-tags");
			if (!string.IsNullOrWhiteSpace(contentTagsText))
			{
				contentTags = contentTagsText
					.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(tag => !string.IsNullOrWhiteSpace(tag))
					.ToArray();
			}

			if (appId == 4000 && (tags is null || tags.Count == 0) && (!string.IsNullOrWhiteSpace(contentType) || (contentTags?.Count ?? 0) > 0))
			{
				var derived = new List<string>(capacity: 3);
				if (!string.IsNullOrWhiteSpace(contentType))
				{
					derived.Add(contentType);
				}

				if (contentTags is not null)
				{
					derived.AddRange(contentTags);
				}

				tags = derived
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray();
			}

			var vdfPath = GetOptionValue(args, "--vdf");
			var steamCmdPath = GetOptionValue(args, "--steamcmd");

			var result = await app.PublishWorkshopItemAsync(
				new WorkshopPublishRequest(
					AppId: appId,
					ContentFolder: contentFolder,
					PreviewFile: previewFile,
					Title: title,
					Description: description,
					ChangeNote: changeNote,
					PublishedFileId: publishedFileId,
					Visibility: visibility.Value,
					VdfPath: vdfPath,
					SteamCmdPath: steamCmdPath,
					SteamCmdUsername: steamCmdUser,
					Tags: tags,
					ContentType: contentType,
					ContentTags: contentTags,
					Output: new Progress<string>(Console.WriteLine),
					SteamCmdPromptAsync: PromptSteamCmdAsync,
					UseSteamworks: useSteamworks,
					SteamPipePath: steamPipePath,
					PackToVpkBeforeUpload: packVpk),
				cancellationToken);

			Console.WriteLine($"PublishedFileId: {result.PublishedFileId}");
			Console.WriteLine($"VDF: {(!string.IsNullOrWhiteSpace(result.VdfPath) ? result.VdfPath : "(steamworks)")}");
			return 0;
		}

		if (string.Equals(command, "delete", StringComparison.Ordinal))
		{
			var appIdText = GetOptionValue(args, "--appid");
			var publishedIdText = GetOptionValue(args, "--published-id");
			var steamPipePath = GetOptionValue(args, "--steampipe");

			if (string.IsNullOrWhiteSpace(appIdText) || string.IsNullOrWhiteSpace(publishedIdText))
			{
				return PrintUsage(Console.Out);
			}

			if (!uint.TryParse(appIdText, out var appId) || appId == 0)
			{
				return PrintError("Invalid --appid value.");
			}

			if (!ulong.TryParse(publishedIdText, out var publishedFileId) || publishedFileId == 0)
			{
				return PrintError("Invalid --published-id value.");
			}

			await app.DeleteWorkshopItemAsync(
				new WorkshopDeleteRequest(
					AppId: appId,
					PublishedFileId: publishedFileId,
					SteamPipePath: steamPipePath,
					Output: new Progress<string>(Console.WriteLine)),
				cancellationToken);

			Console.WriteLine($"Deleted PublishedFileId: {publishedFileId}");
			return 0;
		}

			if (string.Equals(command, "decompile", StringComparison.Ordinal))
				{
					var mdlPath = GetOptionValue(args, "--mdl");
					var outputDirectory = GetOptionValue(args, "--out");
					if (string.IsNullOrWhiteSpace(mdlPath) || string.IsNullOrWhiteSpace(outputDirectory))
			{
				return PrintUsage(Console.Out);
			}

					var writeLogFile = HasFlag(args, "--log");
					var writeDeclareSequenceQci = HasFlag(args, "--declare-sequence-qci");
					var disableAnims = HasFlag(args, "--no-anims");
					var forceLowerCaseQc = HasFlag(args, "--lowercase-qc") || HasFlag(args, "--no-mixed-case-qc");
					var forceValveUv = HasFlag(args, "--valve-uv");

					int? mdlVersionOverride = null;
					var mdlVersionText = GetOptionValue(args, "--mdl-version");
					if (!string.IsNullOrWhiteSpace(mdlVersionText))
					{
						if (!int.TryParse(mdlVersionText.Trim(), out var parsed))
						{
							return PrintError("Invalid --mdl-version value.");
						}

						mdlVersionOverride = parsed;
					}

						var defaults = new Stunstick.App.Decompile.DecompileOptions();
						var stricterFormat = defaults.StricterFormat;
						if (HasFlag(args, "--stricter-format"))
						{
							stricterFormat = true;
						}
						else if (HasFlag(args, "--no-stricter-format"))
						{
							stricterFormat = false;
						}
						var indentPhysics = defaults.IndentPhysicsTriangles && !HasFlag(args, "--no-physics-indent");

						var options = new Stunstick.App.Decompile.DecompileOptions(
							WriteQcFile: !HasFlag(args, "--no-qc"),
						QcGroupIntoQciFiles: defaults.QcGroupIntoQciFiles || HasFlag(args, "--qc-group-qci"),
					QcSkinFamilyOnSingleLine: !HasFlag(args, "--qc-skinfamily-multi-line"),
					QcOnlyChangedMaterialsInTextureGroupLines: !HasFlag(args, "--qc-texturegroup-all-materials"),
						QcIncludeDefineBoneLines: !HasFlag(args, "--no-qc-definebones"),
						WriteReferenceMeshSmdFiles: !HasFlag(args, "--no-ref-smds"),
						WriteBoneAnimationSmdFiles: disableAnims ? false : (HasFlag(args, "--anims") || defaults.WriteBoneAnimationSmdFiles),
					BoneAnimationPlaceInSubfolder: HasFlag(args, "--anims-same-folder") ? false : defaults.BoneAnimationPlaceInSubfolder,
					WriteVertexAnimationVtaFile: HasFlag(args, "--vta") || defaults.WriteVertexAnimationVtaFile,
						WritePhysicsMeshSmdFile: !HasFlag(args, "--no-physics"),
							WriteTextureBmpFiles: !HasFlag(args, "--no-texture-bmps"),
							WriteProceduralBonesVrdFile: !HasFlag(args, "--no-vrd"),
							WriteDeclareSequenceQciFile: writeDeclareSequenceQci,
							WriteDebugInfoFiles: HasFlag(args, "--debug-info"),
							WriteLodMeshSmdFiles: !HasFlag(args, "--no-lods"),
							RemovePathFromSmdMaterialFileNames: HasFlag(args, "--strip-smd-material-path"),
						UseNonValveUvConversion: forceValveUv ? false : (HasFlag(args, "--non-valve-uv") || defaults.UseNonValveUvConversion),
						FolderForEachModel: !HasFlag(args, "--flat-output"),
						PrefixFileNamesWithModelName: HasFlag(args, "--prefix-mesh-with-model-name"),
						StricterFormat: stricterFormat,
						IndentPhysicsTriangles: indentPhysics,
						QcUseMixedCaseForKeywords: forceLowerCaseQc ? false : (HasFlag(args, "--mixed-case-qc") || defaults.QcUseMixedCaseForKeywords),
						VersionOverride: mdlVersionOverride);

				var recursive = HasFlag(args, "--recursive");
				StreamWriter? logWriter = null;
				try
				{
					if (writeLogFile)
					{
						Directory.CreateDirectory(outputDirectory);
						var logPathFileName = Path.Combine(outputDirectory, "decompile.log");
						var logStream = new FileStream(logPathFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
						logWriter = new StreamWriter(logStream) { AutoFlush = true };
						logWriter.WriteLine("Stunstick decompile log");
						logWriter.WriteLine($"Started: {DateTimeOffset.Now:O}");
						logWriter.WriteLine($"Input: {Path.GetFullPath(mdlPath)}");
						logWriter.WriteLine($"Output: {Path.GetFullPath(outputDirectory)}");
						logWriter.WriteLine();
						Console.WriteLine($"Writing log: {logPathFileName}");
					}

				if (Directory.Exists(mdlPath))
				{
					var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
					var mdlFiles = Directory.EnumerateFiles(mdlPath, "*.mdl", search).ToArray();
					if (mdlFiles.Length == 0)
					{
						return PrintError("No .mdl files found in folder.");
					}

					var failures = 0;
					foreach (var file in mdlFiles)
					{
						try
						{
							await app.DecompileAsync(new Stunstick.App.Decompile.DecompileRequest(file, outputDirectory, options), cancellationToken);
							Console.WriteLine($"Decompiled: {file}");
							logWriter?.WriteLine($"Decompiled: {file}");
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex)
						{
							failures++;
							var msg = $"Decompile failed: {file}: {ex.Message}";
							Console.Error.WriteLine(msg);
							logWriter?.WriteLine(msg);
						}
					}

					var succeeded = mdlFiles.Length - failures;
					var summary = failures == 0
						? $"Decompiled {succeeded} model(s) to: {outputDirectory}"
						: $"Decompile finished: {succeeded} succeeded, {failures} failed. Output: {outputDirectory}";
					Console.WriteLine(summary);
					logWriter?.WriteLine(summary);
					return failures == 0 ? 0 : 1;
				}

				if (!File.Exists(mdlPath))
				{
					return PrintError("MDL file/folder not found.");
				}

				try
				{
					await app.DecompileAsync(new Stunstick.App.Decompile.DecompileRequest(mdlPath, outputDirectory, options), cancellationToken);
				}
				catch (Exception ex)
				{
					logWriter?.WriteLine($"ERROR: {ex.Message}");
					Console.Error.WriteLine(ex.Message);
					return 1;
				}
				var modelName = Path.GetFileNameWithoutExtension(mdlPath);
				var outputPath = options.FolderForEachModel && !string.IsNullOrWhiteSpace(modelName)
					? Path.Combine(outputDirectory, modelName)
					: outputDirectory;
				Console.WriteLine($"Decompiled to: {outputPath}");
				logWriter?.WriteLine($"Decompiled to: {outputPath}");
				return 0;
				}
				finally
				{
					if (logWriter is not null)
					{
						logWriter.WriteLine();
						logWriter.WriteLine($"Ended: {DateTimeOffset.Now:O}");
						logWriter.Flush();
						logWriter.Dispose();
					}
				}
			}

	if (string.Equals(command, "unpack", StringComparison.Ordinal))
	{
		var packagePath = GetOptionValue(args, "--in");
		var outputDirectory = GetOptionValue(args, "--out");
		if (string.IsNullOrWhiteSpace(packagePath) || string.IsNullOrWhiteSpace(outputDirectory))
		{
			return PrintUsage(Console.Out);
		}

		var verify = HasFlag(args, "--verify");
		var verifyMd5 = HasFlag(args, "--verify-md5");
		var writeLogFile = HasFlag(args, "--log");
		await app.UnpackAsync(new Stunstick.App.Unpack.UnpackRequest(packagePath, outputDirectory, VerifyCrc32: verify, VerifyMd5: verifyMd5, WriteLogFile: writeLogFile), cancellationToken);
		Console.WriteLine($"Unpacked to: {outputDirectory}");
		return 0;
	}

	if (string.Equals(command, "pack", StringComparison.Ordinal))
	{
		var batch = HasFlag(args, "--batch");
		var inputDirectory = GetOptionValue(args, "--in");
		var outputPath = GetOptionValue(args, "--out");
		if (string.IsNullOrWhiteSpace(inputDirectory) || string.IsNullOrWhiteSpace(outputPath))
		{
			return PrintUsage(Console.Out);
			}
	
			var multiFile = HasFlag(args, "--multi-file");

		long? maxArchiveSizeBytes = null;
		var splitMb = GetOptionValue(args, "--split-mb");
		if (!string.IsNullOrWhiteSpace(splitMb))
		{
			if (!long.TryParse(splitMb, out var mb) || mb <= 0)
			{
				return PrintError("Invalid --split-mb value.");
			}

			maxArchiveSizeBytes = mb * 1024L * 1024L;
		}

		var vpkVersionText = GetOptionValue(args, "--vpk-version");
		uint vpkVersion = 1;
		if (!string.IsNullOrWhiteSpace(vpkVersionText))
		{
			if (!uint.TryParse(vpkVersionText, out vpkVersion) || (vpkVersion is not 1 and not 2))
			{
				return PrintError("Invalid --vpk-version value (expected 1 or 2).");
			}
		}

		var preloadBytesText = GetOptionValue(args, "--preload-bytes");
	var preloadBytes = 0;
	if (!string.IsNullOrWhiteSpace(preloadBytesText))
	{
		if (!int.TryParse(preloadBytesText, out preloadBytes) || preloadBytes < 0 || preloadBytes > ushort.MaxValue)
		{
			return PrintError($"Invalid --preload-bytes value (expected 0 to {ushort.MaxValue}).");
		}
	}

	var gmaTagsText = GetOptionValue(args, "--gma-tags");
	var ignoreWhitelist = HasFlag(args, "--ignore-whitelist-warnings");

	var withMd5 = HasFlag(args, "--with-md5");
	var writeLogFile = HasFlag(args, "--log");

	var vpkToolPath = GetOptionValue(args, "--vpk-tool");
	var gmadPath = GetOptionValue(args, "--gmad");
			var gameDirectory = GetOptionValue(args, "--game");
			var directOptions = GetOptionValue(args, "--opts");

		uint? steamAppId = null;
		var steamAppIdText = GetOptionValue(args, "--steam-appid");
		if (!string.IsNullOrWhiteSpace(steamAppIdText))
		{
			if (!uint.TryParse(steamAppIdText, out var parsed))
			{
				return PrintError("Invalid --steam-appid value.");
			}

			steamAppId = parsed;
		}

		var steamRoot = GetOptionValue(args, "--steam-root");
		var winePrefix = GetOptionValue(args, "--wine-prefix");
		var wineCommand = GetOptionValue(args, "--wine") ?? "wine";

		if (batch)
		{
			PackBatchOutputType outputType;
			try
			{
				outputType = ParsePackBatchOutputType(GetOptionValue(args, "--type"));
			}
			catch (Exception ex)
			{
				return PrintError(ex.Message);
			}

			if (!Directory.Exists(inputDirectory))
			{
				return PrintError("Input folder not found.");
			}

			try
			{
				Directory.CreateDirectory(outputPath);
			}
			catch (Exception ex)
			{
				return PrintError($"Failed to create output folder: {ex.Message}");
			}

			if (!Directory.Exists(outputPath))
			{
				return PrintError("Output folder not found.");
			}

			if (outputType == PackBatchOutputType.Gma && (multiFile || withMd5 || maxArchiveSizeBytes is not null || vpkVersion != 1 || preloadBytes != 0))
			{
				Console.Error.WriteLine("Note: GMA output ignores VPK options (multi-file/split/version/MD5/preload).");
				Console.Error.WriteLine();
			}

			var multiFileForBatch = outputType == PackBatchOutputType.Gma ? false : multiFile;

			var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
			var childFolders = Directory.EnumerateDirectories(inputDirectory, "*", SearchOption.TopDirectoryOnly)
				.OrderBy(p => p, comparer)
				.ToArray();

			if (childFolders.Length == 0)
			{
				return PrintError("No child folders found in the selected parent folder.");
			}

			var successes = 0;
			var failures = 0;

			foreach (var childFolder in childFolders)
			{
				var childName = Path.GetFileName(Path.TrimEndingDirectorySeparator(childFolder));
				var outputFilePath = GetPackBatchOutputPath(childFolder, outputPath, outputType, multiFileForBatch);

				try
				{
					if (outputType == PackBatchOutputType.Gma)
					{
						EnsureGmaAddonJson(childFolder, gmaTagsText);
					}

					await app.PackAsync(
						new Stunstick.App.Pack.PackRequest(
							childFolder,
							outputFilePath,
							MultiFile: multiFileForBatch,
								MaxArchiveSizeBytes: maxArchiveSizeBytes,
								PreloadBytes: preloadBytes,
								VpkVersion: vpkVersion,
								IncludeMd5Sections: withMd5,
								GameDirectory: gameDirectory,
								SteamAppId: steamAppId,
							SteamRoot: steamRoot,
							GmadPath: gmadPath,
							VpkToolPath: vpkToolPath,
							WineOptions: new WineOptions(Prefix: winePrefix, WineCommand: wineCommand),
							WriteLogFile: writeLogFile,
							DirectOptions: directOptions,
							IgnoreWhitelistWarnings: ignoreWhitelist),
						cancellationToken);

					successes++;
					Console.WriteLine($"Packed: {childName} -> {outputFilePath}");
				}
				catch (Exception ex)
				{
					failures++;
					Console.Error.WriteLine($"Pack failed: {childName}: {ex.Message}");
				}
			}

			Console.WriteLine($"Pack batch finished: {successes} succeeded, {failures} failed.");
			return failures == 0 ? 0 : 1;
		}

		if (string.Equals(Path.GetExtension(outputPath), ".gma", StringComparison.OrdinalIgnoreCase))
		{
			EnsureGmaAddonJson(inputDirectory, gmaTagsText);
		}

		await app.PackAsync(
			new Stunstick.App.Pack.PackRequest(
				inputDirectory,
				outputPath,
				MultiFile: multiFile,
					MaxArchiveSizeBytes: maxArchiveSizeBytes,
					PreloadBytes: preloadBytes,
					VpkVersion: vpkVersion,
					IncludeMd5Sections: withMd5,
					GameDirectory: gameDirectory,
					SteamAppId: steamAppId,
				SteamRoot: steamRoot,
				GmadPath: gmadPath,
				VpkToolPath: vpkToolPath,
				WineOptions: new WineOptions(Prefix: winePrefix, WineCommand: wineCommand),
				WriteLogFile: writeLogFile,
				DirectOptions: directOptions,
				IgnoreWhitelistWarnings: ignoreWhitelist),
			cancellationToken);
	Console.WriteLine($"Packed to: {outputPath}");
	return 0;
}

	if (string.Equals(command, "inspect", StringComparison.Ordinal))
	{
		if (args.Length < 2)
		{
			return PrintUsage(Console.Out);
		}

		var path = GetPositionalValue(args, 1);
		if (string.IsNullOrWhiteSpace(path))
		{
			return PrintUsage(Console.Out);
		}

		var fullPath = Path.GetFullPath(path);
		if (Directory.Exists(fullPath))
		{
			Console.WriteLine($"Path: {fullPath}");
			Console.WriteLine("Type: Directory");

			long totalBytes = 0;
			var fileCount = 0;
			var dirCount = 0;

				var enumerationOptions = new EnumerationOptions
				{
					RecurseSubdirectories = true,
					IgnoreInaccessible = true,
					AttributesToSkip = FileAttributes.ReparsePoint
				};

				foreach (var dir in Directory.EnumerateDirectories(fullPath, "*", enumerationOptions))
				{
					dirCount++;
				}

				foreach (var file in Directory.EnumerateFiles(fullPath, "*", enumerationOptions))
				{
					fileCount++;
					try
					{
					totalBytes += new FileInfo(file).Length;
				}
				catch
				{
				}
			}

			Console.WriteLine($"Dirs: {dirCount}");
			Console.WriteLine($"Files: {fileCount}");
			Console.WriteLine($"TotalBytes: {totalBytes}");
			return 0;
		}

			var inspectOptions = new InspectOptions(ComputeSha256: !HasFlag(args, "--no-hash"));
			var result = await app.InspectAsync(fullPath, inspectOptions, cancellationToken);

			int? mdlVersionOverride = null;
			var mdlVersionText = GetOptionValue(args, "--mdl-version");
			if (!string.IsNullOrWhiteSpace(mdlVersionText))
			{
				if (!int.TryParse(mdlVersionText.Trim(), out var parsed))
				{
					return PrintError("Invalid --mdl-version value.");
				}

				mdlVersionOverride = parsed;
			}

		Console.WriteLine($"Path: {result.Path}");
		Console.WriteLine($"Type: {result.FileType}");
		Console.WriteLine($"SizeBytes: {result.SizeBytes}");
		if (result.Sha256Hex is not null)
		{
			Console.WriteLine($"Sha256: {result.Sha256Hex}");
		}

		if (result.FileType == Stunstick.Core.StunstickFileType.Vpk)
		{
			var vpk = await app.InspectVpkAsync(fullPath, cancellationToken);
			Console.WriteLine();
			Console.WriteLine("VPK/FPX:");
			Console.WriteLine($"  DirectoryFile: {vpk.DirectoryFilePath}");
			Console.WriteLine($"  Signature: 0x{vpk.Signature:x8}");
			Console.WriteLine($"  Version: {vpk.Version}");
			Console.WriteLine($"  Entries: {vpk.EntryCount}");
			Console.WriteLine($"  TotalEntryBytes: {vpk.TotalEntryBytes}");
			Console.WriteLine($"  Archives: {vpk.ArchiveCount}");
		}

		if (result.FileType == Stunstick.Core.StunstickFileType.Mdl)
		{
			static void PrintLimitedList(string label, IReadOnlyList<string> items, int maxItems)
			{
				Console.WriteLine($"  {label}: {items.Count}");
				for (var i = 0; i < items.Count && i < maxItems; i++)
				{
					Console.WriteLine($"    {items[i]}");
				}

				if (items.Count > maxItems)
				{
					Console.WriteLine($"    … ({items.Count - maxItems} more)");
				}
			}

			if (mdlVersionOverride is not null)
			{
				Console.WriteLine($"OverrideMdlVersion: {mdlVersionOverride.Value}");
			}

			var mdl = await app.InspectMdlAsync(fullPath, new MdlInspectOptions(VersionOverride: mdlVersionOverride), cancellationToken);

			Console.WriteLine();
			Console.WriteLine("MDL:");
			Console.WriteLine($"  Name: {mdl.Name}");
			Console.WriteLine($"  Version: {mdl.Version}");
			Console.WriteLine($"  Checksum: {mdl.Checksum}");
			Console.WriteLine($"  Length: {mdl.Length}");
			Console.WriteLine($"  Flags: 0x{mdl.Flags:x8}");
			Console.WriteLine($"  Bones: {mdl.BoneCount}");
			Console.WriteLine($"  Sequences: {mdl.LocalSequenceCount}");
			Console.WriteLine($"  Animations: {mdl.LocalAnimationCount}");
			Console.WriteLine($"  TexturePaths: {mdl.TexturePathCount}");
			Console.WriteLine($"  Textures: {mdl.TextureCount}");
			Console.WriteLine($"  SkinFamilies: {mdl.SkinFamilyCount}");
			Console.WriteLine($"  SkinReferences: {mdl.SkinReferenceCount}");
			Console.WriteLine($"  BodyParts: {mdl.BodyPartCount}");
			Console.WriteLine($"  FlexDescs: {mdl.FlexDescCount}");
			Console.WriteLine($"  FlexControllers: {mdl.FlexControllerCount}");
			Console.WriteLine($"  FlexRules: {mdl.FlexRuleCount}");
			Console.WriteLine($"  AnimBlocks: {mdl.AnimBlockCount}");

			Console.WriteLine();
			PrintLimitedList("TexturePaths", mdl.TexturePaths, maxItems: 12);
			Console.WriteLine();
			PrintLimitedList("Textures", mdl.Textures, maxItems: 24);
			Console.WriteLine();
			PrintLimitedList("BodyParts", mdl.BodyParts, maxItems: 16);
			Console.WriteLine();
			PrintLimitedList("Bones", mdl.Bones, maxItems: 24);
			Console.WriteLine();
			PrintLimitedList("Sequences", mdl.Sequences, maxItems: 24);
			Console.WriteLine();
			PrintLimitedList("Animations", mdl.Animations, maxItems: 24);
		}

		return 0;
	}

			if (string.Equals(command, "compile", StringComparison.Ordinal))
			{
				var studioMdlPath = GetOptionValue(args, "--studiomdl");
				var qcPath = GetOptionValue(args, "--qc");
				if (string.IsNullOrWhiteSpace(qcPath))
			{
				return PrintUsage(Console.Out);
			}

				var gameDirectory = GetOptionValue(args, "--game");
				var noP4 = !HasFlag(args, "--no-nop4");
				var verbose = !HasFlag(args, "--no-verbose");
				var defineBonesFileName = GetOptionValue(args, "--definebones-qci");
				var defineBonesWriteQci = HasFlag(args, "--definebones-write-qci") || !string.IsNullOrWhiteSpace(defineBonesFileName);
				var defineBonesOverwrite = HasFlag(args, "--definebones-overwrite-qci");
			var defineBonesModifyQc = HasFlag(args, "--definebones-modify-qc");
				var defineBones = HasFlag(args, "--definebones") || defineBonesWriteQci || defineBonesOverwrite || defineBonesModifyQc;

				var directOptions = GetOptionValue(args, "--opts");
				var writeLogFile = HasFlag(args, "--log");
				var copyTo = GetOptionValue(args, "--copy-to");
				var copyOutput = !string.IsNullOrWhiteSpace(copyTo);

				uint? steamAppId = null;
				var steamAppIdText = GetOptionValue(args, "--steam-appid");
				if (!string.IsNullOrWhiteSpace(steamAppIdText))
				{
				if (!uint.TryParse(steamAppIdText, out var parsed))
				{
					return PrintError("Invalid --steam-appid value.");
				}

				steamAppId = parsed;
			}

				var steamRoot = GetOptionValue(args, "--steam-root");
				if (string.IsNullOrWhiteSpace(studioMdlPath) && string.IsNullOrWhiteSpace(gameDirectory) && steamAppId is null)
				{
					return PrintUsage(Console.Out);
				}

				var resolvedGameDir = ResolveGameDirectory(gameDirectory, steamAppId, steamRoot);
				if (copyOutput)
				{
					if (string.IsNullOrWhiteSpace(resolvedGameDir))
					{
						return PrintError("--copy-to requires --game or --steam-appid.");
					}

					if (!Directory.Exists(resolvedGameDir))
					{
						return PrintError($"Game folder not found: \"{resolvedGameDir}\"");
					}

					try
					{
						Directory.CreateDirectory(copyTo!);
					}
					catch (Exception ex)
					{
						return PrintError($"Failed to create --copy-to folder: {ex.Message}");
					}
				}

					var winePrefix = GetOptionValue(args, "--wine-prefix");
					var wineCommand = GetOptionValue(args, "--wine") ?? "wine";

					var recursive = HasFlag(args, "--recursive");
				if (Directory.Exists(qcPath))
				{
					var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
					var qcFiles = Directory.EnumerateFiles(qcPath, "*.qc", search).ToArray();
					if (qcFiles.Length == 0)
					{
						return PrintError("No .qc files found in folder.");
					}

					var failures = 0;
					foreach (var qcFile in qcFiles)
					{
						var qciName = string.IsNullOrWhiteSpace(defineBonesFileName) ? "DefineBones" : defineBonesFileName;
						if (defineBonesWriteQci)
						{
							qciName = $"{Path.GetFileNameWithoutExtension(qcFile)}_{qciName}";
						}

						var qcExitCode = 0;
						try
						{
							qcExitCode = await app.CompileWithStudioMdlAsync(
								new StudioMdlCompileRequest(
									StudioMdlPath: studioMdlPath,
									QcPath: qcFile,
									GameDirectory: gameDirectory,
									SteamAppId: steamAppId,
									SteamRoot: steamRoot,
									WineOptions: new WineOptions(Prefix: winePrefix, WineCommand: wineCommand),
									NoP4: noP4,
									Verbose: verbose,
									DefineBones: defineBones,
									DefineBonesCreateQciFile: defineBonesWriteQci || defineBonesOverwrite || defineBonesModifyQc,
									DefineBonesQciFileName: qciName,
										DefineBonesOverwriteQciFile: defineBonesOverwrite,
										DefineBonesModifyQcFile: defineBonesModifyQc,
										DirectOptions: directOptions,
										WriteLogFile: writeLogFile,
										Output: new Progress<string>(Console.WriteLine)),
									cancellationToken);
						}
						catch (Exception ex)
						{
							failures++;
								Console.Error.WriteLine($"Compile failed: {qcFile}: {ex.Message}");
								continue;
							}

							if (qcExitCode != 0)
							{
								failures++;
								Console.Error.WriteLine($"Compile failed (exit {qcExitCode}): {qcFile}");
								continue;
							}

							if (copyOutput)
							{
								try
								{
									CopyCompileOutputs(qcFile, resolvedGameDir!, copyTo!, cancellationToken);
								}
								catch (Exception ex)
								{
									failures++;
									Console.Error.WriteLine($"Compile copy failed: {qcFile}: {ex.Message}");
								}
							}
						}

						Console.WriteLine($"Compile finished: {qcFiles.Length - failures} succeeded, {failures} failed.");
					return failures == 0 ? 0 : 1;
				}

				if (!File.Exists(qcPath))
				{
					return PrintError("QC file/folder not found.");
				}

					var exitCode = await app.CompileWithStudioMdlAsync(
						new StudioMdlCompileRequest(
							StudioMdlPath: studioMdlPath,
							QcPath: qcPath,
						GameDirectory: gameDirectory,
						SteamAppId: steamAppId,
						SteamRoot: steamRoot,
						WineOptions: new WineOptions(Prefix: winePrefix, WineCommand: wineCommand),
						NoP4: noP4,
						Verbose: verbose,
						DefineBones: defineBones,
						DefineBonesCreateQciFile: defineBonesWriteQci || defineBonesOverwrite || defineBonesModifyQc,
						DefineBonesQciFileName: string.IsNullOrWhiteSpace(defineBonesFileName) ? "DefineBones" : defineBonesFileName,
							DefineBonesOverwriteQciFile: defineBonesOverwrite,
							DefineBonesModifyQcFile: defineBonesModifyQc,
							DirectOptions: directOptions,
							WriteLogFile: writeLogFile,
								Output: new Progress<string>(Console.WriteLine)),
							cancellationToken);

					if (exitCode != 0)
					{
						return exitCode;
					}

					if (copyOutput)
					{
						try
						{
							CopyCompileOutputs(qcPath, resolvedGameDir!, copyTo!, cancellationToken);
						}
						catch (Exception ex)
						{
							Console.Error.WriteLine($"Compile copy failed: {ex.Message}");
							return 1;
						}
					}

					return exitCode;
				}

		if (string.Equals(command, "view", StringComparison.Ordinal))
		{
			var hlmvPath = GetOptionValue(args, "--hlmv");
			var mdlPath = GetOptionValue(args, "--mdl");
			if (string.IsNullOrWhiteSpace(mdlPath))
			{
				return PrintUsage(Console.Out);
			}

			var gameDirectory = GetOptionValue(args, "--game");
			uint? steamAppId = null;
			var steamAppIdText = GetOptionValue(args, "--steam-appid");
			if (!string.IsNullOrWhiteSpace(steamAppIdText))
			{
				if (!uint.TryParse(steamAppIdText, out var parsed))
				{
					return PrintError("Invalid --steam-appid value.");
				}

				steamAppId = parsed;
			}

			var steamRoot = GetOptionValue(args, "--steam-root");
			if (string.IsNullOrWhiteSpace(hlmvPath) && string.IsNullOrWhiteSpace(gameDirectory) && steamAppId is null)
			{
				return PrintUsage(Console.Out);
			}

			var winePrefix = GetOptionValue(args, "--wine-prefix");
			var wineCommand = GetOptionValue(args, "--wine") ?? "wine";
			var viewAsReplacement = HasFlag(args, "--replacement");

			var exitCode = await app.ViewWithHlmvAsync(
				new HlmvViewRequest(
					HlmvPath: hlmvPath,
					MdlPath: mdlPath,
					GameDirectory: gameDirectory,
					SteamAppId: steamAppId,
					SteamRoot: steamRoot,
					WineOptions: new WineOptions(Prefix: winePrefix, WineCommand: wineCommand),
					ViewAsReplacement: viewAsReplacement),
				cancellationToken);

			return exitCode;
		}

	return PrintUsage(Console.Out);
}
catch (FileNotFoundException ex)
{
	return PrintError(ex.Message);
}
catch (InvalidDataException ex)
{
	return PrintError(ex.Message);
}
catch (NotImplementedException ex)
{
	return PrintError(ex.Message);
}
catch (NotSupportedException ex)
{
	return PrintError(ex.Message);
}
catch (Exception ex)
{
	return PrintError(ex.ToString());
}

enum PackBatchOutputType
{
	Vpk = 0,
	Fpx = 1,
	Gma = 2
}
