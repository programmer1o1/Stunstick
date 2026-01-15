using Stunstick.Core.Steam;

namespace Stunstick.App.Toolchain;

public static class ToolchainDiscovery
{
	private static readonly uint[] SourceSdkBase2013AppIds = { 243730u, 243750u };

	public static IReadOnlyList<ToolchainPreset> DiscoverSteamPresets(
		string? steamRootOverride = null,
		IReadOnlyList<string>? extraLibraryRoots = null)
	{
		var steamRoot = SteamInstallLocator.FindSteamRoot(steamRootOverride);
		var installs = new List<SteamAppInstall>();

		if (steamRoot is not null)
		{
			installs.AddRange(SteamLibraryScanner.GetInstalledApps(steamRoot));
		}

		if (extraLibraryRoots is not null)
		{
			foreach (var root in extraLibraryRoots)
			{
				if (string.IsNullOrWhiteSpace(root))
				{
					continue;
				}

				try
				{
					installs.AddRange(SteamLibraryScanner.GetInstalledApps(root));
				}
				catch
				{
				}
			}
		}

		if (installs.Count == 0)
		{
			return Array.Empty<ToolchainPreset>();
		}

		// Prefer entries whose game directory exists.
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

		var presets = new List<ToolchainPreset>(bestByAppId.Count);

		foreach (var install in bestByAppId.Values.OrderBy(install => install.Name, StringComparer.OrdinalIgnoreCase))
		{
			var engine = DetectEngine(install.GameDirectory);

			var studiomdl = FindStudioMdlPath(install.GameDirectory);
			var goldSrcStudioMdl = FindGoldSrcStudioMdlPath(install.GameDirectory);
			var source2StudioMdl = FindSource2StudioMdlPath(install.GameDirectory);
			var hlmv = FindHlmvPath(install.GameDirectory);
			var hammer = FindHammerPath(install.GameDirectory);
			var vpk = FindVpkPath(install.GameDirectory);
			var gmad = FindGmadPath(install.GameDirectory);
			var packerTool = engine switch
			{
				ToolchainGameEngine.Source2 => vpk ?? gmad,
				ToolchainGameEngine.GoldSrc => gmad ?? vpk,
				_ => vpk ?? gmad
			};

			presets.Add(new ToolchainPreset(
				AppId: install.AppId,
				Name: install.Name,
				GameEngine: engine,
				GameDirectory: install.GameDirectory,
				StudioMdlPath: studiomdl,
				HlmvPath: hlmv,
				HammerPath: hammer,
				PackerToolPath: packerTool,
				VpkToolPath: vpk,
				GmadPath: gmad,
				GoldSrcStudioMdlPath: goldSrcStudioMdl,
				Source2StudioMdlPath: source2StudioMdl));
		}

		return presets;
	}

	public static ToolchainPreset? FindSteamPreset(
		uint appId,
		string? steamRootOverride = null,
		IReadOnlyList<string>? extraLibraryRoots = null)
	{
		return DiscoverSteamPresets(steamRootOverride, extraLibraryRoots)
			.FirstOrDefault(p => p.AppId == appId);
	}

