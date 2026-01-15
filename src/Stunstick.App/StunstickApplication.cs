using Stunstick.App.Decompile;
using Stunstick.App.Inspect;
using Stunstick.App.Pack;
using Stunstick.App.Progress;
using Stunstick.App.Toolchain;
using Stunstick.App.Unpack;
using Stunstick.App.Workshop;
using Stunstick.Core;
using Stunstick.Core.Inspect;
using System.Text;

namespace Stunstick.App;

public sealed class StunstickApplication
{
	private readonly ToolchainLauncher toolchainLauncher;

	public StunstickApplication(ToolchainLauncher toolchainLauncher)
	{
		this.toolchainLauncher = toolchainLauncher;
	}

	public async Task<StunstickInspectResult> InspectAsync(string path, InspectOptions? options, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		var fileInfo = new FileInfo(path);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("File not found.", path);
		}

		var fileType = StunstickFileClassifier.FromPath(path);
		var computeSha256 = options?.ComputeSha256 ?? true;
		string? sha256 = null;
		if (computeSha256)
		{
			sha256 = await Hashing.Sha256HexAsync(path, cancellationToken);
		}

		return new StunstickInspectResult(
			Path: fileInfo.FullName,
			SizeBytes: fileInfo.Length,
			FileType: fileType,
			Sha256Hex: sha256);
	}

	public Task<VpkInspectResult> InspectVpkAsync(string packagePath, CancellationToken cancellationToken)
	{
		return Task.Run(
			() => VpkInspector.Inspect(packagePath, cancellationToken),
			cancellationToken);
	}

	public Task<MdlInspectResult> InspectMdlAsync(string mdlPath, CancellationToken cancellationToken)
	{
		return InspectMdlAsync(mdlPath, options: null, cancellationToken);
	}

	public Task<MdlInspectResult> InspectMdlAsync(string mdlPath, MdlInspectOptions? options, CancellationToken cancellationToken)
	{
		return Task.Run(
			() => MdlInspector.Inspect(mdlPath, options, cancellationToken),
			cancellationToken);
	}

	public async Task<int> CompileWithStudioMdlAsync(StudioMdlCompileRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.QcPath))
		{
			throw new ArgumentException("QC path is required.", nameof(request));
		}

		if (!File.Exists(request.QcPath))
		{
			throw new FileNotFoundException("QC file not found.", request.QcPath);
		}

		string? gameDirectory = request.GameDirectory;
		string? studioMdlPath = request.StudioMdlPath;
		var qcUsesCollisionModel = !OperatingSystem.IsWindows() && QcFileUsesCollisionModel(request.QcPath);
		string? vphysicsSearchGameDirectory = null;

		if (request.SteamAppId is not null)
		{
			var preset = ToolchainDiscovery.FindSteamPreset(request.SteamAppId.Value, request.SteamRoot);
			if (preset is not null)
			{
				var preferredGameDirectory = ToolchainDiscovery.FindPreferredGameDirectory(preset.GameDirectory, preset.AppId) ?? preset.GameDirectory;
				gameDirectory ??= preferredGameDirectory;
				vphysicsSearchGameDirectory = preferredGameDirectory;

				if (string.IsNullOrWhiteSpace(studioMdlPath))
				{
					studioMdlPath = preset.StudioMdlPath;
				}
			}
		}

		// $collisionmodel/$collisionjoints requires .phy generation. On Linux/macOS the bundled (internal) StudioMDL
		// doesn't generate .phy, so prefer a Windows studiomdl.exe under Wine/Proton when available.
		if (!OperatingSystem.IsWindows() &&
		    qcUsesCollisionModel &&
		    string.IsNullOrWhiteSpace(request.StudioMdlPath) &&
		    string.IsNullOrWhiteSpace(studioMdlPath))
		{
			studioMdlPath = ToolchainDiscovery.FindStudioMdlExePathWithSteamHints(request.SteamRoot);
		}

		// For non-collision compiles on Linux/macOS, prefer the bundled native studiomdl over Windows-only toolchains.
		if (!OperatingSystem.IsWindows() &&
		    !qcUsesCollisionModel &&
		    string.IsNullOrWhiteSpace(request.StudioMdlPath) &&
		    string.IsNullOrWhiteSpace(studioMdlPath))
		{
			var bundled = ToolchainDiscovery.FindBundledStudioMdlPath();
			if (!string.IsNullOrWhiteSpace(bundled) && !bundled.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				studioMdlPath = bundled;
			}
		}

		if (string.IsNullOrWhiteSpace(studioMdlPath) && !string.IsNullOrWhiteSpace(gameDirectory))
		{
			studioMdlPath = ToolchainDiscovery.FindStudioMdlPath(gameDirectory);
		}

		if (string.IsNullOrWhiteSpace(studioMdlPath))
		{
			studioMdlPath = ToolchainDiscovery.FindBundledStudioMdlPath();
		}

		if (string.IsNullOrWhiteSpace(studioMdlPath))
		{
			studioMdlPath = ToolchainDiscovery.FindStudioMdlPathWithSteamHints(request.SteamRoot);
		}

		// If discovery produced a Windows-only tool and we have a native bundled alternative, prefer the native tool.
		if (!OperatingSystem.IsWindows() &&
		    !qcUsesCollisionModel &&
		    string.IsNullOrWhiteSpace(request.StudioMdlPath) &&
		    !string.IsNullOrWhiteSpace(studioMdlPath) &&
		    studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			var bundledNative = ToolchainDiscovery.FindBundledStudioMdlPath();
			if (!string.IsNullOrWhiteSpace(bundledNative) && !bundledNative.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				studioMdlPath = bundledNative;
			}
		}

		if (string.IsNullOrWhiteSpace(studioMdlPath))
		{
			throw new FileNotFoundException("StudioMDL not found. Pass --studiomdl or --game/--steam-appid, or place a bundled \"studiomdl\" under tools/studiomdl next to the Stunstick executable.");
		}

		// On Linux/macOS, the bundled (internal) StudioMDL doesn't generate .phy for $collisionmodel/$collisionjoints.
		// When possible, automatically fall back to a Windows studiomdl.exe under Wine/Proton so users get collision output.
		if (qcUsesCollisionModel &&
		    !OperatingSystem.IsWindows() &&
		    !studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			static string NormalizePathForComparison(string path)
			{
				try
				{
					return Path.GetFullPath(path.Trim());
				}
				catch
				{
					return path.Trim();
				}
			}

			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			bool PathEquals(string? a, string? b)
			{
				if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
				{
					return false;
				}

				return string.Equals(
					NormalizePathForComparison(a),
					NormalizePathForComparison(b),
					comparison);
			}

			var bundled = ToolchainDiscovery.FindBundledStudioMdlPath();
			var bundledI686 = ToolchainDiscovery.FindBundledStudioMdlPathLinuxI686();
			var isBundledNative = PathEquals(studioMdlPath, bundled) || PathEquals(studioMdlPath, bundledI686);

			if (isBundledNative)
			{
				var exe = ToolchainDiscovery.FindStudioMdlExePathWithSteamHints(request.SteamRoot);
				if (!string.IsNullOrWhiteSpace(exe))
				{
					studioMdlPath = exe;
					request.Output?.Report($"STUNSTICK: $collisionmodel detected; using Windows StudioMDL via Wine/Proton for .phy generation: {exe}");
				}
				else
				{
					request.Output?.Report("STUNSTICK WARNING: $collisionmodel detected but only bundled/native StudioMDL is available. Install Source SDK Base 2013 and point StudioMDL to studiomdl.exe (Wine/Proton) to generate .phy.");
				}
			}
		}

		if (qcUsesCollisionModel &&
		    !OperatingSystem.IsWindows() &&
		    !studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			request.Output?.Report("STUNSTICK NOTE: On Linux/macOS, the bundled (internal) StudioMDL does not generate .phy for $collisionmodel/$collisionjoints. Use studiomdl.exe via Wine/Proton when you need collision output.");
		}

		StreamWriter? defineBonesWriter = null;
		string? defineBonesQciPathFileName = null;
		StreamWriter? compileLogWriter = null;
		string? compileLogPathFileName = null;
		object? compileLogGate = null;
		string? createdGameInfoPathFileName = null;

		try
		{
			if (request.DefineBones && request.DefineBonesCreateQciFile)
			{
				defineBonesQciPathFileName = GetDefineBonesQciPathFileName(request.QcPath, request.DefineBonesQciFileName);

				if (File.Exists(defineBonesQciPathFileName) && !request.DefineBonesOverwriteQciFile)
				{
					throw new InvalidDataException($"The DefineBones file, \"{defineBonesQciPathFileName}\", already exists.");
				}

				defineBonesWriter = File.CreateText(defineBonesQciPathFileName);
			}

			IProgress<string>? stdout = request.Output;
			IProgress<string>? stderr = request.Output;
			Dictionary<string, string>? environmentVariables = null;

			var usingWindowsStudioMdlUnderWineOrProton = !OperatingSystem.IsWindows() &&
				studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

			// studiomdl.exe expects -game to be a valid mod folder with gameinfo.txt. When compiling into a
			// scratch/output folder (common in Blender workflows) we can synthesize a minimal gameinfo.txt.
			if (qcUsesCollisionModel && usingWindowsStudioMdlUnderWineOrProton)
			{
				if (string.IsNullOrWhiteSpace(gameDirectory))
				{
					try
					{
						gameDirectory = Path.GetDirectoryName(Path.GetFullPath(request.QcPath));
					}
					catch
					{
						gameDirectory = null;
					}
				}

				if (!string.IsNullOrWhiteSpace(gameDirectory))
				{
					var candidate = Path.Combine(gameDirectory, "gameinfo.txt");
					if (!File.Exists(candidate))
					{
						try
						{
							File.WriteAllText(candidate, GetMinimalGameInfoText());
							createdGameInfoPathFileName = candidate;
							request.Output?.Report($"STUNSTICK: Created temporary gameinfo.txt for studiomdl.exe: \"{candidate}\"");
						}
						catch (Exception ex)
						{
							request.Output?.Report($"STUNSTICK WARNING: Failed to create gameinfo.txt in \"{gameDirectory}\" for studiomdl.exe: {ex.Message}");
						}
					}
				}
			}
			else if (qcUsesCollisionModel &&
			         !string.IsNullOrWhiteSpace(gameDirectory) &&
			         !File.Exists(Path.Combine(gameDirectory, "gameinfo.txt")))
			{
				request.Output?.Report("STUNSTICK: NOTE: No gameinfo.txt found in -game; for $collisionmodel/$collisionjoints, using a real mod folder with gameinfo.txt is recommended (e.g. hl2/cstrike).");
			}

			if (OperatingSystem.IsLinux() &&
			    qcUsesCollisionModel &&
			    !studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				var vphysicsBin = ToolchainDiscovery.FindVPhysicsBinDirectoryForTool(
					studioMdlPath,
					vphysicsSearchGameDirectory ?? gameDirectory,
					request.SteamRoot);
				if (!string.IsNullOrWhiteSpace(vphysicsBin))
				{
					var existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
					var value = string.IsNullOrWhiteSpace(existing) ? vphysicsBin : $"{vphysicsBin}:{existing}";
					environmentVariables ??= new Dictionary<string, string>(StringComparer.Ordinal);
					environmentVariables["LD_LIBRARY_PATH"] = value;

					request.Output?.Report($"STUNSTICK: Using native vphysics from: {vphysicsBin}");
				}
				else
				{
					request.Output?.Report("STUNSTICK WARNING: vphysics.so not found (no libtier0.so/vphysics.so). .phy will not be generated on Linux.");
				}
			}

			var args = new List<string>();
			if (!string.IsNullOrWhiteSpace(gameDirectory))
			{
				args.Add("-game");
				args.Add(gameDirectory!);
			}

			if (request.NoP4)
			{
				args.Add("-nop4");
			}

			if (request.Verbose)
			{
				args.Add("-verbose");
			}

			if (request.DefineBones)
			{
				args.Add("-definebones");
			}

			foreach (var token in SplitToolArguments(request.DirectOptions))
			{
				args.Add(token);
			}

			args.Add(request.QcPath);

			IReadOnlyList<string> toolArgs = args;
			string? workingDirectory = null;
			if (!OperatingSystem.IsWindows() &&
			    studioMdlPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				toolArgs = ConvertStudioMdlArgsForWine(args);
				workingDirectory = Path.GetDirectoryName(Path.GetFullPath(request.QcPath));
			}

			if (request.WriteLogFile)
			{
				var qcFullPath = Path.GetFullPath(request.QcPath);
				var qcDir = Path.GetDirectoryName(qcFullPath) ?? string.Empty;
				var baseName = Path.GetFileNameWithoutExtension(qcFullPath);
				compileLogPathFileName = Path.Combine(qcDir, $"{baseName}.compile.log");

				var logStream = new FileStream(compileLogPathFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
				compileLogWriter = new StreamWriter(logStream) { AutoFlush = true };
				compileLogGate = new object();

				compileLogWriter.WriteLine("Stunstick compile log");
				compileLogWriter.WriteLine($"Started: {DateTimeOffset.Now:O}");
				compileLogWriter.WriteLine($"QC: {qcFullPath}");
				if (!string.IsNullOrWhiteSpace(gameDirectory))
				{
					compileLogWriter.WriteLine($"GameDir: {gameDirectory}");
				}
				compileLogWriter.WriteLine($"StudioMDL: {studioMdlPath}");
				compileLogWriter.WriteLine("Args:");
				foreach (var token in toolArgs)
				{
					compileLogWriter.WriteLine($"  {token}");
				}
				compileLogWriter.WriteLine();

				stdout = new LogFileTeeProgress(compileLogWriter, compileLogGate, "OUT", stdout);
				stderr = new LogFileTeeProgress(compileLogWriter, compileLogGate, "ERR", stderr);
				request.Output?.Report($"STUNSTICK: Writing compile log: \"{compileLogPathFileName}\"");
			}

			if (request.DefineBones && defineBonesWriter is not null)
			{
				stdout = new DefineBonesCaptureProgress(defineBonesWriter, stdout);
			}

			var exitCode = await toolchainLauncher.LaunchExternalToolAsync(
				toolPath: studioMdlPath,
				toolArguments: toolArgs,
				wineOptions: request.WineOptions ?? new WineOptions(),
				steamRootOverride: request.SteamRoot,
				waitForExit: true,
				cancellationToken,
				workingDirectory: workingDirectory,
				standardOutput: stdout,
				standardError: stderr,
				environmentVariables: environmentVariables).ConfigureAwait(false);

			if (compileLogWriter is not null && compileLogGate is not null)
			{
				lock (compileLogGate)
				{
					compileLogWriter.WriteLine();
					compileLogWriter.WriteLine($"ExitCode: {exitCode}");
					compileLogWriter.WriteLine($"Ended: {DateTimeOffset.Now:O}");
				}
			}

			return exitCode;
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(createdGameInfoPathFileName))
			{
				try
				{
					File.Delete(createdGameInfoPathFileName);
				}
				catch
				{
					request.Output?.Report($"STUNSTICK WARNING: Failed to delete temporary gameinfo.txt: \"{createdGameInfoPathFileName}\"");
				}
			}

			if (compileLogWriter is not null)
			{
				compileLogWriter.Flush();
				compileLogWriter.Dispose();
				compileLogWriter = null;
				compileLogPathFileName = null;
				compileLogGate = null;
			}

			if (defineBonesWriter is not null)
			{
				defineBonesWriter.Flush();
				defineBonesWriter.Dispose();
				defineBonesWriter = null;

				if (!string.IsNullOrWhiteSpace(defineBonesQciPathFileName))
				{
					FinalizeDefineBonesQciFile(
						request,
						defineBonesQciPathFileName,
						request.QcPath);
				}
			}
		}
	}

	private static string GetMinimalGameInfoText()
	{
		return """
			"GameInfo"
			{
				game        "Stunstick Compile"
				title       "Stunstick Compile"
				type        singleplayer_only

				FileSystem
				{
					SearchPaths
					{
						mod+mod_write+default_write_path    |gameinfo_path|.
						game+game_write                     |gameinfo_path|.
						platform                            |all_source_engine_paths|platform
					}
				}
			}
			""";
	}

	private static string GetDefineBonesQciPathFileName(string qcPathFileName, string? requestedFileName)
	{
		var fileName = string.IsNullOrWhiteSpace(requestedFileName) ? "DefineBones" : requestedFileName.Trim();
		if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
		{
			fileName += ".qci";
		}

		var qcDirectory = Path.GetDirectoryName(Path.GetFullPath(qcPathFileName)) ?? string.Empty;
		return Path.Combine(qcDirectory, fileName);
	}

	private static void FinalizeDefineBonesQciFile(StudioMdlCompileRequest request, string qciPathFileName, string qcPathFileName)
	{
		if (!File.Exists(qciPathFileName))
		{
			request.Output?.Report($"STUNSTICK WARNING: Failed to write QCI file: \"{qciPathFileName}\"");
			return;
		}

		var fileInfo = new FileInfo(qciPathFileName);
		if (fileInfo.Length == 0)
		{
			request.Output?.Report("STUNSTICK WARNING: No define bones were written to QCI file.");
			try
			{
				File.Delete(qciPathFileName);
			}
			catch
			{
				request.Output?.Report($"STUNSTICK WARNING: Failed to delete empty QCI file: \"{qciPathFileName}\"");
			}

			return;
		}

		request.Output?.Report($"STUNSTICK: Wrote define bones into QCI file: \"{qciPathFileName}\"");

		if (request.DefineBonesModifyQcFile)
		{
			var includeLine = AppendIncludeLineToQcFile(qcPathFileName, qciPathFileName);
			request.Output?.Report($"STUNSTICK: Wrote in the QC file this line: {includeLine}");
		}
	}

	private static string AppendIncludeLineToQcFile(string qcPathFileName, string qciPathFileName)
	{
		var qcDirectory = Path.GetDirectoryName(Path.GetFullPath(qcPathFileName)) ?? string.Empty;
		var relative = Path.GetRelativePath(qcDirectory, qciPathFileName).Replace('\\', '/');

		var includeKeyword = QcFileUsesMixedCaseKeywords(qcPathFileName) ? "$Include" : "$include";
		var line = $"{includeKeyword} \"{relative.Replace("\"", "'")}\"";

		File.AppendAllText(qcPathFileName, Environment.NewLine + Environment.NewLine + line + Environment.NewLine);
		return line;
	}

	private static bool QcFileUsesMixedCaseKeywords(string qcPathFileName)
	{
		try
		{
			var text = File.ReadAllText(qcPathFileName);
			return text.Contains("$ModelName", StringComparison.Ordinal) ||
				   text.Contains("$Include", StringComparison.Ordinal) ||
				   text.Contains("$Sequence", StringComparison.Ordinal) ||
				   text.Contains("$BodyGroup", StringComparison.Ordinal) ||
				   text.Contains("$TextureGroup", StringComparison.Ordinal) ||
				   text.Contains("$DefineBone", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static bool QcFileUsesCollisionModel(string qcPathFileName)
	{
		try
		{
			var text = File.ReadAllText(qcPathFileName);
			return text.Contains("$collisionmodel", StringComparison.OrdinalIgnoreCase) ||
				   text.Contains("$collisionjoints", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static IReadOnlyList<string> ConvertStudioMdlArgsForWine(IReadOnlyList<string> args)
	{
		var converted = new List<string>(args);

		for (var i = 0; i < converted.Count; i++)
		{
			if (string.Equals(converted[i], "-game", StringComparison.OrdinalIgnoreCase) && i + 1 < converted.Count)
			{
				converted[i + 1] = ToWineWindowsPath(converted[i + 1]);
				i++;
			}
		}

		// QC path is always the final argument.
		if (converted.Count > 0)
		{
			converted[^1] = ToWineWindowsPath(converted[^1]);
		}

		return converted;
	}

	private static string ToWineWindowsPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return path ?? string.Empty;
		}

		// Already a Windows-style path (e.g. C:\ or Z:\).
		if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
		{
			return path;
		}

		var full = Path.GetFullPath(path);
		if (full.StartsWith(Path.DirectorySeparatorChar))
		{
			// Wine/Proton maps Z: to / by default.
			return "Z:" + full.Replace(Path.DirectorySeparatorChar, '\\');
		}

		return full.Replace(Path.DirectorySeparatorChar, '\\');
	}

	private static IReadOnlyList<string> SplitToolArguments(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<string>();
		}

		var args = new List<string>();
		var current = new StringBuilder();
		var inQuotes = false;

		var span = text.AsSpan();
		for (var i = 0; i < span.Length; i++)
		{
			var ch = span[i];

			if (ch == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if (!inQuotes && char.IsWhiteSpace(ch))
			{
				if (current.Length > 0)
				{
					args.Add(current.ToString());
					current.Clear();
				}

				continue;
			}

			if (ch == '\\' && i + 1 < span.Length && (span[i + 1] == '"' || span[i + 1] == '\\'))
			{
				current.Append(span[i + 1]);
				i++;
				continue;
			}

			current.Append(ch);
		}

		if (current.Length > 0)
		{
			args.Add(current.ToString());
		}

		return args;
	}

	private sealed class DefineBonesCaptureProgress : IProgress<string>
	{
		private readonly StreamWriter writer;
		private readonly IProgress<string>? downstream;
		private readonly object gate = new();

		public DefineBonesCaptureProgress(StreamWriter writer, IProgress<string>? downstream)
		{
			this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
			this.downstream = downstream;
		}

		public void Report(string value)
		{
			var line = value ?? string.Empty;
			var trimmed = line.Trim();
			if (trimmed.StartsWith("$definebone", StringComparison.Ordinal))
			{
				lock (gate)
				{
					writer.WriteLine(trimmed);
				}
			}

			downstream?.Report(line);
		}
	}

	private sealed class LogFileTeeProgress : IProgress<string>
	{
		private readonly StreamWriter writer;
		private readonly object gate;
		private readonly string prefix;
		private readonly IProgress<string>? downstream;

		public LogFileTeeProgress(StreamWriter writer, object gate, string prefix, IProgress<string>? downstream)
		{
			this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
			this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
			this.prefix = string.IsNullOrWhiteSpace(prefix) ? "OUT" : prefix;
			this.downstream = downstream;
		}

		public void Report(string value)
		{
			var line = value ?? string.Empty;
			lock (gate)
			{
				writer.WriteLine($"[{prefix}] {line}");
			}

			downstream?.Report(line);
		}
	}

		private sealed class UnpackLogProgress : IProgress<StunstickProgress>
		{
			private readonly StreamWriter writer;
			private readonly object gate;
			private readonly IProgress<StunstickProgress>? downstream;
		private string? lastItem;
		private string? lastMessage;

		public UnpackLogProgress(StreamWriter writer, object gate, IProgress<StunstickProgress>? downstream)
		{
			this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
			this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
			this.downstream = downstream;
		}

		public void Report(StunstickProgress value)
		{
			if (value is null)
			{
				return;
			}

			var currentItem = value.CurrentItem;
			if (!string.IsNullOrWhiteSpace(currentItem) && !string.Equals(currentItem, lastItem, StringComparison.Ordinal))
			{
				lock (gate)
				{
					writer.WriteLine($"Extract: {currentItem}");
				}

				lastItem = currentItem;
			}

			var message = value.Message;
			if (!string.IsNullOrWhiteSpace(message) && !string.Equals(message, lastMessage, StringComparison.Ordinal))
			{
				lock (gate)
				{
					writer.WriteLine($"Message: {message}");
				}

				lastMessage = message;
			}

				downstream?.Report(value);
			}
		}

		private sealed class PackLogProgress : IProgress<StunstickProgress>
		{
			private readonly StreamWriter writer;
			private readonly object gate;
			private readonly IProgress<StunstickProgress>? downstream;
			private string? lastItem;
			private string? lastMessage;

			public PackLogProgress(StreamWriter writer, object gate, IProgress<StunstickProgress>? downstream)
			{
				this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
				this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
				this.downstream = downstream;
			}

			public void Report(StunstickProgress value)
			{
				if (value is null)
				{
					return;
				}

				var currentItem = value.CurrentItem;
				if (!string.IsNullOrWhiteSpace(currentItem) && !string.Equals(currentItem, lastItem, StringComparison.Ordinal))
				{
					lock (gate)
					{
						writer.WriteLine($"Pack: {currentItem}");
					}

					lastItem = currentItem;
				}

				var message = value.Message;
				if (!string.IsNullOrWhiteSpace(message) && !string.Equals(message, lastMessage, StringComparison.Ordinal))
				{
					lock (gate)
					{
						writer.WriteLine($"Message: {message}");
					}

					lastMessage = message;
				}

				downstream?.Report(value);
			}
		}

	public async Task<int> ViewWithHlmvAsync(HlmvViewRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.MdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(request));
		}

		string? hlmvPath = request.HlmvPath;
		string? gameDirectory = request.GameDirectory;

		if (string.IsNullOrWhiteSpace(hlmvPath) && request.SteamAppId is not null)
		{
			var preset = ToolchainDiscovery.FindSteamPreset(request.SteamAppId.Value, request.SteamRoot);
			if (preset is not null)
			{
				hlmvPath = preset.HlmvPath;
				gameDirectory ??= ToolchainDiscovery.FindPreferredGameDirectory(preset.GameDirectory, preset.AppId) ?? preset.GameDirectory;
			}
		}

		if (string.IsNullOrWhiteSpace(hlmvPath) && !string.IsNullOrWhiteSpace(gameDirectory))
		{
			hlmvPath = ToolchainDiscovery.FindHlmvPath(gameDirectory);
		}

		if (string.IsNullOrWhiteSpace(hlmvPath))
		{
			hlmvPath = ToolchainDiscovery.FindHlmvPathWithSteamHints(request.SteamRoot);
		}

		if (string.IsNullOrWhiteSpace(hlmvPath))
		{
			throw new FileNotFoundException("HLMV not found. Pass --hlmv or --game/--steam-appid.");
		}

		string mdlArgument = request.MdlPath;
		string? workingDirectory = null;
		if (request.ViewAsReplacement)
		{
			var replacement = await Task.Run(
				() => HlmvReplacement.Prepare(request.MdlPath, cancellationToken),
				cancellationToken).ConfigureAwait(false);
			mdlArgument = replacement.RelativeMdlPath;
			workingDirectory = replacement.WorkingDirectory;
		}

		var args = new List<string>(capacity: 4);
		if (!string.IsNullOrWhiteSpace(gameDirectory) && Directory.Exists(gameDirectory))
		{
			args.Add("-olddialogs");
			args.Add("-game");
			args.Add(gameDirectory);
		}
		args.Add(mdlArgument);

		return await toolchainLauncher.LaunchExternalToolAsync(
			toolPath: hlmvPath,
			toolArguments: args,
			wineOptions: request.WineOptions ?? new WineOptions(),
			steamRootOverride: request.SteamRoot,
			waitForExit: false,
			cancellationToken,
			workingDirectory: workingDirectory).ConfigureAwait(false);
	}

	public Task DecompileAsync(DecompileRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (!File.Exists(request.MdlPath))
		{
			throw new FileNotFoundException("MDL file not found.", request.MdlPath);
		}

		var extension = Path.GetExtension(request.MdlPath).Trim();
		if (string.Equals(extension, ".mdl", StringComparison.OrdinalIgnoreCase))
		{
			return MdlDecompiler.DecompileAsync(
				request.MdlPath,
				request.OutputDirectory,
				request.Options ?? new DecompileOptions(),
				cancellationToken);
		}

		throw new NotSupportedException($"Unsupported decompile input type: {extension}");
	}

	public async Task UnpackAsync(UnpackRequest request, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (!File.Exists(request.PackagePath))
		{
			throw new FileNotFoundException("Package file not found.", request.PackagePath);
		}

		var extension = Path.GetExtension(request.PackagePath).Trim();
		var outputDirectory = request.OutputDirectory;

		StreamWriter? logWriter = null;
		object? logGate = null;
		var progress = request.Progress;

		try
		{
			if (request.WriteLogFile)
			{
				Directory.CreateDirectory(outputDirectory);
				var logPathFileName = Path.Combine(outputDirectory, "unpack.log");

				var logStream = new FileStream(logPathFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
				logWriter = new StreamWriter(logStream) { AutoFlush = true };
				logGate = new object();

				lock (logGate)
				{
					logWriter.WriteLine("Stunstick unpack log");
					logWriter.WriteLine($"Started: {DateTimeOffset.Now:O}");
					logWriter.WriteLine($"Package: {Path.GetFullPath(request.PackagePath)}");
					logWriter.WriteLine($"Output: {Path.GetFullPath(outputDirectory)}");
					logWriter.WriteLine($"VerifyCrc32: {request.VerifyCrc32}");
					logWriter.WriteLine($"VerifyMd5: {request.VerifyMd5}");

					if (request.OnlyPaths is not null && request.OnlyPaths.Count > 0)
					{
						logWriter.WriteLine($"OnlyPaths: {request.OnlyPaths.Count}");
						foreach (var path in request.OnlyPaths)
						{
							logWriter.WriteLine($"  {path}");
						}
					}

					logWriter.WriteLine();
				}

				progress = new UnpackLogProgress(logWriter, logGate, progress);
			}

			if (string.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlySet<string>? onlyPaths = null;
				if (request.OnlyPaths is not null && request.OnlyPaths.Count > 0)
				{
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					onlyPaths = new HashSet<string>(request.OnlyPaths.Select(p => p.Replace('\\', '/')), comparer);
				}

				await VpkUnpacker.UnpackAsync(
					request.PackagePath,
					outputDirectory,
					verifyCrc32: request.VerifyCrc32,
					verifyMd5: request.VerifyMd5,
					keepFullPath: request.KeepFullPath,
					progress: progress,
					cancellationToken,
					onlyRelativePaths: onlyPaths);
			}
			else if (string.Equals(extension, ".gma", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlySet<string>? onlyPaths = null;
				if (request.OnlyPaths is not null && request.OnlyPaths.Count > 0)
				{
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					onlyPaths = new HashSet<string>(request.OnlyPaths.Select(p => p.Replace('\\', '/')), comparer);
				}

				await GmaUnpacker.UnpackAsync(
					request.PackagePath,
					outputDirectory,
					verifyCrc32: request.VerifyCrc32,
					keepFullPath: request.KeepFullPath,
					progress: progress,
					cancellationToken,
					onlyRelativePaths: onlyPaths);
			}
			else if (string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlySet<string>? onlyPaths = null;
				if (request.OnlyPaths is not null && request.OnlyPaths.Count > 0)
				{
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					onlyPaths = new HashSet<string>(request.OnlyPaths.Select(p => p.Replace('\\', '/')), comparer);
				}

				await ApkUnpacker.UnpackAsync(
					request.PackagePath,
					outputDirectory,
					verifyCrc32: request.VerifyCrc32,
					keepFullPath: request.KeepFullPath,
					progress: progress,
					cancellationToken,
					onlyRelativePaths: onlyPaths);
			}
			else if (string.Equals(extension, ".hfs", StringComparison.OrdinalIgnoreCase))
			{
				IReadOnlySet<string>? onlyPaths = null;
				if (request.OnlyPaths is not null && request.OnlyPaths.Count > 0)
				{
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					onlyPaths = new HashSet<string>(request.OnlyPaths.Select(p => p.Replace('\\', '/')), comparer);
				}

				await HfsUnpacker.UnpackAsync(
					request.PackagePath,
					outputDirectory,
					verifyCrc32: request.VerifyCrc32,
					keepFullPath: request.KeepFullPath,
					progress: progress,
					cancellationToken,
					onlyRelativePaths: onlyPaths);
			}
			else
			{
				throw new NotSupportedException($"Unsupported package type: {extension}");
			}

			if (logWriter is not null && logGate is not null)
			{
				lock (logGate)
				{
					logWriter.WriteLine();
					logWriter.WriteLine("Status: Success");
				}
			}
		}
		catch (OperationCanceledException)
		{
			if (logWriter is not null && logGate is not null)
			{
				lock (logGate)
				{
					logWriter.WriteLine();
					logWriter.WriteLine("Status: Canceled");
				}
			}

			throw;
		}
		catch (Exception ex)
		{
			if (logWriter is not null && logGate is not null)
			{
				lock (logGate)
				{
					logWriter.WriteLine();
					logWriter.WriteLine("Status: Error");
					logWriter.WriteLine($"Error: {ex.Message}");
				}
			}

			throw;
		}
		finally
		{
			if (logWriter is not null)
			{
				if (logGate is not null)
				{
					lock (logGate)
					{
						logWriter.WriteLine($"Ended: {DateTimeOffset.Now:O}");
					}
				}

				logWriter.Flush();
				logWriter.Dispose();
			}
		}
	}

	public Task<IReadOnlyList<PackageEntry>> ListPackageEntriesAsync(string packagePath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(packagePath))
		{
			throw new ArgumentException("Package path is required.", nameof(packagePath));
		}

		if (!File.Exists(packagePath))
		{
			throw new FileNotFoundException("Package file not found.", packagePath);
		}

		var extension = Path.GetExtension(packagePath).Trim();
		if (string.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase))
		{
			return Task.Run<IReadOnlyList<PackageEntry>>(
				() => VpkUnpacker.ListEntries(packagePath, cancellationToken),
				cancellationToken);
		}

		if (string.Equals(extension, ".gma", StringComparison.OrdinalIgnoreCase))
		{
			return Task.Run<IReadOnlyList<PackageEntry>>(
				() => GmaUnpacker.ListEntries(packagePath, cancellationToken),
				cancellationToken);
		}

		if (string.Equals(extension, ".apk", StringComparison.OrdinalIgnoreCase))
		{
			return Task.Run<IReadOnlyList<PackageEntry>>(
				() => ApkUnpacker.ListEntries(packagePath, cancellationToken),
				cancellationToken);
		}

		if (string.Equals(extension, ".hfs", StringComparison.OrdinalIgnoreCase))
		{
			return Task.Run<IReadOnlyList<PackageEntry>>(
				() => HfsUnpacker.ListEntries(packagePath, cancellationToken),
				cancellationToken);
		}

		throw new NotSupportedException($"Unsupported package type: {extension}");
	}

		public async Task PackAsync(PackRequest request, CancellationToken cancellationToken)
		{
			if (request is null)
			{
				throw new ArgumentNullException(nameof(request));
			}

		if (string.IsNullOrWhiteSpace(request.InputDirectory))
		{
			throw new ArgumentException("Input directory is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.OutputPackagePath))
		{
			throw new ArgumentException("Output package path is required.", nameof(request));
		}

			if (!Directory.Exists(request.InputDirectory))
			{
				throw new DirectoryNotFoundException($"Input directory not found: \"{request.InputDirectory}\".");
			}

			StreamWriter? logWriter = null;
			object? logGate = null;
			PackRequest effectiveRequest = request;

			try
			{
				if (request.WriteLogFile)
				{
					var outputFullPath = Path.GetFullPath(request.OutputPackagePath);
					var outputDirectory = Path.GetDirectoryName(outputFullPath);
					if (!string.IsNullOrWhiteSpace(outputDirectory))
					{
						Directory.CreateDirectory(outputDirectory);
					}

					var baseName = Path.GetFileNameWithoutExtension(outputFullPath);
					baseName = string.IsNullOrWhiteSpace(baseName) ? "pack" : baseName;

					var logPathFileName = Path.Combine(outputDirectory ?? ".", $"{baseName}.pack.log");

					var logStream = new FileStream(logPathFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
					logWriter = new StreamWriter(logStream) { AutoFlush = true };
					logGate = new object();

					lock (logGate)
					{
						logWriter.WriteLine("Stunstick pack log");
						logWriter.WriteLine($"Started: {DateTimeOffset.Now:O}");
						logWriter.WriteLine($"Input: {Path.GetFullPath(request.InputDirectory)}");
						logWriter.WriteLine($"Output: {outputFullPath}");
						logWriter.WriteLine($"MultiFile: {request.MultiFile}");
						if (request.MaxArchiveSizeBytes is not null)
						{
							logWriter.WriteLine($"MaxArchiveSizeBytes: {request.MaxArchiveSizeBytes}");
						}
						logWriter.WriteLine($"PreloadBytes: {request.PreloadBytes}");
						logWriter.WriteLine($"VpkVersion: {request.VpkVersion}");
						logWriter.WriteLine($"IncludeMd5Sections: {request.IncludeMd5Sections}");
						if (!string.IsNullOrWhiteSpace(request.GameDirectory))
						{
							logWriter.WriteLine($"GameDir: {Path.GetFullPath(request.GameDirectory)}");
						}
						if (request.SteamAppId is not null)
						{
							logWriter.WriteLine($"SteamAppId: {request.SteamAppId}");
						}
						if (!string.IsNullOrWhiteSpace(request.SteamRoot))
						{
							logWriter.WriteLine($"SteamRoot: {Path.GetFullPath(request.SteamRoot)}");
						}
						if (!string.IsNullOrWhiteSpace(request.GmadPath))
						{
							logWriter.WriteLine($"GMAD: {request.GmadPath}");
						}
						if (!string.IsNullOrWhiteSpace(request.VpkToolPath))
						{
							logWriter.WriteLine($"VPK: {request.VpkToolPath}");
						}
						if (!string.IsNullOrWhiteSpace(request.DirectOptions))
						{
							logWriter.WriteLine($"DirectOptions: {request.DirectOptions}");
						}

						logWriter.WriteLine();
					}

					request.Progress?.Report(new StunstickProgress("Pack", 0, 0, Message: $"Writing pack log: \"{logPathFileName}\""));

					var progress = new PackLogProgress(logWriter, logGate, request.Progress);
					effectiveRequest = request with { Progress = progress };
				}

				var extension = Path.GetExtension(effectiveRequest.OutputPackagePath).Trim();
				if (string.Equals(extension, ".vpk", StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrWhiteSpace(effectiveRequest.DirectOptions) || !string.IsNullOrWhiteSpace(effectiveRequest.VpkToolPath))
					{
						await VpkToolPacker.PackAsync(effectiveRequest, toolchainLauncher, cancellationToken).ConfigureAwait(false);
					}
					else
					{
						await VpkPacker.PackAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
					}
				}
				else if (string.Equals(extension, ".fpx", StringComparison.OrdinalIgnoreCase))
				{
					if (!string.IsNullOrWhiteSpace(effectiveRequest.DirectOptions) || !string.IsNullOrWhiteSpace(effectiveRequest.VpkToolPath))
					{
						throw new NotSupportedException("Direct packer options / external VPK tool are not supported for FPX output.");
					}

					await VpkPacker.PackAsync(effectiveRequest, cancellationToken).ConfigureAwait(false);
				}
				else if (string.Equals(extension, ".gma", StringComparison.OrdinalIgnoreCase))
				{
					await GmaPacker.PackAsync(effectiveRequest, toolchainLauncher, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					throw new NotSupportedException($"Unsupported package type: {extension}");
				}

				if (logWriter is not null && logGate is not null)
				{
					lock (logGate)
					{
						logWriter.WriteLine();
						logWriter.WriteLine("Status: Success");
					}
				}
			}
			catch (OperationCanceledException)
			{
				if (logWriter is not null && logGate is not null)
				{
					lock (logGate)
					{
						logWriter.WriteLine();
						logWriter.WriteLine("Status: Canceled");
					}
				}

				throw;
			}
			catch (Exception ex)
			{
				if (logWriter is not null && logGate is not null)
				{
					lock (logGate)
					{
						logWriter.WriteLine();
						logWriter.WriteLine("Status: Error");
						logWriter.WriteLine($"Error: {ex.Message}");
					}
				}

				throw;
			}
			finally
			{
				if (logWriter is not null)
				{
					if (logGate is not null)
					{
						lock (logGate)
						{
							logWriter.WriteLine($"Ended: {DateTimeOffset.Now:O}");
						}
					}

					logWriter.Flush();
					logWriter.Dispose();
				}
			}
		}

		public Task<WorkshopDownloadResult> DownloadWorkshopItemAsync(WorkshopDownloadRequest request, CancellationToken cancellationToken)
		{
			return WorkshopDownloader.DownloadFromCacheAsync(request, cancellationToken);
		}

			public Task<WorkshopPublishResult> PublishWorkshopItemAsync(WorkshopPublishRequest request, CancellationToken cancellationToken)
			{
				return WorkshopPublisher.PublishAsync(request, cancellationToken);
			}

			public Task<WorkshopListResult> ListWorkshopPublishedItemsAsync(WorkshopListRequest request, CancellationToken cancellationToken)
			{
				return WorkshopPublisher.ListMyPublishedItemsAsync(request, cancellationToken);
			}

			public Task<WorkshopQuotaResult> GetWorkshopQuotaAsync(WorkshopQuotaRequest request, CancellationToken cancellationToken)
			{
				return WorkshopPublisher.GetQuotaAsync(request, cancellationToken);
			}

			public Task<WorkshopDeleteResult> DeleteWorkshopItemAsync(WorkshopDeleteRequest request, CancellationToken cancellationToken)
			{
				return WorkshopPublisher.DeleteAsync(request, cancellationToken);
			}
		}
