using Stunstick.App.Progress;
using System.Diagnostics;
using System.Text.Json;

namespace Stunstick.App.Workshop;

internal static class SteamPipeClient
{
	public sealed record SteamPipeDownloadResult(
		uint AppId,
		ulong PublishedFileId,
		string InstallFolder);

	public sealed record SteamPipePublishResult(
		uint AppId,
		ulong PublishedFileId);

	public sealed record SteamPipePublishedItem(
		ulong PublishedFileId,
		string? Title,
		string? Description,
		DateTimeOffset? CreatedAtUtc,
		DateTimeOffset? UpdatedAtUtc,
		WorkshopPublishVisibility? Visibility,
		IReadOnlyList<string> Tags);

	public sealed record SteamPipeListResult(
		uint AppId,
		uint Page,
		uint Returned,
		uint TotalMatching,
		IReadOnlyList<SteamPipePublishedItem> Items);

	public sealed record SteamPipeQuotaResult(
		uint AppId,
		ulong TotalBytes,
		ulong AvailableBytes,
		ulong UsedBytes);

	public static Task<SteamPipeDownloadResult> DownloadAsync(
		uint appId,
		ulong publishedFileId,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		CancellationToken cancellationToken)
	{
		var args = new List<string>
		{
			"download",
			"--appid", appId.ToString(),
			"--published-id", publishedFileId.ToString()
		};

		return RunAndParseAsync(
			args,
			steamPipePath,
			progress,
			output,
			expectedResultType: "download_result",
			parseResult: payload =>
			{
				var resultAppId = ReadUInt32(payload, "appId") ?? appId;
				var resultPublishedId = ReadUInt64(payload, "publishedFileId") ?? publishedFileId;
				var installFolder = ReadString(payload, "installFolder");
				if (string.IsNullOrWhiteSpace(installFolder))
				{
					throw new InvalidDataException("SteamPipe returned an empty install folder.");
				}

				return new SteamPipeDownloadResult(resultAppId, resultPublishedId, installFolder);
			},
			onEvent: (type, payload) =>
			{
				if (type == "download_progress")
				{
					var downloaded = ReadUInt64(payload, "bytesDownloaded") ?? 0;
					var total = ReadUInt64(payload, "bytesTotal") ?? 0;
					progress?.Report(new StunstickProgress(
						Operation: "Workshop Download (Steamworks)",
						CompletedBytes: ClampToInt64(downloaded),
						TotalBytes: ClampToInt64(total),
						Message: "Downloading via Steamworks..."));
				}
			},
			cancellationToken);
	}

	public static Task<SteamPipePublishResult> PublishAsync(
		uint appId,
		ulong publishedFileId,
		string contentFolder,
		string previewFile,
		string title,
		string description,
		string changeNote,
		WorkshopPublishVisibility visibility,
		IReadOnlyList<string>? tags,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		CancellationToken cancellationToken)
	{
		var args = new List<string>
		{
			"publish",
			"--appid", appId.ToString(),
			"--content", Path.GetFullPath(contentFolder),
			"--preview", Path.GetFullPath(previewFile),
			"--title", title,
			"--description", description,
			"--change-note", changeNote,
			"--visibility", VisibilityToCliValue(visibility)
		};

		if (publishedFileId != 0)
		{
			args.Add("--published-id");
			args.Add(publishedFileId.ToString());
		}

		if (tags is not null && tags.Count > 0)
		{
			args.Add("--tags");
			args.Add(string.Join(",", tags));
		}

		return RunAndParseAsync(
			args,
			steamPipePath,
			progress,
			output,
			expectedResultType: "publish_result",
			parseResult: payload =>
			{
				var resultAppId = ReadUInt32(payload, "appId") ?? appId;
				var resultPublishedId = ReadUInt64(payload, "publishedFileId") ?? publishedFileId;
				if (resultPublishedId == 0)
				{
					throw new InvalidDataException("SteamPipe returned PublishedFileId=0.");
				}

				return new SteamPipePublishResult(resultAppId, resultPublishedId);
			},
			onEvent: (type, payload) =>
			{
				if (type == "publish_progress")
				{
					var processed = ReadUInt64(payload, "bytesProcessed") ?? 0;
					var total = ReadUInt64(payload, "bytesTotal") ?? 0;
					var status = ReadString(payload, "status");

					progress?.Report(new StunstickProgress(
						Operation: "Workshop Publish (Steamworks)",
						CompletedBytes: ClampToInt64(processed),
						TotalBytes: ClampToInt64(total),
						Message: string.IsNullOrWhiteSpace(status) ? "Uploading via Steamworks..." : $"Uploading via Steamworks... ({status})"));
				}
			},
			cancellationToken);
	}

