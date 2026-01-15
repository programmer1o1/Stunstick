namespace Stunstick.App.Unpack;

public sealed record PackageEntry(
	string RelativePath,
	long SizeBytes,
	uint? Crc32 = null
);

