namespace Stunstick.App.Toolchain;

public sealed record ProcessLaunchRequest(
	string FileName,
	IReadOnlyList<string> Arguments,
	string? WorkingDirectory = null,
	IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
	bool WaitForExit = true,
	System.IProgress<string>? StandardOutput = null,
	System.IProgress<string>? StandardError = null
);
