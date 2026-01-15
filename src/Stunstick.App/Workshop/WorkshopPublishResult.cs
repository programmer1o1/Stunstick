namespace Stunstick.App.Workshop;

public sealed record WorkshopPublishResult(
	uint AppId,
	ulong PublishedFileId,
	string VdfPath
);

