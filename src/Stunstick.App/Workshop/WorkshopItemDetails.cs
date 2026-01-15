namespace Stunstick.App.Workshop;

public sealed record WorkshopItemDetails(
	ulong PublishedFileId,
	string? Title,
	DateTimeOffset? UpdatedAtUtc,
	uint? ConsumerAppId,
	string? FileUrl,
	string? FileName,
	long? FileSizeBytes
);
