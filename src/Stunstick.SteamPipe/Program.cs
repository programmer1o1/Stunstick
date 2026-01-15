using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

static class Program
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver()
	};

	private static SteamAPIWarningMessageHook_t? warningHook;

	public static async Task<int> Main(string[] args)
	{
		if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
		{
			PrintUsage();
			return 2;
		}

		var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += (_, e) =>
		{
			e.Cancel = true;
			cancellation.Cancel();
		};

		try
		{
			var command = args[0].Trim().ToLowerInvariant();

			var appIdText = GetOptionValue(args, "--appid");
			if (string.IsNullOrWhiteSpace(appIdText) || !uint.TryParse(appIdText, out var appId) || appId == 0)
			{
				return WriteError("Missing/invalid --appid.");
			}

			PrepareSteamAppId(appId);
			TryWriteProcessDiagnostics();

			var initResult = SteamAPI.InitEx(out var steamErrorMsg);
			if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
			{
				return WriteError($"SteamAPI.Init failed: {initResult} {steamErrorMsg}".Trim());
			}

			warningHook = (severity, text) =>
			{
				WriteEvent(new { type = "log", message = $"Steam warning ({severity}): {text}" });
			};
			SteamUtils.SetWarningMessageHook(warningHook);

			return command switch
			{
				"download" => await DownloadAsync(args, appId, cancellation.Token),
				"publish" => await PublishAsync(args, appId, cancellation.Token),
				"delete" => await DeleteAsync(args, appId, cancellation.Token),
				"list" => await ListAsync(args, appId, cancellation.Token),
				"quota" => Quota(args, appId),
				_ => WriteError($"Unknown command: {command}")
			};
		}
		catch (OperationCanceledException)
		{
			return WriteError("Canceled.");
		}
		catch (Exception ex)
		{
			return WriteError(ex.Message);
		}
		finally
		{
			try
			{
				SteamAPI.Shutdown();
			}
			catch
			{
			}
		}
	}

	private static async Task<int> DownloadAsync(string[] args, uint appId, CancellationToken cancellationToken)
	{
		var publishedText = GetOptionValue(args, "--published-id");
		if (string.IsNullOrWhiteSpace(publishedText) || !ulong.TryParse(publishedText, out var publishedId) || publishedId == 0)
		{
			return WriteError("Missing/invalid --published-id.");
		}

		var publishedFileId = new PublishedFileId_t(publishedId);
		var resultTcs = new TaskCompletionSource<DownloadItemResult_t>(TaskCreationOptions.RunContinuationsAsynchronously);

		using var downloadCallback = Callback<DownloadItemResult_t>.Create(r =>
		{
			if (r.m_nPublishedFileId == publishedFileId)
			{
				resultTcs.TrySetResult(r);
			}
		});

		WriteEvent(new { type = "log", message = $"Steamworks: DownloadItem {publishedId} (AppID {appId})..." });

		if (!SteamUGC.DownloadItem(publishedFileId, bHighPriority: true))
		{
			return WriteError("SteamUGC.DownloadItem returned false.");
		}

		ulong lastDownloaded = 0;
		ulong lastTotal = 0;

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SteamAPI.RunCallbacks();

			if (SteamUGC.GetItemDownloadInfo(publishedFileId, out var downloaded, out var total) && total > 0)
			{
				if (downloaded != lastDownloaded || total != lastTotal)
				{
					lastDownloaded = downloaded;
					lastTotal = total;
					WriteEvent(new { type = "download_progress", bytesDownloaded = downloaded, bytesTotal = total });
				}
			}

			if (resultTcs.Task.IsCompleted)
			{
				break;
			}

			await Task.Delay(200, cancellationToken).ConfigureAwait(false);
		}

		var result = await resultTcs.Task.ConfigureAwait(false);
		if (result.m_eResult != EResult.k_EResultOK)
		{
			return WriteError($"SteamUGC.DownloadItem failed: {result.m_eResult}");
		}

		if (!SteamUGC.GetItemInstallInfo(publishedFileId, out var sizeOnDisk, out var folder, 4096, out var timeStamp))
		{
			return WriteError("SteamUGC.GetItemInstallInfo failed.");
		}

		if (string.IsNullOrWhiteSpace(folder))
		{
			return WriteError("SteamUGC.GetItemInstallInfo returned an empty folder path.");
		}

		WriteEvent(new
		{
			type = "download_result",
			appId,
			publishedFileId = publishedId,
			installFolder = folder,
			sizeOnDisk,
			timeStamp
		});

		return 0;
	}

	private static async Task<int> PublishAsync(string[] args, uint appId, CancellationToken cancellationToken)
	{
		var contentFolder = GetOptionValue(args, "--content");
		var previewFile = GetOptionValue(args, "--preview");
		var title = GetOptionValue(args, "--title");
		var description = GetOptionValue(args, "--description");
		var changeNote = GetOptionValue(args, "--change-note");

		if (string.IsNullOrWhiteSpace(contentFolder) ||
			string.IsNullOrWhiteSpace(previewFile) ||
			string.IsNullOrWhiteSpace(title) ||
			string.IsNullOrWhiteSpace(description) ||
			string.IsNullOrWhiteSpace(changeNote))
			{
				return WriteError("Missing required publish args. Need --content --preview --title --description --change-note.");
			}

		contentFolder = Path.GetFullPath(contentFolder.Trim());
		previewFile = Path.GetFullPath(previewFile.Trim());

		WriteEvent(new { type = "log", message = $"Steamworks: content folder: {contentFolder}" });
		WriteEvent(new { type = "log", message = $"Steamworks: preview file: {previewFile}" });
		WritePublishPathDiagnostics(appId, contentFolder, previewFile);

		if (!Directory.Exists(contentFolder))
		{
			return WriteError($"Content folder not found: {contentFolder}");
		}

		if (!File.Exists(previewFile))
		{
			return WriteError($"Preview file not found: {previewFile}");
		}

		try
		{
			using var previewStream = File.OpenRead(previewFile);
			WriteEvent(new { type = "log", message = $"Steamworks: preview size: {previewStream.Length} bytes" });
		}
		catch (Exception ex)
		{
			return WriteError($"Preview file could not be read: {ex.Message}");
		}

		try
		{
			var (fileCount, totalBytes, truncated) = GetDirectoryFileStats(contentFolder, cancellationToken);
			if (fileCount == 0)
			{
				return WriteError($"Content folder contains no files: {contentFolder}");
			}

			var sizeText = truncated
				? $"Steamworks: content files: {fileCount}+ (sampled bytes: {totalBytes})"
				: $"Steamworks: content files: {fileCount} (bytes: {totalBytes})";
			WriteEvent(new { type = "log", message = sizeText });
		}
		catch (Exception ex)
		{
			return WriteError($"Content folder could not be enumerated: {ex.Message}");
		}

		if (appId == 4000)
		{
			try
			{
				var hasTopDir = Directory.EnumerateDirectories(contentFolder, "*", SearchOption.TopDirectoryOnly).Any();
				var topFiles = Directory.EnumerateFiles(contentFolder, "*", SearchOption.TopDirectoryOnly).ToArray();
				var hasGma = topFiles.Any(f => string.Equals(Path.GetExtension(f), ".gma", StringComparison.OrdinalIgnoreCase));

				if (hasTopDir || !hasGma)
				{
					return WriteError("Garry's Mod Workshop content must be a folder containing a .gma file (and no top-level subfolders).");
				}

				var gmaPath = topFiles.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".gma", StringComparison.OrdinalIgnoreCase));
				if (!string.IsNullOrWhiteSpace(gmaPath) && File.Exists(gmaPath))
				{
					try
					{
						using var gmaStream = File.OpenRead(gmaPath);
						var header = new byte[4];
						var read = gmaStream.Read(header, 0, header.Length);
						var headerText = read == 4 ? Encoding.ASCII.GetString(header) : "(short read)";
						WriteEvent(new
						{
							type = "log",
							message = $"Steamworks: gma file: {Path.GetFileName(gmaPath)} ({gmaStream.Length} bytes) header={headerText}"
						});
					}
					catch (Exception ex)
					{
						return WriteError($"GMA file could not be read: {ex.Message}");
					}
				}
			}
			catch
			{
				// Best-effort.
			}
		}

		var publishedText = GetOptionValue(args, "--published-id");
		ulong publishedId = 0;
		if (!string.IsNullOrWhiteSpace(publishedText) && (!ulong.TryParse(publishedText, out publishedId)))
		{
			return WriteError("Invalid --published-id.");
		}

		var visibility = (GetOptionValue(args, "--visibility") ?? "public").ToLowerInvariant() switch
		{
			"public" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
			"friends" or "friendsonly" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
			"private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
			"unlisted" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
			_ => (ERemoteStoragePublishedFileVisibility?)null
		};
		if (visibility is null)
		{
			return WriteError("Invalid --visibility value (expected public|friends|private|unlisted).");
		}

		var tags = ParseCsv(GetOptionValue(args, "--tags"));

		var consumerAppId = new AppId_t(appId);
		var publishedFileId = new PublishedFileId_t(publishedId);

		TryWriteSteamUserDiagnostics();
		TryWriteAppInstallDirDiagnostics(appId);

		if (publishedId != 0)
		{
			var exists = await TryWritePublishedItemDetailsAsync(appId, publishedFileId, cancellationToken).ConfigureAwait(false);
			if (exists == false)
			{
				return WriteError($"Published item not found/accessible: {publishedId} (AppID {appId}).");
			}
		}

		if (publishedId == 0)
		{
			WriteEvent(new { type = "log", message = "Steamworks: CreateItem..." });
			var createCall = SteamUGC.CreateItem(consumerAppId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
			var createResult = await WaitForCallResultAsync<CreateItemResult_t>(createCall, cancellationToken).ConfigureAwait(false);

			if (createResult.m_eResult != EResult.k_EResultOK)
			{
				return WriteError($"SteamUGC.CreateItem failed: {createResult.m_eResult}");
			}

			publishedFileId = createResult.m_nPublishedFileId;
			publishedId = (ulong)publishedFileId.m_PublishedFileId;

			if (createResult.m_bUserNeedsToAcceptWorkshopLegalAgreement)
			{
				WriteEvent(new { type = "log", message = "Steamworks: user must accept Workshop legal agreement (check Steam client / overlay)." });
			}
		}

		if (publishedId != 0)
		{
			TryWriteSteamItemPathDiagnostics(appId, publishedFileId);
		}

		async Task<SubmitItemUpdateResult_t> SubmitUpdateAsync(string contentPath, string previewPath)
		{
			var handle = SteamUGC.StartItemUpdate(consumerAppId, publishedFileId);

			if (!SteamUGC.SetItemTitle(handle, title))
			{
				throw new InvalidOperationException("SteamUGC.SetItemTitle failed.");
			}
			if (!SteamUGC.SetItemDescription(handle, description))
			{
				throw new InvalidOperationException("SteamUGC.SetItemDescription failed.");
			}
			if (!SteamUGC.SetItemVisibility(handle, visibility.Value))
			{
				throw new InvalidOperationException("SteamUGC.SetItemVisibility failed.");
			}
			if (tags.Count > 0 && !SteamUGC.SetItemTags(handle, tags))
			{
				throw new InvalidOperationException("SteamUGC.SetItemTags failed.");
			}
			if (!SteamUGC.SetItemContent(handle, contentPath))
			{
				throw new InvalidOperationException("SteamUGC.SetItemContent failed.");
			}
			if (!SteamUGC.SetItemPreview(handle, previewPath))
			{
				throw new InvalidOperationException("SteamUGC.SetItemPreview failed.");
			}

			WriteEvent(new { type = "log", message = $"Steamworks: SubmitItemUpdate {publishedId}..." });

			var submitCall = SteamUGC.SubmitItemUpdate(handle, changeNote);
			var submitTask = WaitForCallResultAsync<SubmitItemUpdateResult_t>(submitCall, cancellationToken);

			ulong lastProcessed = 0;
			ulong lastTotal = 0;
			EItemUpdateStatus lastStatus = 0;

			while (!submitTask.IsCompleted)
			{
				cancellationToken.ThrowIfCancellationRequested();
				SteamAPI.RunCallbacks();

				var status = SteamUGC.GetItemUpdateProgress(handle, out var processed, out var total);
				if (processed != lastProcessed || total != lastTotal || status != lastStatus)
				{
					lastProcessed = processed;
					lastTotal = total;
					lastStatus = status;

					WriteEvent(new { type = "publish_progress", status = status.ToString(), bytesProcessed = processed, bytesTotal = total });
				}

				await Task.Delay(200, cancellationToken).ConfigureAwait(false);
			}

			return await submitTask.ConfigureAwait(false);
		}

		SubmitItemUpdateResult_t submitResult;
		try
		{
			submitResult = await SubmitUpdateAsync(contentFolder, previewFile).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			return WriteError(ex.Message);
		}

		if (submitResult.m_eResult != EResult.k_EResultOK)
		{
			if (submitResult.m_eResult == EResult.k_EResultFileNotFound)
			{
				WriteEvent(new { type = "log", message = "Steamworks: file not found; retrying from staged payload folder..." });

				string? stagingRoot = null;
				try
				{
					(stagingRoot, var stagedContent, var stagedPreview) = StagePublishInputs(appId, contentFolder, previewFile);
					WriteEvent(new { type = "log", message = $"Steamworks: staged content folder: {stagedContent}" });
					WriteEvent(new { type = "log", message = $"Steamworks: staged preview file: {stagedPreview}" });
					WriteEvent(new { type = "log", message = "Steamworks: staged retry preflight..." });
					WritePublishPathDiagnostics(appId, stagedContent, stagedPreview);
					WritePublishPayloadDiagnostics(appId, stagedContent, stagedPreview, cancellationToken, "staged retry");

					var retry = await SubmitUpdateAsync(stagedContent, stagedPreview).ConfigureAwait(false);
					if (retry.m_eResult == EResult.k_EResultOK)
					{
						submitResult = retry;
					}
					else
					{
						WriteEvent(new { type = "log", message = $"Steamworks: staged retry failed: {retry.m_eResult}" });
					}
				}
				catch (Exception ex)
				{
					WriteEvent(new { type = "log", message = $"Steamworks: staged retry threw: {ex.Message}" });
				}
				finally
				{
					if (!string.IsNullOrWhiteSpace(stagingRoot))
					{
						try
						{
							Directory.Delete(stagingRoot, recursive: true);
						}
						catch
						{
						}
					}
				}
			}

			if (submitResult.m_eResult != EResult.k_EResultOK)
			{
				return WriteError($"SteamUGC.SubmitItemUpdate failed: {submitResult.m_eResult}");
			}
		}

		if (submitResult.m_bUserNeedsToAcceptWorkshopLegalAgreement)
		{
			WriteEvent(new { type = "log", message = "Steamworks: user must accept Workshop legal agreement (check Steam client / overlay)." });
		}

		WriteEvent(new { type = "publish_result", appId, publishedFileId = publishedId });
		return 0;
	}

	private static void WritePublishPathDiagnostics(uint appId, string contentFolder, string previewFile)
	{
		string? tempRoot = null;
		try
		{
			tempRoot = Path.GetFullPath(Path.GetTempPath());
			WriteEvent(new { type = "log", message = $"Steamworks: temp root: {tempRoot}" });
		}
		catch
		{
		}

		string? stageBase = null;
		try
		{
			var stage = GetPreferredPublishStageBase(appId);
			stageBase = stage.BasePath;
			if (!string.IsNullOrWhiteSpace(stageBase))
			{
				var exists = false;
				try
				{
					exists = Directory.Exists(stageBase);
				}
				catch
				{
				}

				WriteEvent(new { type = "log", message = $"Steamworks: publish stage base: {stageBase} ({stage.Source}) exists={exists}" });
			}
		}
		catch
		{
		}

		TryWriteAppInstallDirDiagnostics(appId);
		WritePathDiagnostics("content folder", contentFolder, tempRoot);
		WritePathDiagnostics("preview file", previewFile, tempRoot);

		if (!string.IsNullOrWhiteSpace(tempRoot) &&
			(IsSubPathOf(contentFolder, tempRoot) || IsSubPathOf(previewFile, tempRoot)))
		{
			var sandboxText = GetSandboxEnvironmentText();
			if (!string.IsNullOrWhiteSpace(stageBase))
			{
				WriteEvent(new
				{
					type = "log",
					message = $"Steamworks: note: content/preview is under the temp folder. {sandboxText} If publish fails with k_EResultFileNotFound, a staged retry will copy the payload into: {stageBase}"
				});
			}
		}
	}

	private static void WritePublishPayloadDiagnostics(uint appId, string contentFolder, string previewFile, CancellationToken cancellationToken, string label)
	{
		try
		{
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: payload diagnostics..." });
		}
		catch
		{
		}

		try
		{
			var exists = Directory.Exists(contentFolder);
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: content folder exists={exists}" });
		}
		catch
		{
		}

		try
		{
			using var previewStream = File.OpenRead(previewFile);
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: preview size: {previewStream.Length} bytes" });
		}
		catch (Exception ex)
		{
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: preview read failed: {ex.Message}" });
		}

		try
		{
			var (fileCount, totalBytes, truncated) = GetDirectoryFileStats(contentFolder, cancellationToken);
			var sizeText = truncated
				? $"Steamworks: {label}: content files: {fileCount}+ (sampled bytes: {totalBytes})"
				: $"Steamworks: {label}: content files: {fileCount} (bytes: {totalBytes})";
			WriteEvent(new { type = "log", message = sizeText });
		}
		catch (Exception ex)
		{
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: content enumeration failed: {ex.Message}" });
		}

		if (appId != 4000)
		{
			return;
		}

		try
		{
			var hasTopDir = Directory.EnumerateDirectories(contentFolder, "*", SearchOption.TopDirectoryOnly).Any();
			var topFiles = Directory.EnumerateFiles(contentFolder, "*", SearchOption.TopDirectoryOnly).ToArray();
			var gmaPath = topFiles.FirstOrDefault(f => string.Equals(Path.GetExtension(f), ".gma", StringComparison.OrdinalIgnoreCase));
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: gma layout: topDirs={hasTopDir} topFiles={topFiles.Length} hasGma={gmaPath is not null}" });

			if (!string.IsNullOrWhiteSpace(gmaPath) && File.Exists(gmaPath))
			{
				using var gmaStream = File.OpenRead(gmaPath);
				var header = new byte[4];
				var read = gmaStream.Read(header, 0, header.Length);
				var headerText = read == 4 ? Encoding.ASCII.GetString(header) : "(short read)";
				WriteEvent(new { type = "log", message = $"Steamworks: {label}: gma file: {Path.GetFileName(gmaPath)} ({gmaStream.Length} bytes) header={headerText}" });
			}
		}
		catch (Exception ex)
		{
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: gma check failed: {ex.Message}" });
		}
	}

	private static void TryWriteProcessDiagnostics()
	{
		try
		{
			var assembly = Assembly.GetExecutingAssembly();
			var version = assembly.GetName().Version?.ToString() ?? "unknown";
			var location = string.IsNullOrWhiteSpace(assembly.Location) ? "(none)" : assembly.Location;
			WriteEvent(new { type = "log", message = $"Steamworks: steam pipe assembly: {location} version={version}" });
		}
		catch
		{
		}

		try
		{
			var processPath = string.IsNullOrWhiteSpace(Environment.ProcessPath) ? "(unknown)" : Environment.ProcessPath;
			WriteEvent(new { type = "log", message = $"Steamworks: process path: {processPath} base dir: {AppContext.BaseDirectory}" });
		}
		catch
		{
		}
	}

	private static void TryWriteSteamUserDiagnostics()
	{
		try
		{
			var loggedOn = SteamUser.BLoggedOn();
			var steamId = SteamUser.GetSteamID();
			var accountId = steamId.GetAccountID();
			WriteEvent(new { type = "log", message = $"Steamworks: user: {steamId} accountId={accountId} loggedOn={loggedOn}" });
		}
		catch
		{
		}
	}

	private static void TryWriteSteamItemPathDiagnostics(uint appId, PublishedFileId_t publishedFileId)
	{
		try
		{
			var stateValue = SteamUGC.GetItemState(publishedFileId);
			var state = (EItemState)stateValue;
			WriteEvent(new { type = "log", message = $"Steamworks: item state (AppID {appId}): {state} ({stateValue})" });
		}
		catch
		{
		}

		try
		{
			if (SteamUGC.GetItemInstallInfo(publishedFileId, out var sizeOnDisk, out var folder, 4096, out var timeStamp))
			{
				var folderText = string.IsNullOrWhiteSpace(folder) ? "(empty)" : folder;
				WriteEvent(new { type = "log", message = $"Steamworks: item install info (AppID {appId}): folder={folderText} sizeOnDisk={sizeOnDisk} timestamp={timeStamp}" });

				if (!string.IsNullOrWhiteSpace(folder))
				{
					string? tempRoot = null;
					try
					{
						tempRoot = Path.GetFullPath(Path.GetTempPath());
					}
					catch
					{
					}

					WritePathDiagnostics("item install folder", folder, tempRoot);
				}
			}
			else
			{
				WriteEvent(new { type = "log", message = $"Steamworks: GetItemInstallInfo returned false (AppID {appId})." });
			}
		}
		catch
		{
		}
	}

	private static void TryWriteAppInstallDirDiagnostics(uint appId)
	{
		try
		{
			var installDir = TryGetSteamAppInstallDir(appId);
			if (!string.IsNullOrWhiteSpace(installDir))
			{
				WriteEvent(new { type = "log", message = $"Steamworks: SteamApps.GetAppInstallDir: {installDir}" });

				var steamappsDir = TryFindAncestorDirectoryNamed(installDir, "steamapps", maxDepth: 8);
				if (!string.IsNullOrWhiteSpace(steamappsDir))
				{
					WriteEvent(new { type = "log", message = $"Steamworks: steamapps dir: {steamappsDir}" });

					try
					{
						var workshopDir = Path.Combine(steamappsDir, "workshop");
						var workshopExists = Directory.Exists(workshopDir);
						WriteEvent(new { type = "log", message = $"Steamworks: workshop dir: {workshopDir} exists={workshopExists}" });

						var workshopContentDir = Path.Combine(workshopDir, "content");
						var workshopContentExists = Directory.Exists(workshopContentDir);
						WriteEvent(new { type = "log", message = $"Steamworks: workshop content dir: {workshopContentDir} exists={workshopContentExists}" });
					}
					catch
					{
					}
				}
			}
			else
			{
				WriteEvent(new { type = "log", message = $"Steamworks: SteamApps.GetAppInstallDir returned empty (AppID {appId})." });
			}
		}
		catch
		{
		}

		try
		{
			var installed = SteamApps.BIsAppInstalled(new AppId_t(appId));
			WriteEvent(new { type = "log", message = $"Steamworks: SteamApps.BIsAppInstalled({appId})={installed}" });
		}
		catch
		{
		}
	}

	private static string? TryGetSteamAppInstallDir(uint appId)
	{
		try
		{
			var len = SteamApps.GetAppInstallDir(new AppId_t(appId), out var folder, 4096);
			if (len != 0 && !string.IsNullOrWhiteSpace(folder))
			{
				return Path.GetFullPath(folder);
			}
		}
		catch
		{
		}

		return null;
	}

	private static string? TryFindAncestorDirectoryNamed(string startDirectory, string directoryName, int maxDepth)
	{
		if (string.IsNullOrWhiteSpace(startDirectory) || string.IsNullOrWhiteSpace(directoryName) || maxDepth <= 0)
		{
			return null;
		}

		try
		{
			var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
			for (var depth = 0; depth < maxDepth && current is not null; depth++)
			{
				if (string.Equals(current.Name, directoryName, StringComparison.OrdinalIgnoreCase))
				{
					return current.FullName;
				}

				current = current.Parent;
			}
		}
		catch
		{
		}

		return null;
	}

	private static async Task<bool?> TryWritePublishedItemDetailsAsync(uint appId, PublishedFileId_t publishedFileId, CancellationToken cancellationToken)
	{
		UGCQueryHandle_t queryHandle = UGCQueryHandle_t.Invalid;
		try
		{
			WriteEvent(new { type = "log", message = $"Steamworks: querying item details: {publishedFileId.m_PublishedFileId}..." });
			queryHandle = SteamUGC.CreateQueryUGCDetailsRequest(new[] { publishedFileId }, 1);
			var queryCall = SteamUGC.SendQueryUGCRequest(queryHandle);
			var queryResult = await WaitForCallResultAsync<SteamUGCQueryCompleted_t>(queryCall, cancellationToken).ConfigureAwait(false);

			WriteEvent(new
			{
				type = "log",
				message = $"Steamworks: details query result: {queryResult.m_eResult} returned={queryResult.m_unNumResultsReturned} total={queryResult.m_unTotalMatchingResults} cached={queryResult.m_bCachedData}"
			});

			if (queryResult.m_eResult == EResult.k_EResultFileNotFound)
			{
				return false;
			}

			if (queryResult.m_eResult != EResult.k_EResultOK)
			{
				return null;
			}

			if (queryResult.m_unNumResultsReturned == 0)
			{
				return false;
			}

			if (!SteamUGC.GetQueryUGCResult(queryHandle, 0, out var details))
			{
				WriteEvent(new { type = "log", message = "Steamworks: GetQueryUGCResult failed." });
				return null;
			}

			WriteEvent(new
			{
				type = "log",
				message = $"Steamworks: item details: result={details.m_eResult} creatorAppId={details.m_nCreatorAppID.m_AppId} consumerAppId={details.m_nConsumerAppID.m_AppId} owner={details.m_ulSteamIDOwner} banned={details.m_bBanned} visibility={details.m_eVisibility} fileType={details.m_eFileType} title={details.m_rgchTitle}"
			});

			if (details.m_eResult == EResult.k_EResultFileNotFound)
			{
				return false;
			}

			if (details.m_nConsumerAppID.m_AppId != appId)
			{
				WriteEvent(new { type = "log", message = $"Steamworks: warning: item consumer AppID mismatch: expected {appId} but got {details.m_nConsumerAppID.m_AppId}." });
			}

			try
			{
				var me = SteamUser.GetSteamID();
				if ((ulong)me.m_SteamID != details.m_ulSteamIDOwner)
				{
					WriteEvent(new { type = "log", message = $"Steamworks: warning: item owner mismatch: current={(ulong)me.m_SteamID} itemOwner={details.m_ulSteamIDOwner}." });
				}
			}
			catch
			{
			}

			return true;
		}
		catch (Exception ex)
		{
			WriteEvent(new { type = "log", message = $"Steamworks: details query threw: {ex.Message}" });
			return null;
		}
		finally
		{
			if (queryHandle != UGCQueryHandle_t.Invalid)
			{
				try
				{
					SteamUGC.ReleaseQueryUGCRequest(queryHandle);
				}
				catch
				{
				}
			}
		}
	}

	private static void WritePathDiagnostics(string label, string fullPath, string? tempRoot)
	{
		try
		{
			var exists = Directory.Exists(fullPath) || File.Exists(fullPath);
			var inTemp = !string.IsNullOrWhiteSpace(tempRoot) && IsSubPathOf(fullPath, tempRoot!);
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: exists={exists} inTemp={inTemp} chars={fullPath.Length}" });
		}
		catch
		{
		}

		try
		{
			var attrs = File.GetAttributes(fullPath);
			var isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
			WriteEvent(new { type = "log", message = $"Steamworks: {label}: attributes={attrs} isSymlink={isReparse}" });

			if (isReparse)
			{
				string? resolved = null;
				try
				{
					var fileInfo = new FileInfo(fullPath);
					resolved = fileInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
				}
				catch
				{
					try
					{
						var directoryInfo = new DirectoryInfo(fullPath);
						resolved = directoryInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
					}
					catch
					{
					}
				}

				if (!string.IsNullOrWhiteSpace(resolved))
				{
					WriteEvent(new { type = "log", message = $"Steamworks: {label}: symlink target: {resolved}" });
				}
			}
		}
		catch
		{
		}
	}

	private static bool IsSubPathOf(string path, string basePath)
	{
		if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(basePath))
		{
			return false;
		}

		string fullPath;
		string fullBase;
		try
		{
			fullPath = Path.GetFullPath(path);
			fullBase = Path.GetFullPath(basePath);
		}
		catch
		{
			return false;
		}

		if (!fullBase.EndsWith(Path.DirectorySeparatorChar) && !fullBase.EndsWith(Path.AltDirectorySeparatorChar))
		{
			fullBase += Path.DirectorySeparatorChar;
		}

		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		return fullPath.StartsWith(fullBase, comparison);
	}

	private static string? GetSandboxEnvironmentText()
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLATPAK_ID")) || File.Exists("/.flatpak-info"))
			{
				return "Detected Flatpak; Steam may not be able to access sandboxed /tmp paths.";
			}
		}
		catch
		{
		}

		try
		{
			if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNAP")) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SNAP_NAME")))
			{
				return "Detected Snap; Steam may not be able to access sandboxed /tmp paths.";
			}
		}
		catch
		{
		}

		return "Steam may not be able to access /tmp paths in some sandboxed environments.";
	}

	private static (int FileCount, long TotalBytes, bool Truncated) GetDirectoryFileStats(string directory, CancellationToken cancellationToken)
	{
		const int maxFiles = 100_000;
		var count = 0;
		long totalBytes = 0;

		foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			cancellationToken.ThrowIfCancellationRequested();

			count++;
			if (count > maxFiles)
			{
				return (maxFiles, totalBytes, Truncated: true);
			}

			try
			{
				totalBytes += new FileInfo(file).Length;
			}
			catch
			{
			}
		}

		return (count, totalBytes, Truncated: false);
	}

	private static (string StagingRoot, string ContentFolder, string PreviewFile) StagePublishInputs(uint appId, string contentFolder, string previewFile)
	{
		var guid = Guid.NewGuid().ToString("N");
		var stage = GetPreferredPublishStageBase(appId);
		var dataRoot = stage.BasePath;
		if (string.IsNullOrWhiteSpace(dataRoot))
		{
			dataRoot = GetStunstickDataRoot();
			stage = (dataRoot, "StunstickData");
		}

		try
		{
			return StagePublishInputsAtBase(dataRoot, appId, guid, contentFolder, previewFile);
		}
		catch (Exception ex) when (stage.Source.StartsWith("Steam", StringComparison.Ordinal))
		{
			var fallbackRoot = GetStunstickDataRoot();
			WriteEvent(new { type = "log", message = $"Steamworks: staging under {stage.Source} failed ({ex.Message}); falling back to {fallbackRoot}." });
			return StagePublishInputsAtBase(fallbackRoot, appId, guid, contentFolder, previewFile);
		}
	}

	private static (string StagingRoot, string ContentFolder, string PreviewFile) StagePublishInputsAtBase(
		string dataRoot,
		uint appId,
		string guid,
		string contentFolder,
		string previewFile)
	{
		try
		{
			var tempRoot = Path.GetFullPath(Path.GetTempPath());
			if (IsSubPathOf(dataRoot, tempRoot))
			{
				WriteEvent(new { type = "log", message = $"Steamworks: warning: staging root is under temp: {dataRoot}" });
			}
		}
		catch
		{
		}

		var stagingRoot = Path.Combine(dataRoot, "SteamPipe", "PublishStage", appId.ToString(), guid);
		var stagedContent = Path.Combine(stagingRoot, "content");
		Directory.CreateDirectory(stagedContent);
		CopyDirectory(contentFolder, stagedContent);

		var previewExt = Path.GetExtension(previewFile);
		if (string.IsNullOrWhiteSpace(previewExt))
		{
			previewExt = ".jpg";
		}

		var stagedPreview = Path.Combine(stagingRoot, "preview" + previewExt);
		File.Copy(previewFile, stagedPreview, overwrite: true);

		return (stagingRoot, stagedContent, stagedPreview);
	}

	private static (string? BasePath, string Source) GetPreferredPublishStageBase(uint appId)
	{
		var workshopStageBase = TryGetSteamWorkshopStageBase(appId, out var source);
		if (!string.IsNullOrWhiteSpace(workshopStageBase))
		{
			return (workshopStageBase, source);
		}

		return (GetStunstickDataRoot(), "StunstickData");
	}

	private static string? TryGetSteamWorkshopStageBase(uint appId, out string source)
	{
		source = "SteamApps";
		var installDir = TryGetSteamAppInstallDir(appId);
		if (string.IsNullOrWhiteSpace(installDir))
		{
			source = "SteamRoot";
			return TryGetSteamWorkshopStageBaseFromRoot();
		}

		var steamappsDir = TryFindAncestorDirectoryNamed(installDir, "steamapps", maxDepth: 8);
		if (string.IsNullOrWhiteSpace(steamappsDir))
		{
			source = "SteamRoot";
			return TryGetSteamWorkshopStageBaseFromRoot();
		}

		var workshopDir = Path.Combine(steamappsDir, "workshop");
		return Path.Combine(workshopDir, "Stunstick");
	}

	private static string? TryGetSteamWorkshopStageBaseFromRoot()
	{
		var steamRoot = TryFindSteamRoot();
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			return null;
		}

		var steamAppsDir = Path.Combine(steamRoot, "steamapps");
		if (!Directory.Exists(steamAppsDir))
		{
			return null;
		}

		return Path.Combine(steamAppsDir, "workshop", "Stunstick");
	}

	private static string? TryFindSteamRoot()
	{
		foreach (var env in new[] { "STEAM_CLIENT_HOME", "STEAM_COMPAT_CLIENT_INSTALL_PATH", "STEAM_INSTALL_PATH" })
		{
			try
			{
				var value = Environment.GetEnvironmentVariable(env);
				if (string.IsNullOrWhiteSpace(value))
				{
					continue;
				}

				var fullPath = Path.GetFullPath(value);
				if (Directory.Exists(fullPath))
				{
					return fullPath;
				}
			}
			catch
			{
			}
		}

		foreach (var candidate in GetDefaultSteamRoots())
		{
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static IReadOnlyList<string> GetDefaultSteamRoots()
	{
		var roots = new List<string>();

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(home))
		{
			return roots;
		}

		if (OperatingSystem.IsWindows())
		{
			var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			if (!string.IsNullOrWhiteSpace(programFilesX86))
			{
				roots.Add(Path.Combine(programFilesX86, "Steam"));
			}

			var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrWhiteSpace(programFiles))
			{
				roots.Add(Path.Combine(programFiles, "Steam"));
			}
		}
		else if (OperatingSystem.IsMacOS())
		{
			roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
		}
		else
		{
			roots.Add(Path.Combine(home, ".steam", "steam"));
			roots.Add(Path.Combine(home, ".local", "share", "Steam"));
			roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
		}

		return roots;
	}

	private static string GetStunstickDataRoot()
	{
		string? tempRoot = null;
		try
		{
			tempRoot = Path.GetFullPath(Path.GetTempPath());
		}
		catch
		{
		}

		var candidates = new List<string>();

		try
		{
			var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			if (!string.IsNullOrWhiteSpace(localAppData))
			{
				candidates.Add(Path.Combine(localAppData, "Stunstick"));
			}
		}
		catch
		{
		}

		try
		{
			var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			if (!string.IsNullOrWhiteSpace(appData))
			{
				candidates.Add(Path.Combine(appData, "Stunstick"));
			}
		}
		catch
		{
		}

		try
		{
			var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			if (!string.IsNullOrWhiteSpace(xdgDataHome))
			{
				candidates.Add(Path.Combine(xdgDataHome, "Stunstick"));
			}
		}
		catch
		{
		}

		try
		{
			var homeEnv = Environment.GetEnvironmentVariable("HOME");
			if (!string.IsNullOrWhiteSpace(homeEnv))
			{
				candidates.Add(Path.Combine(homeEnv, ".local", "share", "Stunstick"));
			}
		}
		catch
		{
		}

		try
		{
			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (!string.IsNullOrWhiteSpace(home))
			{
				candidates.Add(Path.Combine(home, ".local", "share", "Stunstick"));
			}
		}
		catch
		{
		}

		foreach (var candidate in candidates.Distinct())
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			if (!string.IsNullOrWhiteSpace(tempRoot) && IsSubPathOf(candidate, tempRoot!))
			{
				continue;
			}

			return candidate;
		}

		foreach (var candidate in candidates.Distinct())
		{
			if (!string.IsNullOrWhiteSpace(candidate))
			{
				return candidate;
			}
		}

		return Path.Combine(Path.GetTempPath(), "Stunstick");
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
	{
		var sourceFullPath = Path.GetFullPath(sourceDirectory);
		if (!Directory.Exists(sourceFullPath))
		{
			throw new DirectoryNotFoundException($"Source folder not found: \"{sourceFullPath}\"");
		}

		var destinationFullPath = Path.GetFullPath(destinationDirectory);
		Directory.CreateDirectory(destinationFullPath);

		foreach (var directory in Directory.EnumerateDirectories(sourceFullPath, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(sourceFullPath, directory);
			if (relative.StartsWith("..", StringComparison.Ordinal))
			{
				continue;
			}

			Directory.CreateDirectory(Path.Combine(destinationFullPath, relative));
		}

		foreach (var file in Directory.EnumerateFiles(sourceFullPath, "*", SearchOption.AllDirectories))
		{
			var relative = Path.GetRelativePath(sourceFullPath, file);
			if (relative.StartsWith("..", StringComparison.Ordinal))
			{
				continue;
			}

			var destinationPath = Path.Combine(destinationFullPath, relative);
			var destinationDir = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrWhiteSpace(destinationDir))
			{
				Directory.CreateDirectory(destinationDir);
			}

			File.Copy(file, destinationPath, overwrite: true);
		}
	}

	private static async Task<int> DeleteAsync(string[] args, uint appId, CancellationToken cancellationToken)
	{
		var publishedText = GetOptionValue(args, "--published-id");
		if (string.IsNullOrWhiteSpace(publishedText) || !ulong.TryParse(publishedText, out var publishedId) || publishedId == 0)
		{
			return WriteError("Missing/invalid --published-id.");
		}

		var publishedFileId = new PublishedFileId_t(publishedId);

		WriteEvent(new { type = "log", message = $"Steamworks: DeleteItem {publishedId} (AppID {appId})..." });

		var deleteCall = SteamUGC.DeleteItem(publishedFileId);
		var deleteResult = await WaitForCallResultAsync<DeleteItemResult_t>(deleteCall, cancellationToken).ConfigureAwait(false);
		if (deleteResult.m_eResult != EResult.k_EResultOK)
		{
			return WriteError($"SteamUGC.DeleteItem failed: {deleteResult.m_eResult}");
		}

		WriteEvent(new { type = "delete_result", appId, publishedFileId = publishedId });
		return 0;
	}

	private static async Task<int> ListAsync(string[] args, uint appId, CancellationToken cancellationToken)
	{
		uint page = 1;
		var pageText = GetOptionValue(args, "--page");
		if (!string.IsNullOrWhiteSpace(pageText) && (!uint.TryParse(pageText, out page) || page == 0))
		{
			return WriteError("Invalid --page value (expected >= 1).");
		}

		var steamId = SteamUser.GetSteamID();
		var accountId = steamId.GetAccountID();
		if (accountId == new AccountID_t(0))
		{
			return WriteError("SteamUser.GetSteamID returned an invalid AccountID.");
		}

		var consumerAppId = new AppId_t(appId);

		WriteEvent(new { type = "log", message = $"Steamworks: Query published items (AppID {appId}, page {page})..." });

		var handle = SteamUGC.CreateQueryUserUGCRequest(
			accountId,
			EUserUGCList.k_EUserUGCList_Published,
			EUGCMatchingUGCType.k_EUGCMatchingUGCType_All,
			EUserUGCListSortOrder.k_EUserUGCListSortOrder_LastUpdatedDesc,
			consumerAppId,
			consumerAppId,
			page);

		try
		{
			SteamUGC.SetReturnLongDescription(handle, true);
			SteamUGC.SetReturnKeyValueTags(handle, true);

			var call = SteamUGC.SendQueryUGCRequest(handle);
			var completed = await WaitForCallResultAsync<SteamUGCQueryCompleted_t>(call, cancellationToken).ConfigureAwait(false);
			if (completed.m_eResult != EResult.k_EResultOK)
			{
				return WriteError($"SteamUGC query failed: {completed.m_eResult}");
			}

			var items = new List<object>();
			for (uint index = 0; index < completed.m_unNumResultsReturned; index++)
			{
				if (!SteamUGC.GetQueryUGCResult(completed.m_handle, index, out var details))
				{
					continue;
				}

				items.Add(new
				{
					publishedFileId = (ulong)details.m_nPublishedFileId.m_PublishedFileId,
					title = details.m_rgchTitle,
					description = details.m_rgchDescription,
					createdAt = details.m_rtimeCreated,
					updatedAt = details.m_rtimeUpdated,
					visibility = details.m_eVisibility switch
					{
						ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic => "public",
						ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly => "friends",
						ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate => "private",
						ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted => "unlisted",
						_ => details.m_eVisibility.ToString()
					},
					tags = ParseCsv(details.m_rgchTags),
					fileName = details.m_pchFileName,
					legacyFileSizeBytes = details.m_nFileSize,
					totalFilesSizeBytes = details.m_ulTotalFilesSize
				});
			}

			WriteEvent(new
			{
				type = "list_result",
				appId,
				page,
				returned = completed.m_unNumResultsReturned,
				totalMatching = completed.m_unTotalMatchingResults,
				items
			});

			return 0;
		}
		finally
		{
			try
			{
				SteamUGC.ReleaseQueryUGCRequest(handle);
			}
			catch
			{
			}
		}
	}

	private static int Quota(string[] args, uint appId)
	{
		if (!SteamRemoteStorage.GetQuota(out var totalBytes, out var availableBytes))
		{
			return WriteError("SteamRemoteStorage.GetQuota failed.");
		}

		WriteEvent(new
		{
			type = "quota_result",
			appId,
			totalBytes,
			availableBytes,
			usedBytes = totalBytes >= availableBytes ? totalBytes - availableBytes : 0
		});

		return 0;
	}

	private static async Task<T> WaitForCallResultAsync<T>(SteamAPICall_t call, CancellationToken cancellationToken) where T : struct
	{
		var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var callResult = CallResult<T>.Create((result, failed) =>
		{
			if (failed)
			{
				tcs.TrySetException(new InvalidOperationException("Steamworks call failed (I/O failure)."));
				return;
			}

			tcs.TrySetResult(result);
		});

		callResult.Set(call);

		while (!tcs.Task.IsCompleted)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SteamAPI.RunCallbacks();
			await Task.Delay(50, cancellationToken).ConfigureAwait(false);
		}

		return await tcs.Task.ConfigureAwait(false);
	}

	private static void PrepareSteamAppId(uint appId)
	{
		var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		// SteamAPI.Init reads the AppID from either environment variables or a steam_appid.txt file.
		// In practice (especially on Linux), the most reliable behavior is having steam_appid.txt
		// next to the process and setting the working directory there.
			var candidateDirs = new List<string>
			{
				AppContext.BaseDirectory,
				Path.Combine(Path.GetTempPath(), "Stunstick", "SteamPipe", appId.ToString())
			};

		string? chosenDir = null;
		foreach (var dir in candidateDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
		{
			try
			{
				Directory.CreateDirectory(dir);
				// Note: Steam's steam_appid.txt parser may not tolerate a UTF-8 BOM, so write without BOM.
				File.WriteAllText(Path.Combine(dir, "steam_appid.txt"), appId.ToString(), utf8NoBom);
				chosenDir = dir;
				break;
			}
			catch
			{
			}
		}

		try
		{
			Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
			Environment.SetEnvironmentVariable("SteamGameId", appId.ToString());
			// Some environments look for the all-caps variant (Linux env vars are case-sensitive).
			Environment.SetEnvironmentVariable("SteamAppID", appId.ToString());
			Environment.SetEnvironmentVariable("SteamGameID", appId.ToString());
		}
		catch
		{
		}

		try
		{
			if (!string.IsNullOrWhiteSpace(chosenDir))
			{
				Directory.SetCurrentDirectory(chosenDir);
			}
		}
		catch
		{
		}
	}

	private static int WriteError(string message)
	{
		WriteEvent(new { type = "error", message });
		return 1;
	}

	private static void WriteEvent(object payload)
	{
		Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
	}

		private static void PrintUsage()
		{
			Console.WriteLine("Stunstick.SteamPipe (Steamworks.NET helper)");
			Console.WriteLine();
			Console.WriteLine("Usage:");
			Console.WriteLine("  Stunstick.SteamPipe download --appid <id> --published-id <id>");
			Console.WriteLine("  Stunstick.SteamPipe publish --appid <id> --content <dir> --preview <file> --title <text> --description <text> --change-note <text> [--published-id <id>] [--visibility <public|friends|private|unlisted>] [--tags <csv>]");
			Console.WriteLine("  Stunstick.SteamPipe delete --appid <id> --published-id <id>");
			Console.WriteLine("  Stunstick.SteamPipe list --appid <id> [--page <n>]");
			Console.WriteLine("  Stunstick.SteamPipe quota --appid <id>");
		}

	private static string? GetOptionValue(string[] args, string optionName)
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

	private static bool HasFlag(string[] args, string flagName)
	{
		return args.Any(arg => string.Equals(arg, flagName, StringComparison.Ordinal));
	}

	private static List<string> ParseCsv(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return new List<string>();
		}

		return value
			.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(t => t.Trim())
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}
}
