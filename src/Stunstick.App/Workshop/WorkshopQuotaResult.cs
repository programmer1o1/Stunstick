namespace Stunstick.App.Workshop;

public sealed record WorkshopQuotaResult(
	uint AppId,
	ulong TotalBytes,
	ulong AvailableBytes,
	ulong UsedBytes
);

