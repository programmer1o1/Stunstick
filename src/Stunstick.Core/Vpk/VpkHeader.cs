namespace Stunstick.Core.Vpk;

public sealed record VpkHeader(
	uint Signature,
	uint Version,
	uint DirectoryTreeSize,
	uint FileDataSectionSize,
	uint ArchiveMd5SectionSize,
	uint OtherMd5SectionSize,
	uint SignatureSectionSize
);

