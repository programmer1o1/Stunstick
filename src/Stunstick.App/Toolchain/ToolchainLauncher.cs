using Stunstick.App.Toolchain;
using Stunstick.Core.Steam;
using System.ComponentModel;

namespace Stunstick.App.Toolchain;

public sealed class ToolchainLauncher
{
	private readonly IProcessLauncher processLauncher;

	public ToolchainLauncher(IProcessLauncher processLauncher)
	{
		this.processLauncher = processLauncher;
	}

	public async Task<int> LaunchExternalToolAsync(
		string toolPath,
		IReadOnlyList<string> toolArguments,
		WineOptions wineOptions,
		string? steamRootOverride,
		bool waitForExit,
		CancellationToken cancellationToken,
		string? workingDirectory = null,
		IProgress<string>? standardOutput = null,
		IProgress<string>? standardError = null,
		IReadOnlyDictionary<string, string>? environmentVariables = null)
	{
		if (string.IsNullOrWhiteSpace(toolPath))
		{
			throw new ArgumentException("Tool path is required.", nameof(toolPath));
		}

		if (OperatingSystem.IsWindows() || !wineOptions.Enabled || !toolPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			return await processLauncher.LaunchAsync(
				new ProcessLaunchRequest(
					toolPath,
					toolArguments,
					WorkingDirectory: workingDirectory,
					EnvironmentVariables: environmentVariables,
					WaitForExit: waitForExit,
					StandardOutput: standardOutput,
					StandardError: standardError),
				cancellationToken);
		}

		if (OperatingSystem.IsLinux() && LooksLikeProtonCommand(wineOptions.WineCommand))
		{
			return await LaunchWithProtonAsync(
				protonCommand: wineOptions.WineCommand,
				toolPath,
				toolArguments,
				wineOptions,
				steamRootOverride,
				waitForExit,
				cancellationToken,
				workingDirectory,
				standardOutput,
				standardError,
				environmentVariables);
		}

		try
		{
			return await LaunchWithWineAsync(
				wineCommand: wineOptions.WineCommand,
				toolPath,
				toolArguments,
				wineOptions,
				waitForExit,
				cancellationToken,
				workingDirectory,
				standardOutput,
				standardError,
				environmentVariables);
		}
		catch (Win32Exception ex) when (OperatingSystem.IsLinux() && ex.NativeErrorCode == 2)
		{
			var protonCommand = FindProtonCommand(steamRootOverride);
			if (string.IsNullOrWhiteSpace(protonCommand))
			{
				throw;
			}

			return await LaunchWithProtonAsync(
				protonCommand,
				toolPath,
				toolArguments,
				wineOptions,
				steamRootOverride,
				waitForExit,
				cancellationToken,
				workingDirectory,
				standardOutput,
				standardError,
				environmentVariables);
		}
	}

	private Task<int> LaunchWithWineAsync(
		string wineCommand,
		string toolPath,
		IReadOnlyList<string> toolArguments,
		WineOptions wineOptions,
		bool waitForExit,
		CancellationToken cancellationToken,
		string? workingDirectory,
		IProgress<string>? standardOutput,
		IProgress<string>? standardError,
		IReadOnlyDictionary<string, string>? environmentVariables)
	{
		var env = new Dictionary<string, string>(StringComparer.Ordinal);
		if (!string.IsNullOrWhiteSpace(wineOptions.Prefix))
		{
			env["WINEPREFIX"] = ExpandHomePath(wineOptions.Prefix!);
		}

		if (environmentVariables is not null)
		{
			foreach (var (key, value) in environmentVariables)
			{
				env[key] = value;
			}
		}

		var args = new List<string>(capacity: 1 + toolArguments.Count)
		{
			toolPath
		};
		args.AddRange(toolArguments);

		return processLauncher.LaunchAsync(
			new ProcessLaunchRequest(
				wineCommand,
				args,
				WorkingDirectory: workingDirectory,
				EnvironmentVariables: env,
				WaitForExit: waitForExit,
				StandardOutput: standardOutput,
				StandardError: standardError),
			cancellationToken);
	}

	private Task<int> LaunchWithProtonAsync(
		string protonCommand,
		string toolPath,
		IReadOnlyList<string> toolArguments,
		WineOptions wineOptions,
		string? steamRootOverride,
		bool waitForExit,
		CancellationToken cancellationToken,
		string? workingDirectory,
		IProgress<string>? standardOutput,
		IProgress<string>? standardError,
		IReadOnlyDictionary<string, string>? environmentVariables)
	{
		var steamRoot = SteamInstallLocator.FindSteamRoot(steamRootOverride);
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			throw new DirectoryNotFoundException("Steam Root not found. Set Steam Root so Proton can be used to run Windows tools.");
		}

		var compatDataPath = !string.IsNullOrWhiteSpace(wineOptions.Prefix)
			? ExpandHomePath(wineOptions.Prefix!)
			: GetDefaultStunstickProtonCompatDataPath();
		Directory.CreateDirectory(compatDataPath);

		var env = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["STEAM_COMPAT_DATA_PATH"] = compatDataPath,
			["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = steamRoot
		};

		if (environmentVariables is not null)
		{
			foreach (var (key, value) in environmentVariables)
			{
				env[key] = value;
			}
		}

		var args = new List<string>(capacity: 2 + toolArguments.Count)
		{
			"run",
			toolPath
		};
		args.AddRange(toolArguments);

		return processLauncher.LaunchAsync(
			new ProcessLaunchRequest(
				protonCommand,
				args,
				WorkingDirectory: workingDirectory,
				EnvironmentVariables: env,
				WaitForExit: waitForExit,
				StandardOutput: standardOutput,
				StandardError: standardError),
			cancellationToken);
	}

	private static bool LooksLikeProtonCommand(string wineCommand)
	{
		if (string.IsNullOrWhiteSpace(wineCommand))
		{
			return false;
		}

		var fileName = Path.GetFileName(wineCommand);
		return string.Equals(fileName, "proton", StringComparison.OrdinalIgnoreCase) ||
		       wineCommand.Contains("Proton", StringComparison.OrdinalIgnoreCase);
	}

	private static string? FindProtonCommand(string? steamRootOverride)
	{
		var steamRoot = SteamInstallLocator.FindSteamRoot(steamRootOverride);
		if (string.IsNullOrWhiteSpace(steamRoot))
		{
			return null;
		}

		var common = Path.Combine(steamRoot, "steamapps", "common");
		if (!Directory.Exists(common))
		{
			return null;
		}

		var preferred = new[]
		{
			Path.Combine(common, "Proton - Experimental", "proton"),
			Path.Combine(common, "Proton Hotfix", "proton")
		};

		foreach (var candidate in preferred)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		try
		{
			foreach (var directory in Directory.EnumerateDirectories(common, "Proton*", SearchOption.TopDirectoryOnly))
			{
				var candidate = Path.Combine(directory, "proton");
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}
		catch
		{
		}

		return null;
	}

	private static string ExpandHomePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path[0] != '~')
		{
			return path;
		}

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(home))
		{
			return path;
		}

		if (path.Length == 1)
		{
			return home;
		}

		if (path[1] == Path.DirectorySeparatorChar || path[1] == Path.AltDirectorySeparatorChar)
		{
			return Path.Combine(home, path[2..]);
		}

		return path;
	}

	private static string GetDefaultStunstickProtonCompatDataPath()
	{
		var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrWhiteSpace(baseDir))
		{
			baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
		}

		return Path.Combine(baseDir, "Stunstick", "proton");
	}
}
