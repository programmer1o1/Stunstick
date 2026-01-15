namespace Stunstick.Core.Vpk;

public sealed record VpkEntry(
	string RelativePath,
	uint Crc32,
	ushort PreloadBytes,
	ushort ArchiveIndex,
	uint EntryOffset,
	uint EntryLength,
	long PreloadDataOffset
);

