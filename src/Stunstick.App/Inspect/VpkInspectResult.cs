namespace Stunstick.App.Inspect;

public sealed record VpkInspectResult(
	string InputPath,
	string DirectoryFilePath,
	uint Signature,
	uint Version,
	uint DirectoryTreeSize,
	uint FileDataSectionSize,
	uint ArchiveMd5SectionSize,
	uint OtherMd5SectionSize,
	uint SignatureSectionSize,
	int EntryCount,
	long TotalEntryBytes,
	int ArchiveCount,
	bool HasV2Metadata,
	bool HasSignatureSection
);

