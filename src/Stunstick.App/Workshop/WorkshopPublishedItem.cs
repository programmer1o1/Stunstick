namespace Stunstick.App.Workshop;

public sealed record WorkshopPublishedItem(
	ulong PublishedFileId,
	string? Title,
	string? Description,
	DateTimeOffset? CreatedAtUtc,
	DateTimeOffset? UpdatedAtUtc,
	WorkshopPublishVisibility? Visibility,
	IReadOnlyList<string> Tags
);

