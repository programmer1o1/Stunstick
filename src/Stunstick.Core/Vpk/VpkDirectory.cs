namespace Stunstick.Core.Vpk;

public sealed record VpkDirectory(
	VpkHeader Header,
	long DirectoryTreeOffset,
	IReadOnlyList<VpkEntry> Entries
)
{
	public long DataSectionOffset => DirectoryTreeOffset + Header.DirectoryTreeSize;
}

