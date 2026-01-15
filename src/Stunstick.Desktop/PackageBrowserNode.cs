using System.Collections.ObjectModel;

namespace Stunstick.Desktop;

internal sealed class PackageBrowserNode
{
	public string Name { get; }
	public string RelativePath { get; }
	public bool IsDirectory { get; }
	public long SizeBytes { get; }
	public uint? Crc32 { get; }

	public ObservableCollection<PackageBrowserNode> Children { get; } = new();

	public PackageBrowserNode(string name, string relativePath, bool isDirectory, long sizeBytes = 0, uint? crc32 = null)
	{
		Name = name;
		RelativePath = relativePath;
		IsDirectory = isDirectory;
		SizeBytes = sizeBytes;
		Crc32 = crc32;
	}

	public override string ToString()
	{
		return Name;
	}
}

