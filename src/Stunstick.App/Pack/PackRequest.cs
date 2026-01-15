using Stunstick.App.Progress;
using Stunstick.App.Toolchain;

namespace Stunstick.App.Pack;

public sealed record PackRequest(
	string InputDirectory,
	string OutputPackagePath,
	bool MultiFile = false,
	long? MaxArchiveSizeBytes = null,
	int PreloadBytes = 0,
	uint VpkVersion = 1,
	bool IncludeMd5Sections = false,
	byte[]? VpkSignaturePublicKey = null,
	byte[]? VpkSignature = null,
	string? GameDirectory = null,
	uint? SteamAppId = null,
	string? SteamRoot = null,
	string? GmadPath = null,
	string? VpkToolPath = null,
	WineOptions? WineOptions = null,
	IProgress<StunstickProgress>? Progress = null,
	bool WriteLogFile = false,
	string? DirectOptions = null,
	bool IgnoreWhitelistWarnings = false
);
