namespace Stunstick.App.Toolchain;

public sealed record HlmvViewRequest(
	string? HlmvPath,
	string MdlPath,
	string? GameDirectory = null,
	uint? SteamAppId = null,
	string? SteamRoot = null,
	WineOptions? WineOptions = null,
	bool ViewAsReplacement = false
);
