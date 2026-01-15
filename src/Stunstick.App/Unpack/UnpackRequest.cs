using Stunstick.App.Progress;

namespace Stunstick.App.Unpack;

public sealed record UnpackRequest(
	string PackagePath,
	string OutputDirectory,
	bool VerifyCrc32 = false,
	bool VerifyMd5 = false,
	bool KeepFullPath = true,
	IProgress<StunstickProgress>? Progress = null,
	IReadOnlyList<string>? OnlyPaths = null,
	bool WriteLogFile = false
);
