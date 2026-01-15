using Stunstick.App.Toolchain;

namespace Stunstick.Desktop;

internal sealed class ToolchainCustomPreset
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public string? Name { get; set; }
	public ToolchainGameEngine GameEngine { get; set; } = ToolchainGameEngine.Source;
	public string? InstallDirectory { get; set; }
	public string? GameDirectory { get; set; }
	public string? StudioMdlPath { get; set; }
	public string? HlmvPath { get; set; }
	public string? HammerPath { get; set; }
	public string? PackerToolPath { get; set; }
	public string? VpkToolPath { get; set; }
	public string? GmadPath { get; set; }
	public string? GoldSrcStudioMdlPath { get; set; }
	public string? Source2StudioMdlPath { get; set; }
}
