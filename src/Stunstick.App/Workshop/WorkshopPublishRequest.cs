using Stunstick.App.Progress;

namespace Stunstick.App.Workshop;

public sealed record WorkshopPublishRequest(
	uint AppId,
	string ContentFolder,
	string PreviewFile,
	string Title,
	string Description,
	string ChangeNote,
	ulong PublishedFileId = 0,
	WorkshopPublishVisibility Visibility = WorkshopPublishVisibility.Public,
	string? VdfPath = null,
	string? SteamCmdPath = null,
	string? SteamCmdUsername = null,
	IReadOnlyList<string>? Tags = null,
	string? ContentType = null,
	IReadOnlyList<string>? ContentTags = null,
	IProgress<StunstickProgress>? Progress = null,
	IProgress<string>? Output = null,
	Func<SteamCmdPrompt, CancellationToken, Task<string?>>? SteamCmdPromptAsync = null,
	bool UseSteamworks = false,
	string? SteamPipePath = null,
	bool PackToVpkBeforeUpload = false,
	bool StageCleanPayload = false,
	uint VpkVersion = 1,
	bool VpkIncludeMd5Sections = false,
	bool VpkMultiFile = false
);
