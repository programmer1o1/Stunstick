using Stunstick.App.Progress;
using Stunstick.App.Pack;
using Stunstick.App.Toolchain;
using Stunstick.Core.Steam;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Stunstick.App.Workshop;

internal static class WorkshopPublisher
{
	private const uint GarrysModAppId = 4000;
	private static readonly string[] GarrysModAllowedContentTypes =
	{
		"ServerContent",
		"gamemode",
		"map",
		"weapon",
		"vehicle",
		"npc",
		"tool",
		"effects",
		"model"
	};

	private static readonly HashSet<string> GarrysModAllowedContentTags = new(StringComparer.OrdinalIgnoreCase)
	{
		"fun",
		"roleplay",
		"scenic",
		"movie",
		"realism",
		"cartoon",
		"water",
		"comic",
		"build"
	};

	private static readonly string[] RecommendedPayloadIgnorePatterns =
	{
		".git/",
		".github/",
		".vs/",
		".vscode/",
		".idea/",
		".svn/",
		".hg/",
		"bin/",
		"obj/",
		"node_modules/",
		"__pycache__/",
		".venv/",
		".pytest_cache/",
		".DS_Store",
		"Thumbs.db",
		"desktop.ini",
		".gitignore",
		".gitattributes",
		".gitmodules"
	};

