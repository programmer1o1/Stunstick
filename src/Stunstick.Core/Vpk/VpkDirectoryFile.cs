namespace Stunstick.Core.Vpk;

public sealed record VpkDirectoryFile(
	byte[] HeaderBytes,
	VpkDirectory Directory,
	ReadOnlyMemory<byte> DirectoryTreeBytes,
	VpkV2Metadata? V2Metadata
);