	public static Task DeleteAsync(
		uint appId,
		ulong publishedFileId,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		CancellationToken cancellationToken)
	{
		var args = new List<string>
		{
			"delete",
			"--appid", appId.ToString(),
			"--published-id", publishedFileId.ToString()
		};

		return RunAndParseAsync(
			args,
			steamPipePath,
			progress,
			output,
			expectedResultType: "delete_result",
			parseResult: _ => true,
			onEvent: (_, _) => { },
			cancellationToken);
	}

	public static Task<SteamPipeListResult> ListPublishedAsync(
		uint appId,
		uint page,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		CancellationToken cancellationToken)
	{
		if (page == 0)
		{
			throw new ArgumentOutOfRangeException(nameof(page), "Page must be >= 1.");
		}

		var args = new List<string>
		{
			"list",
			"--appid", appId.ToString(),
			"--page", page.ToString()
		};

		return RunAndParseAsync(
			args,
			steamPipePath,
			progress,
			output,
			expectedResultType: "list_result",
			parseResult: payload =>
			{
				var resultAppId = ReadUInt32(payload, "appId") ?? appId;
				var resultPage = ReadUInt32(payload, "page") ?? page;
				var returned = ReadUInt32(payload, "returned") ?? 0;
				var totalMatching = ReadUInt32(payload, "totalMatching") ?? 0;

				var items = new List<SteamPipePublishedItem>();
				if (payload.TryGetProperty("items", out var itemsArray) && itemsArray.ValueKind == JsonValueKind.Array)
				{
					foreach (var element in itemsArray.EnumerateArray())
					{
						var id = ReadUInt64(element, "publishedFileId") ?? 0;
						if (id == 0)
						{
							continue;
						}

						var title = ReadString(element, "title");
						var description = ReadString(element, "description");

						var createdAt = ReadUInt64(element, "createdAt");
						var updatedAt = ReadUInt64(element, "updatedAt");

						var createdAtUtc = createdAt is ulong createdSeconds and > 0
							? DateTimeOffset.FromUnixTimeSeconds(ClampToInt64(createdSeconds)).ToUniversalTime()
							: (DateTimeOffset?)null;

						var updatedAtUtc = updatedAt is ulong updatedSeconds and > 0
							? DateTimeOffset.FromUnixTimeSeconds(ClampToInt64(updatedSeconds)).ToUniversalTime()
							: (DateTimeOffset?)null;

						var visibility = ReadString(element, "visibility");
						var parsedVisibility = ParseVisibility(visibility);

						var tags = new List<string>();
						if (element.TryGetProperty("tags", out var tagsArray) && tagsArray.ValueKind == JsonValueKind.Array)
						{
							foreach (var tagElement in tagsArray.EnumerateArray())
							{
								if (tagElement.ValueKind == JsonValueKind.String)
								{
									var tag = tagElement.GetString();
									if (!string.IsNullOrWhiteSpace(tag))
									{
										tags.Add(tag.Trim());
									}
								}
							}
						}

						items.Add(new SteamPipePublishedItem(
							PublishedFileId: id,
							Title: title,
							Description: description,
							CreatedAtUtc: createdAtUtc,
							UpdatedAtUtc: updatedAtUtc,
							Visibility: parsedVisibility,
							Tags: tags));
					}
				}

				return new SteamPipeListResult(resultAppId, resultPage, returned, totalMatching, items);
			},
			onEvent: (_, _) => { },
			cancellationToken);
	}