	private static readonly HashSet<string> CommonJunkDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".git",
		".github",
		".vs",
		".vscode",
		".idea",
		".svn",
		".hg",
		"bin",
		"obj",
		"node_modules",
		"__pycache__",
		".venv",
		".pytest_cache"
	};

	private static readonly HashSet<string> CommonJunkFileNames = new(StringComparer.OrdinalIgnoreCase)
	{
		".ds_store",
		"thumbs.db",
		"desktop.ini",
		".gitignore",
		".gitattributes",
		".gitmodules"
	};

	public static Task<WorkshopPublishResult> PublishAsync(WorkshopPublishRequest request, CancellationToken cancellationToken)
	{
		return request.UseSteamworks
			? PublishViaSteamPipeAsync(request, cancellationToken)
			: PublishViaSteamCmdAsync(request, cancellationToken);
	}

	public static async Task<WorkshopListResult> ListMyPublishedItemsAsync(WorkshopListRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (request.AppId == 0)
		{
			throw new ArgumentException("AppID is required.", nameof(request));
		}

		if (request.Page == 0)
		{
			throw new ArgumentException("Page must be >= 1.", nameof(request));
		}

		request.Progress?.Report(new StunstickProgress("Workshop List", 0, 0, Message: "Listing published items via Steamworks..."));

		var result = await SteamPipeClient.ListPublishedAsync(
			appId: request.AppId,
			page: request.Page,
			steamPipePath: request.SteamPipePath,
			progress: request.Progress,
			output: request.Output,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		return new WorkshopListResult(
			AppId: result.AppId,
			Page: result.Page,
			Returned: result.Returned,
			TotalMatching: result.TotalMatching,
			Items: result.Items.Select(i => new WorkshopPublishedItem(
				PublishedFileId: i.PublishedFileId,
				Title: i.Title,
				Description: i.Description,
				CreatedAtUtc: i.CreatedAtUtc,
				UpdatedAtUtc: i.UpdatedAtUtc,
				Visibility: i.Visibility,
				Tags: i.Tags)).ToArray());
	}

	public static async Task<WorkshopQuotaResult> GetQuotaAsync(WorkshopQuotaRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (request.AppId == 0)
		{
			throw new ArgumentException("AppID is required.", nameof(request));
		}

		request.Progress?.Report(new StunstickProgress("Workshop Quota", 0, 0, Message: "Reading quota via Steamworks..."));

		var result = await SteamPipeClient.GetQuotaAsync(
			appId: request.AppId,
			steamPipePath: request.SteamPipePath,
			progress: request.Progress,
			output: request.Output,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		return new WorkshopQuotaResult(
			AppId: result.AppId,
			TotalBytes: result.TotalBytes,
			AvailableBytes: result.AvailableBytes,
			UsedBytes: result.UsedBytes);
	}

	public static async Task<WorkshopDeleteResult> DeleteAsync(WorkshopDeleteRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (request.AppId == 0)
		{
			throw new ArgumentException("AppID is required.", nameof(request));
		}

		if (request.PublishedFileId == 0)
		{
			throw new ArgumentException("PublishedFileId is required.", nameof(request));
		}

		request.Progress?.Report(new StunstickProgress("Workshop Delete", 0, 0, Message: "Deleting via Steamworks..."));

		await SteamPipeClient.DeleteAsync(
			request.AppId,
			request.PublishedFileId,
			request.SteamPipePath,
			request.Progress,
			request.Output,
			cancellationToken).ConfigureAwait(false);

		return new WorkshopDeleteResult(request.AppId, request.PublishedFileId);
	}

	private static async Task<WorkshopPublishResult> PublishViaSteamCmdAsync(WorkshopPublishRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (request.AppId == 0)
		{
			throw new ArgumentException("AppID is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.ContentFolder))
		{
			throw new ArgumentException("Content path is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.PreviewFile))
		{
			throw new ArgumentException("Preview file is required.", nameof(request));
		}

		if (!File.Exists(request.PreviewFile))
		{
			throw new FileNotFoundException("Preview file not found.", request.PreviewFile);
		}

		if (string.IsNullOrWhiteSpace(request.Title))
		{
			throw new ArgumentException("Title is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.Description))
		{
			throw new ArgumentException("Description is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.ChangeNote))
		{
			throw new ArgumentException("Change note is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.SteamCmdUsername))
		{
			throw new ArgumentException("SteamCMD username is required.", nameof(request));
		}

		var (contentFolderForVdf, cleanupFolders) = await PrepareWorkshopPayloadFolderAsync(request, cancellationToken).ConfigureAwait(false);

		var vdfPath = string.IsNullOrWhiteSpace(request.VdfPath)
			? Path.Combine(Path.GetTempPath(), $"stunstick_workshop_{request.AppId}_{(request.PublishedFileId == 0 ? "new" : request.PublishedFileId.ToString())}.vdf")
			: Path.GetFullPath(request.VdfPath);

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(vdfPath) ?? ".");

			WriteWorkshopVdf(
				vdfPath,
				appId: request.AppId,
				publishedFileId: request.PublishedFileId,
				contentFolder: contentFolderForVdf,
				previewFile: request.PreviewFile,
				visibility: request.Visibility,
				title: request.Title,
				description: request.Description,
				changeNote: request.ChangeNote,
				tags: request.Tags);

			request.Progress?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Running SteamCMD..."));

			var steamCmdPath = FindSteamCmdExecutable(request.SteamCmdPath);
			if (steamCmdPath is null)
			{
				throw new FileNotFoundException("SteamCMD not found. Install SteamCMD or pass --steamcmd.");
			}

			await RunSteamCmdWorkshopBuildItemAsync(
				steamCmdPath,
				request.SteamCmdUsername!,
				vdfPath,
				request.Output,
				request.SteamCmdPromptAsync,
				cancellationToken).ConfigureAwait(false);

			var publishedId = TryReadPublishedFileIdFromVdf(vdfPath) ?? request.PublishedFileId;
			return new WorkshopPublishResult(
				AppId: request.AppId,
				PublishedFileId: publishedId,
				VdfPath: vdfPath);
		}
		finally
		{
			foreach (var folder in cleanupFolders)
			{
				try
				{
					Directory.Delete(folder, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static async Task<WorkshopPublishResult> PublishViaSteamPipeAsync(WorkshopPublishRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (request.AppId == 0)
		{
			throw new ArgumentException("AppID is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.ContentFolder))
		{
			throw new ArgumentException("Content path is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.PreviewFile))
		{
			throw new ArgumentException("Preview file is required.", nameof(request));
		}

		if (!File.Exists(request.PreviewFile))
		{
			throw new FileNotFoundException("Preview file not found.", request.PreviewFile);
		}

		if (string.IsNullOrWhiteSpace(request.Title))
		{
			throw new ArgumentException("Title is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.Description))
		{
			throw new ArgumentException("Description is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.ChangeNote))
		{
			throw new ArgumentException("Change note is required.", nameof(request));
		}

		var (contentFolderForUpload, cleanupFolders) = await PrepareWorkshopPayloadFolderAsync(request, cancellationToken).ConfigureAwait(false);

		try
		{
			(request.Progress)?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Uploading via Steamworks..."));

			var result = await SteamPipeClient.PublishAsync(
				appId: request.AppId,
				publishedFileId: request.PublishedFileId,
				contentFolder: contentFolderForUpload,
				previewFile: request.PreviewFile,
				title: request.Title,
				description: request.Description,
				changeNote: request.ChangeNote,
				visibility: request.Visibility,
				tags: request.Tags,
				steamPipePath: request.SteamPipePath,
				progress: request.Progress,
				output: request.Output,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return new WorkshopPublishResult(
				AppId: result.AppId,
				PublishedFileId: result.PublishedFileId,
				VdfPath: string.Empty);
		}
		finally
		{
			foreach (var folder in cleanupFolders)
			{
				try
				{
					Directory.Delete(folder, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static async Task<(string PayloadFolder, IReadOnlyList<string> CleanupFolders)> PrepareWorkshopPayloadFolderAsync(
		WorkshopPublishRequest request,
		CancellationToken cancellationToken)
	{
		var cleanupFolders = new List<string>();

		var inputPath = request.ContentFolder;
		var inputIsFile = File.Exists(inputPath);
		var inputIsDirectory = !inputIsFile && Directory.Exists(inputPath);
		if (!inputIsFile && !inputIsDirectory)
		{
			throw new DirectoryNotFoundException($"Content path not found: \"{inputPath}\".");
		}

		var payloadFolder = inputPath;

		if (inputIsFile)
		{
			var ext = Path.GetExtension(inputPath);
			if (request.AppId == GarrysModAppId && !string.Equals(ext, ".gma", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("Garry's Mod Workshop content must be a folder or a .gma file.");
			}

			(request.Progress)?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Preparing single-file payload folder..."));
			var prepared = CreateSingleFilePayloadFolder(inputPath);
			cleanupFolders.Add(prepared.CleanupFolder);
			payloadFolder = prepared.PayloadFolder;
		}
		else if (request.StageCleanPayload && request.AppId != GarrysModAppId && !IsVpkWorkshopPayloadFolder(payloadFolder))
		{
			(request.Progress)?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Staging clean payload folder..."));
			var prepared = CreateStagedPayloadFolder(payloadFolder, cancellationToken);
			cleanupFolders.Add(prepared.CleanupFolder);
			payloadFolder = prepared.PayloadFolder;
		}

		if (request.AppId == GarrysModAppId && inputIsDirectory && !IsGarrysModWorkshopPayloadFolder(payloadFolder))
		{
			(request.Progress)?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Packing .gma for Garry's Mod..."));
			var prepared = await CreateGarrysModPayloadFolderAsync(payloadFolder, request, cancellationToken).ConfigureAwait(false);
			cleanupFolders.Add(prepared.CleanupFolder);
			payloadFolder = prepared.PayloadFolder;
		}

		if (request.PackToVpkBeforeUpload && request.AppId != GarrysModAppId && inputIsDirectory && !IsVpkWorkshopPayloadFolder(payloadFolder))
		{
			(request.Progress)?.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Packing .vpk (workshop payload)..."));
			var prepared = await CreateVpkPayloadFolderAsync(payloadFolder, request, cancellationToken).ConfigureAwait(false);
			cleanupFolders.Add(prepared.CleanupFolder);
			payloadFolder = prepared.PayloadFolder;
		}

		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		return (Path.GetFullPath(payloadFolder), cleanupFolders.Distinct(comparer).ToArray());
	}

	private static void WriteWorkshopVdf(
		string vdfPath,
		uint appId,
		ulong publishedFileId,
		string contentFolder,
		string previewFile,
		WorkshopPublishVisibility visibility,
		string title,
		string description,
		string changeNote,
		IReadOnlyList<string>? tags)
	{
		var sb = new StringBuilder();
		sb.AppendLine("\"workshopitem\"");
		sb.AppendLine("{");
		sb.AppendLine($"\t\"appid\"\t\t\"{appId}\"");
		sb.AppendLine($"\t\"publishedfileid\"\t\t\"{publishedFileId}\"");
		sb.AppendLine($"\t\"contentfolder\"\t\t\"{EscapeVdfValue(Path.GetFullPath(contentFolder))}\"");
		sb.AppendLine($"\t\"previewfile\"\t\t\"{EscapeVdfValue(Path.GetFullPath(previewFile))}\"");
		sb.AppendLine($"\t\"visibility\"\t\t\"{(int)visibility}\"");
		sb.AppendLine($"\t\"title\"\t\t\"{EscapeVdfValue(title)}\"");
		sb.AppendLine($"\t\"description\"\t\t\"{EscapeVdfValue(description)}\"");
		sb.AppendLine($"\t\"changenote\"\t\t\"{EscapeVdfValue(changeNote)}\"");
		var tagText = BuildTagsCsv(tags);
		if (!string.IsNullOrWhiteSpace(tagText))
		{
			sb.AppendLine($"\t\"tags\"\t\t\"{EscapeVdfValue(tagText)}\"");
		}
		sb.AppendLine("}");

		File.WriteAllText(vdfPath, sb.ToString(), Encoding.UTF8);
	}

	private static bool IsGarrysModWorkshopPayloadFolder(string contentFolder)
	{
		if (string.IsNullOrWhiteSpace(contentFolder) || !Directory.Exists(contentFolder))
		{
			return false;
		}

		var hasTopDir = Directory.EnumerateDirectories(contentFolder, "*", SearchOption.TopDirectoryOnly).Any();
		if (hasTopDir)
		{
			return false;
		}

		var files = Directory.EnumerateFiles(contentFolder, "*", SearchOption.TopDirectoryOnly).ToArray();
		return files.Length == 1 && string.Equals(Path.GetExtension(files[0]), ".gma", StringComparison.OrdinalIgnoreCase);
	}

	private readonly record struct PreparedPayload(string PayloadFolder, string CleanupFolder);

	private static PreparedPayload CreateStagedPayloadFolder(string inputDirectory, CancellationToken cancellationToken)
	{
		var fullPath = Path.GetFullPath(inputDirectory);
		if (!Directory.Exists(fullPath))
		{
			throw new DirectoryNotFoundException($"Content folder not found: \"{inputDirectory}\".");
		}

		var guid = Guid.NewGuid().ToString("N");
		var tempRoot = Path.Combine(Path.GetTempPath(), "Stunstick", "Workshop", "Publish", $"stage_{guid}");
		var payloadFolder = Path.Combine(tempRoot, "payload");
		CopyDirectory(fullPath, payloadFolder, cancellationToken, excludeCommonJunk: true);
		return new PreparedPayload(PayloadFolder: payloadFolder, CleanupFolder: tempRoot);
	}

	private static bool IsVpkWorkshopPayloadFolder(string contentFolder)
	{
		if (string.IsNullOrWhiteSpace(contentFolder) || !Directory.Exists(contentFolder))
		{
			return false;
		}

		var hasTopDir = Directory.EnumerateDirectories(contentFolder, "*", SearchOption.TopDirectoryOnly).Any();
		if (hasTopDir)
		{
			return false;
		}

		var files = Directory.EnumerateFiles(contentFolder, "*", SearchOption.TopDirectoryOnly).ToArray();
		if (files.Length == 0)
		{
			return false;
		}

		return files.All(file =>
		{
			var ext = Path.GetExtension(file);
			return string.Equals(ext, ".vpk", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(ext, ".fpx", StringComparison.OrdinalIgnoreCase);
		});
	}

	private static PreparedPayload CreateSingleFilePayloadFolder(string inputFilePath)
	{
		var fullPath = Path.GetFullPath(inputFilePath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("Content file not found.", inputFilePath);
		}

		var guid = Guid.NewGuid().ToString("N");
		var tempRoot = Path.Combine(Path.GetTempPath(), "Stunstick", "Workshop", "Publish", $"file_{guid}");
		var payloadFolder = Path.Combine(tempRoot, "payload");
		Directory.CreateDirectory(payloadFolder);

		var filesToCopy = TryGetVpkMultiFilePayloadFiles(fullPath) ?? new[] { fullPath };

		foreach (var sourcePath in filesToCopy)
		{
			var fileName = Path.GetFileName(sourcePath);
			if (string.IsNullOrWhiteSpace(fileName))
			{
				throw new InvalidDataException("Content file name could not be determined.");
			}

			File.Copy(sourcePath, Path.Combine(payloadFolder, fileName), overwrite: true);
		}

		return new PreparedPayload(PayloadFolder: payloadFolder, CleanupFolder: tempRoot);
	}

	private static IReadOnlyList<string>? TryGetVpkMultiFilePayloadFiles(string inputFilePath)
	{
		var fullPath = Path.GetFullPath(inputFilePath);
		var directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return null;
		}

		var extension = Path.GetExtension(fullPath);
		if (!string.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var baseName = Path.GetFileNameWithoutExtension(fullPath);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return null;
		}

		var expectedDirectorySuffix = string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase) ? "_fdr" : "_dir";

		string? prefix = null;
		string? directoryFilePath = null;

		if (baseName.EndsWith(expectedDirectorySuffix, StringComparison.OrdinalIgnoreCase))
		{
			prefix = baseName[..^expectedDirectorySuffix.Length];
			directoryFilePath = fullPath;
		}
		else
		{
			var underscoreIndex = baseName.LastIndexOf('_');
			if (underscoreIndex <= 0)
			{
				return null;
			}

			var suffix = baseName[(underscoreIndex + 1)..];
			if (suffix.Length != 3 || suffix.Any(c => c < '0' || c > '9'))
			{
				return null;
			}

			prefix = baseName[..underscoreIndex];
			directoryFilePath = Path.Combine(directory, prefix + expectedDirectorySuffix + extension);
			if (!File.Exists(directoryFilePath))
			{
				return null;
			}
		}

		if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(directoryFilePath))
		{
			return null;
		}

		var files = new List<string> { directoryFilePath };
		foreach (var candidate in Directory.EnumerateFiles(directory, $"{prefix}_*{extension}", SearchOption.TopDirectoryOnly))
		{
			var candidateBaseName = Path.GetFileNameWithoutExtension(candidate);
			if (string.IsNullOrWhiteSpace(candidateBaseName) ||
				!candidateBaseName.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var indexText = candidateBaseName[(prefix.Length + 1)..];
			if (indexText.Length == 3 && indexText.All(c => c is >= '0' and <= '9'))
			{
				files.Add(candidate);
			}
		}

		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		return files
			.Distinct(comparer)
			.OrderBy(p => p, comparer)
			.ToArray();
	}

	private static async Task<PreparedPayload> CreateVpkPayloadFolderAsync(string inputDirectory, WorkshopPublishRequest request, CancellationToken cancellationToken)
	{
		var guid = Guid.NewGuid().ToString("N");
		var tempRoot = Path.Combine(Path.GetTempPath(), "Stunstick", "Workshop", "Publish", $"vpk_{guid}");
		var payloadFolder = Path.Combine(tempRoot, "payload");
		Directory.CreateDirectory(payloadFolder);

		var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileName(inputDirectory) : request.Title);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			baseName = "addon";
		}

		if (baseName.Length > 80)
		{
			baseName = baseName[..80];
		}

		var vpkVersion = request.VpkVersion;
		if (vpkVersion is not 1 and not 2)
		{
			throw new ArgumentOutOfRangeException(nameof(request.VpkVersion), "VPK version must be 1 or 2.");
		}

		var includeMd5 = request.VpkIncludeMd5Sections;
		if (includeMd5 && vpkVersion != 2)
		{
			vpkVersion = 2;
		}

		var multiFile = request.VpkMultiFile;
		var vpkPath = Path.Combine(payloadFolder, multiFile ? baseName + "_dir.vpk" : baseName + ".vpk");

		await VpkPacker.PackAsync(
			new PackRequest(
				InputDirectory: inputDirectory,
				OutputPackagePath: vpkPath,
				MultiFile: multiFile,
				VpkVersion: vpkVersion,
				IncludeMd5Sections: includeMd5,
				Progress: request.Progress),
			cancellationToken).ConfigureAwait(false);

		if (!File.Exists(vpkPath))
		{
			throw new InvalidDataException("Failed to create VPK payload for publish.");
		}

		return new PreparedPayload(PayloadFolder: payloadFolder, CleanupFolder: tempRoot);
	}

	private static async Task<PreparedPayload> CreateGarrysModPayloadFolderAsync(string inputDirectory, WorkshopPublishRequest request, CancellationToken cancellationToken)
	{
		var guid = Guid.NewGuid().ToString("N");
		var tempRoot = Path.Combine(Path.GetTempPath(), "Stunstick", "Workshop", "Publish", $"gmod_{guid}");
		var payloadFolder = Path.Combine(tempRoot, "payload");
		Directory.CreateDirectory(payloadFolder);

		var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileName(request.ContentFolder) : request.Title);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			baseName = "addon";
		}

		if (baseName.Length > 80)
		{
			baseName = baseName[..80];
		}

		var gmaPath = Path.Combine(payloadFolder, baseName + ".gma");

		var packInput = inputDirectory;
		var addonJsonPath = FindAddonJsonPath(packInput);
		var hasAddonJson = !string.IsNullOrWhiteSpace(addonJsonPath) && File.Exists(addonJsonPath);
		var hasCanonicalAddonJson = hasAddonJson && string.Equals(Path.GetFileName(addonJsonPath), "addon.json", StringComparison.OrdinalIgnoreCase);
		var shouldStage = request.StageCleanPayload || !hasAddonJson || !hasCanonicalAddonJson;

		if (shouldStage)
		{
			var stageFolder = Path.Combine(tempRoot, "stage");
			CopyDirectory(packInput, stageFolder, cancellationToken, excludeCommonJunk: true);

			var stageAddonJsonPath = Path.Combine(stageFolder, "addon.json");
			if (hasAddonJson && !File.Exists(stageAddonJsonPath))
			{
				var sourceFileName = Path.GetFileName(addonJsonPath);
				if (!string.IsNullOrWhiteSpace(sourceFileName))
				{
					var stagedSource = Path.Combine(stageFolder, sourceFileName);
					if (File.Exists(stagedSource))
					{
						File.Copy(stagedSource, stageAddonJsonPath, overwrite: true);
						if (!string.Equals(sourceFileName, "addon.json", StringComparison.OrdinalIgnoreCase))
						{
							try
							{
								File.Delete(stagedSource);
							}
							catch
							{
							}
						}
					}
				}
			}

			if (!File.Exists(stageAddonJsonPath))
			{
				var contentTags = request.ContentTags ?? request.Tags;
				WriteGarrysModAddonJson(
					stageAddonJsonPath,
					request.Title,
					request.Description,
					contentType: request.ContentType,
					contentTags: contentTags);
			}
			else
			{
				TryPatchGarrysModAddonJson(stageAddonJsonPath, request);
			}

			packInput = stageFolder;
		}

		var packRequest = new PackRequest(
			InputDirectory: packInput,
			OutputPackagePath: gmaPath,
			Progress: request.Progress);

		await GmaPacker.PackAsync(packRequest, new ToolchainLauncher(new SystemProcessLauncher()), cancellationToken).ConfigureAwait(false);

		if (!File.Exists(gmaPath))
		{
			throw new InvalidDataException("Failed to create GMA payload for publish.");
		}

		return new PreparedPayload(PayloadFolder: payloadFolder, CleanupFolder: tempRoot);
	}

	private static string SanitizeFileName(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		var invalid = Path.GetInvalidFileNameChars();
		var sb = new StringBuilder(input.Length);
		foreach (var c in input.Trim())
		{
			sb.Append(invalid.Contains(c) || c is '/' or '\\' ? '_' : c);
		}

		return sb.ToString().Trim().TrimEnd('.', ' ');
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken, bool excludeCommonJunk = false)
	{
		var sourceFullPath = Path.GetFullPath(sourceDirectory);
		if (!Directory.Exists(sourceFullPath))
		{
			throw new DirectoryNotFoundException($"Source folder not found: \"{sourceFullPath}\"");
		}

		var destinationFullPath = Path.GetFullPath(destinationDirectory);
		Directory.CreateDirectory(destinationFullPath);

		CopyRecursive(sourceFullPath, destinationFullPath, excludeCommonJunk, cancellationToken);
		return;

		void CopyRecursive(string sourcePath, string destinationPath, bool excludeJunk, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Directory.CreateDirectory(destinationPath);

			foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.TopDirectoryOnly))
			{
				ct.ThrowIfCancellationRequested();
				var name = Path.GetFileName(directory);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}
				if (excludeJunk && !string.IsNullOrWhiteSpace(name) && CommonJunkDirectoryNames.Contains(name))
				{
					continue;
				}

				CopyRecursive(directory, Path.Combine(destinationPath, name), excludeJunk, ct);
			}

			foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly))
			{
				ct.ThrowIfCancellationRequested();
				var name = Path.GetFileName(file);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}
				if (excludeJunk && !string.IsNullOrWhiteSpace(name) && CommonJunkFileNames.Contains(name))
				{
					continue;
				}

				File.Copy(file, Path.Combine(destinationPath, name), overwrite: true);
			}
		}
	}

	private static string? FindAddonJsonPath(string inputDirectoryFullPath)
	{
		var direct = Path.Combine(inputDirectoryFullPath, "addon.json");
		if (File.Exists(direct))
		{
			return direct;
		}

		string[] candidates;
		try
		{
			candidates = Directory.EnumerateFiles(inputDirectoryFullPath, "*.json", SearchOption.TopDirectoryOnly).ToArray();
		}
		catch
		{
			return null;
		}

		var addonMatch = candidates.FirstOrDefault(file => string.Equals(Path.GetFileName(file), "addon.json", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(addonMatch))
		{
			return addonMatch;
		}

		var dJsonMatch = candidates.FirstOrDefault(file => string.Equals(Path.GetFileName(file), "d.json", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(dJsonMatch))
		{
			return dJsonMatch;
		}

		return candidates.Length == 1 ? candidates[0] : null;
	}

	private static void TryPatchGarrysModAddonJson(string addonJsonPath, WorkshopPublishRequest request)
	{
		var fullPath = Path.GetFullPath(addonJsonPath);
		if (!File.Exists(fullPath))
		{
			return;
		}

		JsonNode? rootNode;
		try
		{
			var text = File.ReadAllText(fullPath, Encoding.UTF8);
			if (text.Length > 0 && text[0] == '\uFEFF')
			{
				text = text[1..];
			}
			var documentOptions = new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			};
			rootNode = JsonNode.Parse(text, nodeOptions: null, documentOptions: documentOptions);
		}
		catch (Exception ex)
		{
			throw new InvalidDataException($"Failed to read addon.json ({fullPath}): {ex.Message}", ex);
		}

		if (rootNode is not JsonObject obj)
		{
			throw new InvalidDataException("addon.json root must be a JSON object.");
		}

		var changed = false;

		if (!TryGetString(obj, "title", out var existingTitle) || string.IsNullOrWhiteSpace(existingTitle))
		{
			obj["title"] = request.Title?.Trim() ?? "Addon";
			changed = true;
		}

		if (!TryGetString(obj, "description", out _))
		{
			obj["description"] = request.Description ?? string.Empty;
			changed = true;
		}

		if (!TryGetString(obj, "type", out _) && !string.IsNullOrWhiteSpace(request.ContentType))
		{
			var typeInput = request.ContentType.Trim();
			var canonicalType = GarrysModAllowedContentTypes.FirstOrDefault(t => string.Equals(t, typeInput, StringComparison.OrdinalIgnoreCase));
			if (canonicalType is not null)
			{
				obj["type"] = canonicalType;
				changed = true;
			}
		}

		if (!obj.TryGetPropertyValue("tags", out var tagsNode) || tagsNode is null)
		{
			var tags = request.ContentTags ?? request.Tags;
			var cleanedTags = tags?
				.Select(t => t?.Trim())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t!.ToLowerInvariant())
				.Where(t => GarrysModAllowedContentTags.Contains(t))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Take(2)
				.ToArray() ?? Array.Empty<string>();

			if (cleanedTags.Length > 0)
			{
				var tagArray = new JsonArray();
				foreach (var tag in cleanedTags)
				{
					tagArray.Add(tag);
				}

				obj["tags"] = tagArray;
				changed = true;
			}
		}

		if (!obj.TryGetPropertyValue("ignore", out var ignoreNode) || ignoreNode is null || ignoreNode is not JsonArray ignoreArray)
		{
			ignoreArray = new JsonArray();
			obj["ignore"] = ignoreArray;
			changed = true;
		}

		var existingIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var node in ignoreArray)
		{
			if (node is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
			{
				existingIgnore.Add(s.Trim());
			}
		}

		foreach (var pattern in RecommendedPayloadIgnorePatterns)
		{
			if (!existingIgnore.Contains(pattern))
			{
				ignoreArray.Add(pattern);
				changed = true;
			}
		}

		if (!changed)
		{
			return;
		}

		var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
		var json = obj.ToJsonString(options) + Environment.NewLine;
		File.WriteAllText(fullPath, json, Encoding.UTF8);

		return;

		static bool TryGetString(JsonObject obj, string key, out string? value)
		{
			value = null;
			if (!obj.TryGetPropertyValue(key, out var node) || node is null)
			{
				return false;
			}

			if (node is JsonValue v && v.TryGetValue<string>(out var s))
			{
				value = s;
				return true;
			}

			return false;
		}
	}

	private static void WriteGarrysModAddonJson(
		string outputPath,
		string title,
		string description,
		string? contentType,
		IReadOnlyList<string>? contentTags)
	{
		var typeInput = string.IsNullOrWhiteSpace(contentType) ? "ServerContent" : contentType.Trim();
		var canonicalType = GarrysModAllowedContentTypes.FirstOrDefault(t => string.Equals(t, typeInput, StringComparison.OrdinalIgnoreCase));
		if (canonicalType is null)
		{
			throw new InvalidDataException($"Invalid Garry's Mod addon type: \"{typeInput}\".");
		}

		var cleanedTags = contentTags?
			.Select(t => t?.Trim())
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Select(t => t!.ToLowerInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray() ?? Array.Empty<string>();

		if (cleanedTags.Length > 2)
		{
			throw new InvalidDataException("Garry's Mod addon tags are limited to 2.");
		}

		foreach (var tag in cleanedTags)
		{
			if (!GarrysModAllowedContentTags.Contains(tag))
			{
				throw new InvalidDataException($"Invalid Garry's Mod addon tag: \"{tag}\".");
			}
		}

		var safeTitle = string.IsNullOrWhiteSpace(title) ? "Addon" : title.Trim();
		var safeDescription = description ?? string.Empty;

		var tagsJson = cleanedTags.Length == 0
			? "[]"
			: "[ " + string.Join(", ", cleanedTags.Select(t => System.Text.Json.JsonSerializer.Serialize(t))) + " ]";

		var ignoreJson = RecommendedPayloadIgnorePatterns.Length == 0
			? "[]"
			: "[ " + string.Join(", ", RecommendedPayloadIgnorePatterns.Select(p => System.Text.Json.JsonSerializer.Serialize(p))) + " ]";

		var json =
			"{" + Environment.NewLine +
			$"\t\"title\": {System.Text.Json.JsonSerializer.Serialize(safeTitle)}," + Environment.NewLine +
			$"\t\"type\": {System.Text.Json.JsonSerializer.Serialize(canonicalType)}," + Environment.NewLine +
			$"\t\"tags\": {tagsJson}," + Environment.NewLine +
			$"\t\"ignore\": {ignoreJson}," + Environment.NewLine +
			$"\t\"description\": {System.Text.Json.JsonSerializer.Serialize(safeDescription)}" + Environment.NewLine +
			"}" + Environment.NewLine;

		var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		File.WriteAllText(outputPath, json, utf8NoBom);
	}

	private static string? BuildTagsCsv(IReadOnlyList<string>? tags)
	{
		if (tags is null || tags.Count == 0)
		{
			return null;
		}

		var cleaned = tags
			.Select(tag => tag?.Trim())
			.Where(tag => !string.IsNullOrWhiteSpace(tag))
			.Select(tag => tag!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return cleaned.Length == 0 ? null : string.Join(",", cleaned);
	}

	private static string EscapeVdfValue(string value)
	{
		if (value is null)
		{
			return string.Empty;
		}

		return value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\r", "\\r")
			.Replace("\n", "\\n")
			.Replace("\t", "\\t");
	}

	private static ulong? TryReadPublishedFileIdFromVdf(string vdfPath)
	{
		try
		{
			var root = VdfParser.ParseFile(vdfPath);
			if (!root.TryGetObject("workshopitem", out var workshopItem))
			{
				return null;
			}

			if (!workshopItem.TryGetString("publishedfileid", out var publishedIdText))
			{
				return null;
			}

			return ulong.TryParse(publishedIdText, out var publishedId) ? publishedId : null;
		}
		catch
		{
			return null;
		}
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

	private static async Task RunSteamCmdWorkshopBuildItemAsync(
		string steamCmdPath,
		string username,
		string vdfPath,
		IProgress<string>? output,
		Func<SteamCmdPrompt, CancellationToken, Task<string?>>? promptAsync,
		CancellationToken cancellationToken)
	{
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
			"+login", username,
			"+workshop_build_item", vdfPath,
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
}
