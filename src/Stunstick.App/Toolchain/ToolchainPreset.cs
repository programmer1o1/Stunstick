namespace Stunstick.App.Toolchain;

public sealed record ToolchainPreset(
	uint AppId,
	string Name,
	ToolchainGameEngine GameEngine,
	string GameDirectory,
	string? StudioMdlPath,
	string? HlmvPath,
	string? HammerPath,
	string? PackerToolPath,
	string? VpkToolPath,
	string? GmadPath,
	string? GoldSrcStudioMdlPath,
	string? Source2StudioMdlPath
);
