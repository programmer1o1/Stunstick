using Stunstick.App.Unpack;
using Stunstick.Core.Vpk;
using System.Text;

namespace Stunstick.App.Inspect;

internal static class VpkInspector
{
	public static VpkInspectResult Inspect(string packagePath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("Path is required.", nameof(packagePath));
		}

		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("File not found.", packagePath);
		}

		cancellationToken.ThrowIfCancellationRequested();

		var directoryFilePath = VpkUnpacker.ResolveDirectoryFilePath(packagePath);
		using var directoryStream = new FileStream(directoryFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

		var directoryFile = VpkDirectoryFileReader.Read(directoryStream, textEncoding: Encoding.Default);
		var header = directoryFile.Directory.Header;

		var totalEntryBytes = directoryFile.Directory.Entries.Sum(entry => (long)entry.PreloadBytes + (long)entry.EntryLength);
		var archiveCount = directoryFile.Directory.Entries
			.Select(entry => entry.ArchiveIndex)
			.Where(index => index != VpkConstants.DirectoryArchiveIndex)
			.Distinct()
			.Count();

		return new VpkInspectResult(
			InputPath: Path.GetFullPath(packagePath),
			DirectoryFilePath: Path.GetFullPath(directoryFilePath),
			Signature: header.Signature,
			Version: header.Version,
			DirectoryTreeSize: header.DirectoryTreeSize,
			FileDataSectionSize: header.FileDataSectionSize,
			ArchiveMd5SectionSize: header.ArchiveMd5SectionSize,
			OtherMd5SectionSize: header.OtherMd5SectionSize,
			SignatureSectionSize: header.SignatureSectionSize,
			EntryCount: directoryFile.Directory.Entries.Count,
			TotalEntryBytes: totalEntryBytes,
			ArchiveCount: archiveCount,
			HasV2Metadata: directoryFile.V2Metadata is not null,
			HasSignatureSection: directoryFile.V2Metadata?.SignatureSection is not null);
	}
}

