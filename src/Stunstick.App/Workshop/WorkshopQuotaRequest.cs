using Stunstick.App.Progress;

namespace Stunstick.App.Workshop;

public sealed record WorkshopQuotaRequest(
	uint AppId,
	IProgress<StunstickProgress>? Progress = null,
	IProgress<string>? Output = null,
	string? SteamPipePath = null
);

