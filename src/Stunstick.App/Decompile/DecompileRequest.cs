namespace Stunstick.App.Decompile;

public sealed record DecompileRequest(
	string MdlPath,
	string OutputDirectory,
	DecompileOptions? Options = null
);
