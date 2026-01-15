using Stunstick.Core.Steam;

namespace Stunstick.App.Workshop;

internal static class WorkshopCacheLocator
{
	public readonly record struct WorkshopCacheHit(uint AppId, string ContentDirectory);

	public static string? FindContentDirectory(string steamRoot, uint appId, ulong publishedFileId)
	{
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			return null;
		}

		foreach (var libraryRoot in SteamLibraryScanner.GetLibraryRoots(steamRoot))
		{
			var candidate = Path.Combine(
				libraryRoot,
				"steamapps",
				"workshop",
				"content",
				appId.ToString(),
				publishedFileId.ToString());

			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	public static WorkshopCacheHit? FindContentDirectoryAnyApp(string steamRoot, ulong publishedFileId)
	{
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			return null;
		}

		foreach (var libraryRoot in SteamLibraryScanner.GetLibraryRoots(steamRoot))
		{
			var contentRoot = Path.Combine(libraryRoot, "steamapps", "workshop", "content");
			if (!Directory.Exists(contentRoot))
			{
				continue;
			}

			IEnumerable<string> appDirs;
			try
			{
				appDirs = Directory.EnumerateDirectories(contentRoot, "*", SearchOption.TopDirectoryOnly);
			}
			catch
			{
				continue;
			}

			foreach (var appDir in appDirs)
			{
				var appIdText = Path.GetFileName(appDir);
				if (!uint.TryParse(appIdText, out var appId) || appId == 0)
				{
					continue;
				}

				var candidate = Path.Combine(appDir, publishedFileId.ToString());
				if (Directory.Exists(candidate))
				{
					return new WorkshopCacheHit(appId, candidate);
				}
			}
		}

		return null;
	}
}
