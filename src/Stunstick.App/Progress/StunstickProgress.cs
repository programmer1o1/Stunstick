namespace Stunstick.App.Progress;

public sealed record StunstickProgress(
	string Operation,
	long CompletedBytes,
	long TotalBytes,
	string? CurrentItem = null,
	string? Message = null
);

