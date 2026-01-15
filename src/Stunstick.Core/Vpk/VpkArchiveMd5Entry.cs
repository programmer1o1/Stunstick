namespace Stunstick.Core.Vpk;

public sealed record VpkArchiveMd5Entry(
	uint ArchiveIndex,
	uint ArchiveOffset,
	uint Length,
	byte[] Md5
);

