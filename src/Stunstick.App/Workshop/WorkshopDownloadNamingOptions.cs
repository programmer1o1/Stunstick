namespace Stunstick.App.Workshop;

public sealed record WorkshopDownloadNamingOptions(
	bool IncludeTitle = false,
	bool IncludeId = true,
	bool AppendUpdatedTimestamp = false,
	bool ReplaceSpacesWithUnderscores = true
);

