namespace Stunstick.App.Workshop;

public enum SteamCmdPromptKind
{
	Unknown = 0,
	Password = 1,
	SteamGuardCode = 2
}

public sealed record SteamCmdPrompt(
	SteamCmdPromptKind Kind,
	string Message
);

