namespace Stunstick.Core.Vpk;

public sealed record VpkDirectoryTreeEntry(
	string RelativePath,
	uint Crc32,
	ushort PreloadBytes,
	ushort ArchiveIndex,
	uint EntryOffset,
	uint EntryLength,
	ReadOnlyMemory<byte> PreloadData
);
