using Stunstick.App.Progress;

namespace Stunstick.App.Workshop;

public sealed record WorkshopDownloadRequest(
	string IdOrLink,
	string OutputDirectory,
	uint AppId = 4000,
	string? SteamRoot = null,
	bool ConvertToExpectedFileOrFolder = true,
	bool FetchDetails = false,
	bool OverwriteExisting = false,
	WorkshopDownloadNamingOptions? NamingOptions = null,
	IProgress<StunstickProgress>? Progress = null,
	IProgress<string>? Output = null,
	bool UseSteamworksWhenNotCached = false,
	bool UseSteamCmdWhenNotCached = false,
	string? SteamCmdPath = null,
	string? SteamCmdInstallDirectory = null,
	string? SteamCmdUsername = null,
	Func<SteamCmdPrompt, CancellationToken, Task<string?>>? SteamCmdPromptAsync = null,
	string? SteamPipePath = null
);
