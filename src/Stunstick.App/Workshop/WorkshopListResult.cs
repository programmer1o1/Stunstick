namespace Stunstick.App.Workshop;

public sealed record WorkshopListResult(
	uint AppId,
	uint Page,
	uint Returned,
	uint TotalMatching,
	IReadOnlyList<WorkshopPublishedItem> Items
);