	public static string? FindStudioMdlPath(string gameDirectory)
	{
		var candidates = OperatingSystem.IsWindows()
			? new[]
			{
				"bin/studiomdl.exe",
				"bin/win32/studiomdl.exe",
				"bin/x64/studiomdl.exe",
				"bin/studiomdl"
			}
			: OperatingSystem.IsMacOS()
				? new[]
				{
					"bin/studiomdl_osx",
					"bin/osx32/studiomdl",
					"bin/osx64/studiomdl",
					"bin/studiomdl",
					"bin/studiomdl.exe",
					"bin/win32/studiomdl.exe",
					"bin/x64/studiomdl.exe"
				}
				: new[]
				{
					"bin/studiomdl_linux",
					"bin/linux32/studiomdl",
					"bin/linux64/studiomdl",
					"bin/studiomdl",
					"bin/studiomdl.exe",
					"bin/win32/studiomdl.exe",
					"bin/x64/studiomdl.exe"
				};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindGoldSrcStudioMdlPath(string gameDirectory)
	{
		var candidates = new[]
		{
			"bin/studiomdl.exe",
			"studiomdl.exe",
			"studiomdl"
		};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindSource2StudioMdlPath(string gameDirectory)
	{
		var candidates = new[]
		{
			"game/bin/win64/resourcecompiler.exe",
			"game/bin/win64/resourcecompiler",
			"resourcecompiler.exe",
			"resourcecompiler"
		};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindStudioMdlPathWithSteamHints(string? steamRootOverride = null)
	{
		foreach (var appId in SourceSdkBase2013AppIds)
		{
			var preset = FindSteamPreset(appId, steamRootOverride);
			if (!string.IsNullOrWhiteSpace(preset?.StudioMdlPath))
			{
				return preset!.StudioMdlPath;
			}
		}

		return null;
	}

	public static string? FindStudioMdlExePathWithSteamHints(string? steamRootOverride = null)
	{
		foreach (var appId in SourceSdkBase2013AppIds)
		{
			var preset = FindSteamPreset(appId, steamRootOverride);
			if (!string.IsNullOrWhiteSpace(preset?.StudioMdlPath) &&
			    preset!.StudioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				return preset.StudioMdlPath;
			}
		}

		// Fallback: scan any installed app for a Windows studiomdl.exe.
		foreach (var preset in DiscoverSteamPresets(steamRootOverride))
		{
			if (!string.IsNullOrWhiteSpace(preset.StudioMdlPath) &&
			    preset.StudioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				return preset.StudioMdlPath;
			}
		}

		return null;
	}

	public static string? FindBundledStudioMdlPath(string? baseDirectory = null)
	{
		var baseDir = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
		if (string.IsNullOrWhiteSpace(baseDir))
		{
			return null;
		}

		var toolNames = OperatingSystem.IsWindows()
			? new[] { "studiomdl.exe", "studiomdl" }
			: OperatingSystem.IsMacOS()
				? new[] { "studiomdl", "studiomdl_osx", "studiomdl.exe" }
				: new[] { "studiomdl", "studiomdl_linux", "studiomdl.exe" };

		var rels = new List<string>();
		foreach (var toolName in toolNames)
		{
			rels.Add(toolName);
			rels.Add(Path.Combine("tools", "studiomdl", toolName));
			rels.Add(Path.Combine("tools", "studiomdl", "bin", toolName));
		}

		foreach (var toolName in toolNames)
		{
			rels.Add(Path.Combine("tools", "MDLForge", "build", "utils", "studiomdl", toolName));
			rels.Add(Path.Combine("tools", "MDLForge", "build", "utils", "studiomdl", "Release", toolName));
			rels.Add(Path.Combine("tools", "MDLForge", "build", "utils", "studiomdl", "Debug", toolName));
			rels.Add(Path.Combine("tools", "MDLForge", "build", "utils", "studiomdl", "RelWithDebInfo", toolName));
			rels.Add(Path.Combine("tools", "MDLForge", "build", "utils", "studiomdl", "MinSizeRel", toolName));
		}

		var candidate = FindFirstExistingFile(baseDir, rels);
		if (candidate is not null)
		{
			return candidate;
		}

		var repoRoot = TryFindRepoRoot(baseDir);
		if (repoRoot is null)
		{
			return null;
		}

		return FindFirstExistingFile(repoRoot, rels);
	}

	public static string? FindBundledStudioMdlPathLinuxI686(string? baseDirectory = null)
	{
		if (!OperatingSystem.IsLinux())
		{
			return null;
		}

		var baseDir = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
		if (string.IsNullOrWhiteSpace(baseDir))
		{
			return null;
		}

		var rels = new List<string>
		{
			"studiomdl_i686",
			Path.Combine("tools", "studiomdl", "studiomdl_i686"),
			Path.Combine("tools", "studiomdl", "bin", "studiomdl_i686"),
			Path.Combine("tools", "MDLForge", "build-i686", "utils", "studiomdl", "studiomdl")
		};

		var candidate = FindFirstExistingFile(baseDir, rels);
		if (candidate is not null)
		{
			return candidate;
		}

		var repoRoot = TryFindRepoRoot(baseDir);
		if (repoRoot is null)
		{
			return null;
		}

		return FindFirstExistingFile(repoRoot, rels);
	}

	public static string? FindHlmvPath(string gameDirectory)
	{
		var candidates = OperatingSystem.IsWindows()
			? new[]
			{
				"bin/hlmv.exe",
				"bin/win32/hlmv.exe",
				"bin/x64/hlmv.exe",
				"bin/hlmv"
			}
			: OperatingSystem.IsMacOS()
				? new[]
				{
					"bin/hlmv_osx",
					"bin/osx32/hlmv",
					"bin/osx64/hlmv",
					"bin/hlmv",
					"bin/hlmv.exe",
					"bin/win32/hlmv.exe",
					"bin/x64/hlmv.exe"
				}
				: new[]
				{
					"bin/hlmv_linux",
					"bin/linux32/hlmv",
					"bin/linux64/hlmv",
					"bin/hlmv",
					"bin/hlmv.exe",
					"bin/win32/hlmv.exe",
					"bin/x64/hlmv.exe"
				};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindHlmvPathWithSteamHints(string? steamRootOverride = null)
	{
		foreach (var appId in SourceSdkBase2013AppIds)
		{
			var preset = FindSteamPreset(appId, steamRootOverride);
			if (!string.IsNullOrWhiteSpace(preset?.HlmvPath))
			{
				return preset!.HlmvPath;
			}
		}

		return null;
	}

	public static string? FindHammerPath(string gameDirectory)
	{
		return FindFirstExistingFile(gameDirectory, new[]
		{
			"bin/hammer.exe",
			"bin/win32/hammer.exe",
			"bin/x64/hammer.exe",
			"bin/hammer"
		});
	}

	public static string? FindGmadPath(string gameDirectory)
	{
		var candidates = OperatingSystem.IsWindows()
			? new[]
			{
				"bin/gmad.exe",
				"bin/win32/gmad.exe",
				"bin/x64/gmad.exe",
				"bin/gmad"
			}
			: OperatingSystem.IsMacOS()
				? new[]
				{
					"bin/gmad_osx",
					"bin/gmad",
					"bin/gmad.exe",
					"bin/win32/gmad.exe",
					"bin/x64/gmad.exe"
				}
				: new[]
				{
					"bin/gmad_linux",
					"bin/gmad",
					"bin/gmad.exe",
					"bin/win32/gmad.exe",
					"bin/x64/gmad.exe"
				};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindVpkPath(string gameDirectory)
	{
		var candidates = OperatingSystem.IsWindows()
			? new[]
			{
				"bin/vpk.exe",
				"bin/win32/vpk.exe",
				"bin/x64/vpk.exe",
				"bin/vpk"
			}
			: OperatingSystem.IsMacOS()
				? new[]
				{
					"bin/vpk_osx",
					"bin/vpk",
					"bin/vpk.exe",
					"bin/win32/vpk.exe",
					"bin/x64/vpk.exe"
				}
				: new[]
				{
					"bin/vpk_linux",
					"bin/vpk",
					"bin/vpk.exe",
					"bin/win32/vpk.exe",
					"bin/x64/vpk.exe"
				};

		return FindFirstExistingFile(gameDirectory, candidates);
	}

	public static string? FindVpkPathWithSteamHints(string? steamRootOverride = null)
	{
		foreach (var appId in SourceSdkBase2013AppIds)
		{
			var preset = FindSteamPreset(appId, steamRootOverride);
			if (preset is null)
			{
				continue;
			}

			var vpk = FindVpkPath(preset.GameDirectory);
			if (!string.IsNullOrWhiteSpace(vpk))
			{
				return vpk;
			}
		}

		return null;
	}

	public static IReadOnlyList<string> FindGameDirectoryCandidates(string installDirectory)
	{
		if (string.IsNullOrWhiteSpace(installDirectory))
		{
			return Array.Empty<string>();
		}

		var installFullPath = Path.GetFullPath(installDirectory);
		if (!Directory.Exists(installFullPath))
		{
			return Array.Empty<string>();
		}

		var candidates = new List<string>();

		// If the provided folder is itself a mod/game folder, include it.
		if (File.Exists(Path.Combine(installFullPath, "gameinfo.txt")) ||
		    File.Exists(Path.Combine(installFullPath, "gameinfo.gi")) ||
		    File.Exists(Path.Combine(installFullPath, "liblist.gam")))
		{
			candidates.Add(installFullPath);
		}

		// Source 1 style: "<install>/<mod>/gameinfo.txt"
		foreach (var folder in Directory.EnumerateDirectories(installFullPath, "*", SearchOption.TopDirectoryOnly))
		{
			var gameInfo = Path.Combine(folder, "gameinfo.txt");
			if (File.Exists(gameInfo))
			{
				candidates.Add(folder);
			}
		}

		// Source 2 style: "<install>/game/<mod>/gameinfo.gi" (or "<install>/game/gameinfo.gi")
		var gameRoot = Path.Combine(installFullPath, "game");
		if (Directory.Exists(gameRoot))
		{
			var giAtRoot = Path.Combine(gameRoot, "gameinfo.gi");
			if (File.Exists(giAtRoot))
			{
				candidates.Add(gameRoot);
			}

			foreach (var folder in Directory.EnumerateDirectories(gameRoot, "*", SearchOption.TopDirectoryOnly))
			{
				var gi = Path.Combine(folder, "gameinfo.gi");
				if (File.Exists(gi))
				{
					candidates.Add(folder);
				}
			}
		}

		// GoldSrc style: "<install>/<mod>/liblist.gam"
		foreach (var folder in Directory.EnumerateDirectories(installFullPath, "*", SearchOption.TopDirectoryOnly))
		{
			var liblist = Path.Combine(folder, "liblist.gam");
			if (File.Exists(liblist))
			{
				candidates.Add(folder);
			}
		}

		var liblistAtRoot = Path.Combine(installFullPath, "liblist.gam");
		if (File.Exists(liblistAtRoot))
		{
			candidates.Add(installFullPath);
		}

		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

		return candidates
			.Distinct(comparer)
			.OrderBy(p => p, comparer)
			.ToArray();
	}

	public static string? FindPreferredGameDirectory(string installDirectory, uint appId)
	{
		if (string.IsNullOrWhiteSpace(installDirectory))
		{
			return null;
		}

		var candidates = FindGameDirectoryCandidates(installDirectory);
		if (candidates.Count == 0)
		{
			return installDirectory;
		}

		var preferredName = GetPreferredModFolderName(appId);
		if (!string.IsNullOrWhiteSpace(preferredName))
		{
			foreach (var candidate in candidates)
			{
				if (string.Equals(Path.GetFileName(candidate), preferredName, StringComparison.OrdinalIgnoreCase))
				{
					return candidate;
				}
			}
		}

		return candidates[0];
	}

	private static string? GetPreferredModFolderName(uint appId)
	{
		return appId switch
		{
			220u => "hl2",
			240u => "cstrike",
			340u => "lostcoast",
			380u => "episodic",
			400u => "portal",
			420u => "ep2",
			440u => "tf",
			4000u => "garrysmod",
			243730u => "hl2",
			243750u => "hl2mp",
			_ => null
		};
	}

	private static byte? TryReadElfClass(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			Span<byte> header = stackalloc byte[5];
			if (stream.Read(header) != 5)
			{
				return null;
			}

			// 0x7F 'E' 'L' 'F' then EI_CLASS (1=32-bit, 2=64-bit)
			if (header[0] != 0x7F ||
			    header[1] != (byte)'E' ||
			    header[2] != (byte)'L' ||
			    header[3] != (byte)'F')
			{
				return null;
			}

			var elfClass = header[4];
			return elfClass == 1 || elfClass == 2 ? elfClass : null;
		}
		catch
		{
			return null;
		}
	}

	public static string? FindVPhysicsBinDirectoryForTool(string studioMdlPath, string? gameDirectory, string? steamRootOverride = null)
	{
		if (!OperatingSystem.IsLinux())
		{
			return null;
		}

		var desiredElfClass = TryReadElfClass(studioMdlPath) ?? (Environment.Is64BitProcess ? (byte)2 : (byte)1);

		bool HasVPhysics(string binDir)
		{
			if (string.IsNullOrWhiteSpace(binDir) || !Directory.Exists(binDir))
			{
				return false;
			}

			var vphysics = Path.Combine(binDir, "vphysics.so");
			var tier0 = Path.Combine(binDir, "libtier0.so");
			return File.Exists(vphysics) &&
			       File.Exists(tier0) &&
			       TryReadElfClass(vphysics) == desiredElfClass &&
			       TryReadElfClass(tier0) == desiredElfClass;
		}

		static IEnumerable<string> GetCandidateBinDirs(string root)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				yield break;
			}

			var rootFullPath = Path.GetFullPath(root);

			yield return Path.Combine(rootFullPath, "bin", "linux64");
			yield return Path.Combine(rootFullPath, "bin", "linux32");
			yield return Path.Combine(rootFullPath, "bin");
		}

		var roots = new List<string>(capacity: 2);
		if (!string.IsNullOrWhiteSpace(gameDirectory))
		{
			var gameDirFull = Path.GetFullPath(gameDirectory);
			roots.Add(gameDirFull);

			try
			{
				var parent = Directory.GetParent(gameDirFull)?.FullName;
				if (!string.IsNullOrWhiteSpace(parent))
				{
					roots.Add(parent);
				}
			}
			catch
			{
			}
		}

		foreach (var root in roots.Distinct(StringComparer.Ordinal))
		{
			foreach (var binDir in GetCandidateBinDirs(root))
			{
				if (HasVPhysics(binDir))
				{
					return binDir;
				}
			}
		}

		var steamRoot = SteamInstallLocator.FindSteamRoot(steamRootOverride);
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			return null;
		}

		foreach (var install in SteamLibraryScanner.GetInstalledApps(steamRoot))
		{
			foreach (var binDir in GetCandidateBinDirs(install.GameDirectory))
			{
				if (HasVPhysics(binDir))
				{
					return binDir;
				}
			}
		}

		return null;
	}

	public static string? FindVPhysicsBinDirectory(string? gameDirectory, string? steamRootOverride = null)
	{
		// Back-compat overload: assume the tool bitness matches the current process.
		// Prefer FindVPhysicsBinDirectoryForTool when launching an external tool of a different bitness.
		return FindVPhysicsBinDirectoryForTool(Environment.ProcessPath ?? string.Empty, gameDirectory, steamRootOverride);
	}

	public static ToolchainGameEngine DetectEngine(string installDirectory)
	{
		if (string.IsNullOrWhiteSpace(installDirectory))
		{
			return ToolchainGameEngine.Unknown;
		}

		string installFullPath;
		try
		{
			installFullPath = Path.GetFullPath(installDirectory);
		}
		catch
		{
			installFullPath = installDirectory.Trim();
		}

		if (string.IsNullOrWhiteSpace(installFullPath) || !Directory.Exists(installFullPath))
		{
			return ToolchainGameEngine.Unknown;
		}

		bool Exists(string path) => File.Exists(path);

		// Source2: gameinfo.gi at root or under /game
		if (Exists(Path.Combine(installFullPath, "gameinfo.gi")) ||
		    Exists(Path.Combine(installFullPath, "game", "gameinfo.gi")))
		{
			return ToolchainGameEngine.Source2;
		}

		// Source 1: gameinfo.txt at root or direct subfolder
		if (Exists(Path.Combine(installFullPath, "gameinfo.txt")))
		{
			return ToolchainGameEngine.Source;
		}

		// GoldSrc: liblist.gam at root or direct subfolder
		if (Exists(Path.Combine(installFullPath, "liblist.gam")))
		{
			return ToolchainGameEngine.GoldSrc;
		}

		foreach (var folder in Directory.EnumerateDirectories(installFullPath, "*", SearchOption.TopDirectoryOnly))
		{
			if (Exists(Path.Combine(folder, "liblist.gam")))
			{
				return ToolchainGameEngine.GoldSrc;
			}

			if (Exists(Path.Combine(folder, "gameinfo.gi")))
			{
				return ToolchainGameEngine.Source2;
			}

			if (Exists(Path.Combine(folder, "gameinfo.txt")))
			{
				return ToolchainGameEngine.Source;
			}
		}

		// Source2: sometimes only exists under "<install>/game/<mod>/gameinfo.gi".
		var gameRoot = Path.Combine(installFullPath, "game");
		if (Directory.Exists(gameRoot))
		{
			foreach (var folder in Directory.EnumerateDirectories(gameRoot, "*", SearchOption.TopDirectoryOnly))
			{
				if (Exists(Path.Combine(folder, "gameinfo.gi")))
				{
					return ToolchainGameEngine.Source2;
				}
			}
		}

		return ToolchainGameEngine.Unknown;
	}

	private static string? TryFindRepoRoot(string startPath)
	{
		if (string.IsNullOrWhiteSpace(startPath))
		{
			return null;
		}

		DirectoryInfo? current = null;
		try
		{
			var full = Path.GetFullPath(startPath);
			current = new DirectoryInfo(File.Exists(full) ? (Path.GetDirectoryName(full) ?? full) : full);
		}
		catch
		{
			return null;
		}

		for (var i = 0; i < 10 && current is not null; i++)
		{
			var dir = current.FullName;

			if (File.Exists(Path.Combine(dir, "Stunstick.CrossPlatform.sln")) ||
			    File.Exists(Path.Combine(dir, "Stunstick.sln")) ||
			    File.Exists(Path.Combine(dir, "tools", "MDLForge", "CMakeLists.txt")))
			{
				return dir;
			}

			current = current.Parent;
		}

		return null;
	}

	private static string? FindFirstExistingFile(string root, IReadOnlyList<string> relativePaths)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			return null;
		}

		var rootFullPath = Path.GetFullPath(root);
		foreach (var relativePath in relativePaths)
		{
			var candidate = Path.Combine(rootFullPath, relativePath);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}
}
