namespace Stunstick.Core.Vpk;

public sealed record VpkOtherMd5(
	byte[] TreeChecksum,
	byte[] ArchiveMd5SectionChecksum,
	byte[] WholeFileChecksum
);

