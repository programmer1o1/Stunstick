using Stunstick.App.Progress;

namespace Stunstick.App.Workshop;

public sealed record WorkshopDeleteRequest(
	uint AppId,
	ulong PublishedFileId,
	string? SteamPipePath = null,
	IProgress<StunstickProgress>? Progress = null,
	IProgress<string>? Output = null
);

