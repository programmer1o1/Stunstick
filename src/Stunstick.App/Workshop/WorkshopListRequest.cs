using Stunstick.App.Progress;

namespace Stunstick.App.Workshop;

public sealed record WorkshopListRequest(
	uint AppId,
	uint Page = 1,
	IProgress<StunstickProgress>? Progress = null,
	IProgress<string>? Output = null,
	string? SteamPipePath = null
);

