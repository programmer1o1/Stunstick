namespace Stunstick.Core.Inspect;

public sealed record StunstickInspectResult(
	string Path,
	long SizeBytes,
	Stunstick.Core.StunstickFileType FileType,
	string? Sha256Hex
);

