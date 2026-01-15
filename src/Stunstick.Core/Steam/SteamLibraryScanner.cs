namespace Stunstick.Core.Steam;

public static class SteamLibraryScanner
{
	public static IReadOnlyList<string> GetLibraryRoots(string steamRoot)
	{
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			throw new ArgumentException("Steam root is required.", nameof(steamRoot));
		}

		var roots = new List<string>();
		var steamRootFullPath = Path.GetFullPath(steamRoot);
		roots.Add(steamRootFullPath);

		var libraryFoldersPath = Path.Combine(steamRootFullPath, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(libraryFoldersPath))
		{
			return roots;
		}

		VdfObject root;
		try
		{
			root = VdfParser.ParseFile(libraryFoldersPath);
		}
		catch
		{
			return roots;
		}

		if (!root.TryGetObject("libraryfolders", out var libraryFolders))
		{
			return roots;
		}

		foreach (var (key, value) in libraryFolders.Properties)
		{
			_ = key;
			if (value is VdfString s)
			{
				AddIfValid(roots, s.Value);
				continue;
			}

			if (value is VdfObject obj && obj.TryGetString("path", out var path) && !string.IsNullOrWhiteSpace(path))
			{
				AddIfValid(roots, path);
			}
		}

		return roots;
	}

	public static IReadOnlyList<SteamAppInstall> GetInstalledApps(string steamRoot)
	{
		var installs = new List<SteamAppInstall>();

		foreach (var libraryRoot in GetLibraryRoots(steamRoot))
		{
			var steamAppsDirectory = Path.Combine(libraryRoot, "steamapps");
			if (!Directory.Exists(steamAppsDirectory))
			{
				continue;
			}

			foreach (var manifestPath in Directory.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
			{
				var install = TryReadAppManifest(manifestPath, libraryRoot);
				if (install is not null)
				{
					installs.Add(install);
				}
			}
		}

		// Some Steam installs can end up with duplicate manifests (e.g. after moving games between libraries).
		// Prefer entries whose game directory actually exists.
		var bestByAppId = new Dictionary<uint, SteamAppInstall>();
		foreach (var install in installs)
		{
			if (!bestByAppId.TryGetValue(install.AppId, out var existing))
			{
				bestByAppId[install.AppId] = install;
				continue;
			}

			var existingExists = Directory.Exists(existing.GameDirectory);
			var candidateExists = Directory.Exists(install.GameDirectory);

			if (candidateExists && !existingExists)
			{
				bestByAppId[install.AppId] = install;
			}
		}

		return bestByAppId.Values.ToArray();
	}

	public static SteamAppInstall? FindInstalledApp(string steamRoot, uint appId)
	{
		return GetInstalledApps(steamRoot).FirstOrDefault(install => install.AppId == appId);
	}

	private static SteamAppInstall? TryReadAppManifest(string manifestPath, string libraryRoot)
	{
		VdfObject root;
		try
		{
			root = VdfParser.ParseFile(manifestPath);
		}
		catch
		{
			return null;
		}

		if (!root.TryGetObject("AppState", out var appState))
		{
			return null;
		}

		if (!appState.TryGetString("appid", out var appIdText) || !uint.TryParse(appIdText, out var appId))
		{
			return null;
		}

		_ = appState.TryGetString("name", out var name);
		name ??= string.Empty;

		if (!appState.TryGetString("installdir", out var installDir) || string.IsNullOrWhiteSpace(installDir))
		{
			return null;
		}

		var gameDirectory = Path.Combine(libraryRoot, "steamapps", "common", installDir);
		return new SteamAppInstall(
			AppId: appId,
			Name: name,
			InstallDir: installDir,
			LibraryRoot: libraryRoot,
			GameDirectory: gameDirectory);
	}

	private static void AddIfValid(List<string> roots, string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate))
		{
			return;
		}

		var fullPath = Path.GetFullPath(candidate);
		if (Directory.Exists(fullPath) && !roots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
		{
			roots.Add(fullPath);
		}
	}
}