	public static Task<SteamPipeQuotaResult> GetQuotaAsync(
		uint appId,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		CancellationToken cancellationToken)
	{
		var args = new List<string>
		{
			"quota",
			"--appid", appId.ToString()
		};

		return RunAndParseAsync(
			args,
			steamPipePath,
			progress,
			output,
			expectedResultType: "quota_result",
			parseResult: payload =>
			{
				var resultAppId = ReadUInt32(payload, "appId") ?? appId;
				var totalBytes = ReadUInt64(payload, "totalBytes") ?? 0;
				var availableBytes = ReadUInt64(payload, "availableBytes") ?? 0;
				var usedBytes = ReadUInt64(payload, "usedBytes") ?? (totalBytes >= availableBytes ? totalBytes - availableBytes : 0);
				return new SteamPipeQuotaResult(resultAppId, totalBytes, availableBytes, usedBytes);
			},
			onEvent: (_, _) => { },
			cancellationToken);
	}

	private static async Task<T> RunAndParseAsync<T>(
		IReadOnlyList<string> steamPipeArgs,
		string? steamPipePath,
		IProgress<StunstickProgress>? progress,
		IProgress<string>? output,
		string expectedResultType,
		Func<JsonElement, T> parseResult,
		Action<string, JsonElement> onEvent,
		CancellationToken cancellationToken)
	{
		if (steamPipeArgs is null || steamPipeArgs.Count == 0)
		{
			throw new ArgumentException("SteamPipe args are required.", nameof(steamPipeArgs));
		}

		var (fileName, args) = ResolveSteamPipeCommand(steamPipePath, steamPipeArgs);

		if (output is not null)
		{
			var resolved = string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) && args.Count > 0
				? args[0]
				: fileName;
			output.Report($"SteamPipe: using {resolved}");
		}

		var startInfo = new ProcessStartInfo
		{
			FileName = fileName,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		foreach (var arg in args)
		{
			startInfo.ArgumentList.Add(arg);
		}

		using var process = new Process { StartInfo = startInfo };
		if (!process.Start())
		{
			throw new InvalidOperationException("Failed to start SteamPipe helper process.");
		}

		using var cancellationRegistration = cancellationToken.Register(() =>
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
		});

		var resultTcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

		async Task ReadStdoutAsync()
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
				if (line is null)
				{
					break;
				}

				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				if (!TryParseJson(line, out var json) || json is null)
				{
					output?.Report(line);
					continue;
				}

