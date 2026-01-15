namespace Stunstick.Core.Steam;

public sealed record SteamAppInstall(
	uint AppId,
	string Name,
	string InstallDir,
	string LibraryRoot,
	string GameDirectory
);

