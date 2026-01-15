using Stunstick.App.Toolchain;

namespace Stunstick.Desktop;

internal sealed class ToolchainPresetOverrides
{
	public uint AppId { get; set; }
	public ToolchainGameEngine? GameEngine { get; set; }
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
