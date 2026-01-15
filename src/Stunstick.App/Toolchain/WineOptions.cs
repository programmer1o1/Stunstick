namespace Stunstick.App.Toolchain;

public sealed record WineOptions(
	bool Enabled = true,
	string? Prefix = null,
	string WineCommand = "wine"
);

