namespace Stunstick.App.Toolchain;

public sealed record StudioMdlCompileRequest(
	string? StudioMdlPath,
	string QcPath,
	string? GameDirectory = null,
	uint? SteamAppId = null,
	string? SteamRoot = null,
	WineOptions? WineOptions = null,
	bool NoP4 = true,
	bool Verbose = true,
	bool DefineBones = false,
	bool DefineBonesCreateQciFile = false,
	string? DefineBonesQciFileName = "DefineBones",
	bool DefineBonesOverwriteQciFile = false,
	bool DefineBonesModifyQcFile = false,
	string? DirectOptions = null,
	bool WriteLogFile = false,
	System.IProgress<string>? Output = null
);
