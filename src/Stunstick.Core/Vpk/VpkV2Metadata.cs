namespace Stunstick.Core.Vpk;

public sealed record VpkV2Metadata(
	long FileDataSectionSize,
	ReadOnlyMemory<byte> ArchiveMd5SectionBytes,
	IReadOnlyList<VpkArchiveMd5Entry> ArchiveMd5Entries,
	VpkOtherMd5? OtherMd5,
	ReadOnlyMemory<byte> SignatureSectionBytes,
	VpkSignatureSection? SignatureSection
);
