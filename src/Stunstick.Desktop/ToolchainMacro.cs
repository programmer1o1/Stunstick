namespace Stunstick.Desktop;

internal sealed class ToolchainMacro
{
	public string Name { get; set; } = string.Empty;
	public string? Path { get; set; }

	public override string ToString()
	{
		return string.IsNullOrWhiteSpace(Path)
			? Name
			: $"{Name} = {Path}";
	}
}