				using (json)
				{
					var root = json.RootElement;
					var type = ReadString(root, "type");
					if (string.IsNullOrWhiteSpace(type))
					{
						output?.Report(line);
						continue;
					}

					if (string.Equals(type, "log", StringComparison.Ordinal))
					{
						var message = ReadString(root, "message");
						if (!string.IsNullOrWhiteSpace(message))
						{
							output?.Report(message);
						}
						continue;
					}

					if (string.Equals(type, "error", StringComparison.Ordinal))
					{
						var message = ReadString(root, "message");
						resultTcs.TrySetException(new InvalidDataException(string.IsNullOrWhiteSpace(message) ? "SteamPipe error." : message));
						return;
					}

					if (string.Equals(type, expectedResultType, StringComparison.Ordinal))
					{
						resultTcs.TrySetResult(parseResult(root));
						return;
					}

					onEvent(type, root);
				}
			}
		}

		async Task ReadStderrAsync()
		{
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
				if (line is null)
				{
					break;
				}

				if (!string.IsNullOrWhiteSpace(line))
				{
					output?.Report(line);
				}
			}
		}

		var stdoutTask = ReadStdoutAsync();
		var stderrTask = ReadStderrAsync();

		await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdoutTask, stderrTask).ConfigureAwait(false);

		if (resultTcs.Task.IsCompleted)
		{
			return await resultTcs.Task.ConfigureAwait(false);
		}

		if (process.ExitCode != 0)
		{
			throw new InvalidDataException($"SteamPipe failed with exit code {process.ExitCode}.");
		}

		throw new InvalidDataException("SteamPipe did not return a result.");
	}

	private static (string FileName, IReadOnlyList<string> Args) ResolveSteamPipeCommand(string? overridePath, IReadOnlyList<string> steamPipeArgs)
	{
		var candidate = ResolveSteamPipePath(overridePath) ?? ResolveSteamPipePath(AppContext.BaseDirectory);
		if (candidate is null)
		{
			throw new FileNotFoundException("SteamPipe helper not found. Build Stunstick.SteamPipe and place it next to the Desktop/CLI executable (or pass an explicit path).");
		}

		if (candidate.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
		{
			var args = new List<string>(steamPipeArgs.Count + 1) { candidate };
			args.AddRange(steamPipeArgs);
			return ("dotnet", args);
		}

		return (candidate, steamPipeArgs);
	}

	private static string? ResolveSteamPipePath(string? pathOrDirectory)
	{
		if (string.IsNullOrWhiteSpace(pathOrDirectory))
		{
			return null;
		}

		var full = Path.GetFullPath(pathOrDirectory);

		if (File.Exists(full))
		{
			return full;
		}

		if (!Directory.Exists(full))
		{
			return null;
		}

		var candidates = OperatingSystem.IsWindows()
			? new[]
			{
				"Stunstick.SteamPipe.exe",
				"Stunstick.SteamPipe.dll",
				"Stunstick.SteamPipe"
			}
			: new[]
			{
				"Stunstick.SteamPipe",
				"Stunstick.SteamPipe.dll",
				"Stunstick.SteamPipe.exe"
			};

		foreach (var fileName in candidates)
		{
			var candidate = Path.Combine(full, fileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static bool TryParseJson(string input, out JsonDocument? document)
	{
		document = null;
		try
		{
			document = JsonDocument.Parse(input);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string? ReadString(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if (!element.TryGetProperty(propertyName, out var prop))
		{
			return null;
		}

		return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
	}

	private static uint? ReadUInt32(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
		{
			return null;
		}

		if (prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt32(out var value))
		{
			return value;
		}

		return uint.TryParse(prop.ToString(), out var parsed) ? parsed : null;
	}

	private static ulong? ReadUInt64(JsonElement element, string propertyName)
	{
		if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var prop))
		{
			return null;
		}

		if (prop.ValueKind == JsonValueKind.Number && prop.TryGetUInt64(out var value))
		{
			return value;
		}

		return ulong.TryParse(prop.ToString(), out var parsed) ? parsed : null;
	}

	private static string VisibilityToCliValue(WorkshopPublishVisibility visibility)
	{
		return visibility switch
		{
			WorkshopPublishVisibility.Public => "public",
			WorkshopPublishVisibility.FriendsOnly => "friends",
			WorkshopPublishVisibility.Private => "private",
			WorkshopPublishVisibility.Unlisted => "unlisted",
			_ => "public"
		};
	}

	private static WorkshopPublishVisibility? ParseVisibility(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var normalized = value.Trim();
		if (normalized.Contains("Public", StringComparison.OrdinalIgnoreCase))
		{
			return WorkshopPublishVisibility.Public;
		}
		if (normalized.Contains("Friends", StringComparison.OrdinalIgnoreCase))
		{
			return WorkshopPublishVisibility.FriendsOnly;
		}
		if (normalized.Contains("Unlisted", StringComparison.OrdinalIgnoreCase))
		{
			return WorkshopPublishVisibility.Unlisted;
		}
		if (normalized.Contains("Private", StringComparison.OrdinalIgnoreCase))
		{
			return WorkshopPublishVisibility.Private;
		}

		return null;
	}

	private static long ClampToInt64(ulong value)
	{
		return value > (ulong)long.MaxValue ? long.MaxValue : (long)value;
	}
}
