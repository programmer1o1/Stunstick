namespace Stunstick.Core.Steam;

public static class SteamInstallLocator
{
	public static string? FindSteamRoot(string? steamRootOverride = null)
	{
		if (!string.IsNullOrWhiteSpace(steamRootOverride))
		{
			var fullPath = Path.GetFullPath(steamRootOverride);
			return Directory.Exists(fullPath) ? fullPath : null;
		}

		foreach (var candidate in GetDefaultSteamRoots())
		{
			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	public static IReadOnlyList<string> GetDefaultSteamRoots()
	{
		var roots = new List<string>();

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (OperatingSystem.IsWindows())
		{
			var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			if (!string.IsNullOrWhiteSpace(programFilesX86))
			{
				roots.Add(Path.Combine(programFilesX86, "Steam"));
			}

			var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrWhiteSpace(programFiles))
			{
				roots.Add(Path.Combine(programFiles, "Steam"));
			}
		}
		else if (OperatingSystem.IsMacOS())
		{
			roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
		}
		else
		{
			roots.Add(Path.Combine(home, ".steam", "steam"));
			roots.Add(Path.Combine(home, ".local", "share", "Steam"));
		}

		return roots;
	}
}

