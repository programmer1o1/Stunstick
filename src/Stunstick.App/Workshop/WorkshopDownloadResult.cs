namespace Stunstick.App.Workshop;

public sealed record WorkshopDownloadResult(
	ulong PublishedFileId,
	uint AppId,
	string SourcePath,
	string OutputPath,
	WorkshopDownloadOutputType OutputType,
	WorkshopItemDetails? Details
);

