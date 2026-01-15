using Stunstick.App.Toolchain;
using Stunstick.App.Progress;
using Stunstick.Core;
using System.Text;
using System.Text.Json;

namespace Stunstick.App.Pack;

internal static class GmaPacker
{
	private const uint GarrysModAppId = 4000;
	private const byte GmaVersion = 3;
	private const int DefaultBufferSize = 1024 * 256;

	public static async Task PackAsync(PackRequest request, ToolchainLauncher toolchainLauncher, CancellationToken cancellationToken)
	{
		if (request is null)
		{
			throw new ArgumentNullException(nameof(request));
		}

		if (!string.IsNullOrWhiteSpace(request.GmadPath) || !string.IsNullOrWhiteSpace(request.DirectOptions))
		{
			if (toolchainLauncher is null)
			{
				throw new ArgumentNullException(nameof(toolchainLauncher));
			}

			await PackWithGmadAsync(request, toolchainLauncher, cancellationToken);
			return;
		}

		await PackBuiltInAsync(request, cancellationToken);
	}

	private sealed record AddonManifest(
		string Title,
		string Description,
		string Author,
		uint AddonVersion,
		IReadOnlyList<string> IgnorePatterns,
		IReadOnlyList<string> Tags);

	private sealed record GmaFileEntry(
		string SourcePath,
		string RelativePath,
		long Length,
		uint Crc32);

	private static async Task PackBuiltInAsync(PackRequest request, CancellationToken cancellationToken)
	{
		var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);

		var (packInput, addonJsonPath, cleanupRoot) = PrepareAddonJsonPackInput(inputDirectoryFullPath, cancellationToken);
		var inputRoot = EnsureTrailingSeparator(packInput);

		try
		{
			var manifest = await ReadAddonJsonAsync(addonJsonPath, fallbackTitle: Path.GetFileName(packInput), cancellationToken);
			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			var entries = new List<GmaFileEntry>();
			foreach (var filePath in Directory.EnumerateFiles(packInput, "*", SearchOption.AllDirectories))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var fullPath = Path.GetFullPath(filePath);
				if (!fullPath.StartsWith(inputRoot, comparison))
				{
					throw new InvalidDataException($"Refusing to pack file outside input directory: \"{filePath}\".");
				}

				if (string.Equals(fullPath, outputFullPath, comparison))
				{
					continue;
				}

			var relativePath = Path.GetRelativePath(packInput, fullPath);
			relativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
			relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');
			relativePath = relativePath.TrimStart('/');

			if (PathsEqual(fullPath, addonJsonPath))
			{
				// GMAD does not include addon.json in the packed file list; keeping it breaks the whitelist on mount.
				continue;
			}

				if (IsIgnored(relativePath, manifest.IgnorePatterns, comparison))
				{
					continue;
				}

				var info = new FileInfo(fullPath);
				entries.Add(new GmaFileEntry(
					SourcePath: fullPath,
					RelativePath: relativePath,
					Length: info.Length,
					Crc32: 0));
			}

			entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));

			var totalBytes = entries.Sum(entry => entry.Length) * 2;
			var progress = new ProgressReporter(request.Progress, operation: "Pack", totalBytes: totalBytes);

			var buffer = new byte[DefaultBufferSize];
			var computedEntries = new List<GmaFileEntry>(entries.Count);
			foreach (var entry in entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				progress.SetCurrentItem(entry.RelativePath);
				progress.SetMessage("Hashing");

				var crc32 = await ComputeCrc32Async(entry.SourcePath, buffer, progress, cancellationToken);
				computedEntries.Add(entry with { Crc32 = crc32 });
			}

			progress.SetMessage("Writing");

			await using var outputStream = new FileStream(
				request.OutputPackagePath,
				FileMode.Create,
				FileAccess.Write,
				FileShare.None,
				bufferSize: DefaultBufferSize,
				useAsync: true);

			using var writer = new BinaryWriter(outputStream, Encoding.UTF8, leaveOpen: true);

			writer.Write(Encoding.ASCII.GetBytes("GMAD"));
			writer.Write(GmaVersion);
			writer.Write(0UL); // steamid
			writer.Write((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
			writer.Write((byte)0); // padding / required content list terminator
			WriteNullTerminatedString(writer, manifest.Title);
			WriteNullTerminatedString(writer, manifest.Description);
			WriteNullTerminatedString(writer, manifest.Author);
			writer.Write(manifest.AddonVersion);

			uint fileNumber = 1;
			foreach (var entry in computedEntries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				writer.Write(fileNumber);
				WriteNullTerminatedString(writer, entry.RelativePath);
				writer.Write((ulong)entry.Length);
				writer.Write(entry.Crc32);
				fileNumber++;
			}

			writer.Write(0u); // end file list
			await outputStream.FlushAsync(cancellationToken);

			foreach (var entry in computedEntries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				progress.SetCurrentItem(entry.RelativePath);
				progress.SetMessage("Writing");

				await using var inputStream = new FileStream(
					entry.SourcePath,
					FileMode.Open,
					FileAccess.Read,
					FileShare.ReadWrite,
					bufferSize: DefaultBufferSize,
					useAsync: true);

				while (true)
				{
					var read = await inputStream.ReadAsync(buffer, cancellationToken);
					if (read <= 0)
					{
						break;
					}

					progress.AddCompletedBytes(read);
					await outputStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}
			}

			progress.Complete();
			await outputStream.FlushAsync(cancellationToken);
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(cleanupRoot))
			{
				try
				{
					Directory.Delete(cleanupRoot, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static async Task<AddonManifest> ReadAddonJsonAsync(string addonJsonPath, string fallbackTitle, CancellationToken cancellationToken)
	{
		var jsonText = await ReadAddonJsonTextAsync(addonJsonPath, cancellationToken);
		if (jsonText.Length > 0 && jsonText[0] == '\uFEFF')
		{
			jsonText = jsonText[1..];
		}

		var options = new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip
		};

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(jsonText, options);
		}
		catch (JsonException ex)
		{
			if (TryParseAddonVdf(jsonText, fallbackTitle, out var vdfManifest))
			{
				return vdfManifest;
			}

			throw new InvalidDataException($"Failed to parse addon.json ({addonJsonPath}): {ex.Message}", ex);
		}

		using var _ = document;

		string? title = null;
		string? description = null;
		string? author = null;
		uint addonVersion = 1;
		var ignore = new List<string>();
		var tags = new List<string>();

		var root = document.RootElement;
		if (root.ValueKind == JsonValueKind.Object)
		{
			if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
			{
				title = titleElement.GetString();
			}

			if (root.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String)
			{
				description = descriptionElement.GetString();
			}

			if (root.TryGetProperty("author", out var authorElement) && authorElement.ValueKind == JsonValueKind.String)
			{
				author = authorElement.GetString();
			}

			if (root.TryGetProperty("version", out var versionElement))
			{
				if (versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetUInt32(out var parsed))
				{
					addonVersion = parsed;
				}
				else if (versionElement.ValueKind == JsonValueKind.String && uint.TryParse(versionElement.GetString(), out parsed))
				{
					addonVersion = parsed;
				}
			}

			if (root.TryGetProperty("ignore", out var ignoreElement) && ignoreElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in ignoreElement.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.String)
					{
						var value = item.GetString();
						if (!string.IsNullOrWhiteSpace(value))
						{
							ignore.Add(value);
						}
					}
				}
			}

			if (root.TryGetProperty("tags", out var tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var item in tagsElement.EnumerateArray())
				{
					if (item.ValueKind == JsonValueKind.String)
					{
						var value = item.GetString();
						if (!string.IsNullOrWhiteSpace(value))
						{
							tags.Add(value.Trim());
						}
					}
				}
			}
		}

		title = string.IsNullOrWhiteSpace(title) ? fallbackTitle : title;
		description ??= string.Empty;
		author ??= string.Empty;

		return new AddonManifest(
			Title: title,
			Description: description,
			Author: author,
			AddonVersion: addonVersion,
			IgnorePatterns: ignore,
			Tags: tags);
	}

	private static async Task<string> ReadAddonJsonTextAsync(string addonJsonPath, CancellationToken cancellationToken)
	{
		var bytes = await File.ReadAllBytesAsync(addonJsonPath, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		var text = DecodeAddonJsonBytes(bytes);
		return text.Contains('\0') ? text.Replace("\0", string.Empty) : text;
	}

	private static string? FindAddonJsonPath(string inputDirectoryFullPath)
	{
		var direct = Path.Combine(inputDirectoryFullPath, "addon.json");
		if (File.Exists(direct))
		{
			return direct;
		}

		string[] candidates;
		try
		{
			candidates = Directory.EnumerateFiles(inputDirectoryFullPath, "*.json", SearchOption.TopDirectoryOnly).ToArray();
		}
		catch
		{
			return null;
		}

		var addonMatch = candidates.FirstOrDefault(file => string.Equals(Path.GetFileName(file), "addon.json", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(addonMatch))
		{
			return addonMatch;
		}

		var dJsonMatch = candidates.FirstOrDefault(file => string.Equals(Path.GetFileName(file), "d.json", StringComparison.OrdinalIgnoreCase));
		if (!string.IsNullOrWhiteSpace(dJsonMatch))
		{
			return dJsonMatch;
		}

		return candidates.Length == 1 ? candidates[0] : null;
	}

	private static string DecodeAddonJsonBytes(byte[] bytes)
	{
		if (bytes.Length == 0)
		{
			return string.Empty;
		}

		if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
		{
			return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
		}

		if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
		{
			return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
		}

		if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
		{
			return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
		}

		if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
		{
			return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
		}

		if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
		{
			return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
		}

		var evenZeros = 0;
		var oddZeros = 0;
		for (var i = 0; i < bytes.Length; i++)
		{
			if (bytes[i] != 0)
			{
				continue;
			}

			if ((i & 1) == 0)
			{
				evenZeros++;
			}
			else
			{
				oddZeros++;
			}
		}

		if (evenZeros > oddZeros * 2)
		{
			return Encoding.BigEndianUnicode.GetString(bytes);
		}

		if (oddZeros > evenZeros * 2)
		{
			return Encoding.Unicode.GetString(bytes);
		}

		return Encoding.UTF8.GetString(bytes);
	}

	private static (string PackInput, string AddonJsonPath, string? CleanupRoot) PrepareAddonJsonPackInput(
		string inputDirectoryFullPath,
		CancellationToken cancellationToken)
	{
		var addonJsonPath = FindAddonJsonPath(inputDirectoryFullPath);
		if (string.IsNullOrWhiteSpace(addonJsonPath) || !File.Exists(addonJsonPath))
		{
			throw new InvalidDataException("GMA packing requires an addon.json (or d.json) in the input folder.");
		}

		var fileName = Path.GetFileName(addonJsonPath);
		var dJsonPath = Path.Combine(inputDirectoryFullPath, "d.json");
		var hasAlternateDJson = File.Exists(dJsonPath) && !PathsEqual(dJsonPath, addonJsonPath);
		if (string.Equals(fileName, "addon.json", StringComparison.OrdinalIgnoreCase) && !hasAlternateDJson)
		{
			return (inputDirectoryFullPath, addonJsonPath, null);
		}

		var guid = Guid.NewGuid().ToString("N");
		var stageRoot = Path.Combine(Path.GetTempPath(), "Stunstick", "Gma", $"stage_{guid}");
		CopyDirectory(inputDirectoryFullPath, stageRoot, cancellationToken);

		var stagedSource = Path.Combine(stageRoot, fileName);
		var stagedAddonJson = Path.Combine(stageRoot, "addon.json");
		if (File.Exists(stagedSource))
		{
			File.Copy(stagedSource, stagedAddonJson, overwrite: true);
			if (!string.Equals(fileName, "addon.json", StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					File.Delete(stagedSource);
				}
				catch
				{
				}
			}
		}

		if (hasAlternateDJson)
		{
			var stagedDJson = Path.Combine(stageRoot, "d.json");
			if (File.Exists(stagedDJson))
			{
				try
				{
					File.Delete(stagedDJson);
				}
				catch
				{
				}
			}
		}

		return (stageRoot, stagedAddonJson, stageRoot);
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
	{
		var sourceFullPath = Path.GetFullPath(sourceDirectory);
		if (!Directory.Exists(sourceFullPath))
		{
			throw new DirectoryNotFoundException($"Source folder not found: \"{sourceFullPath}\"");
		}

		var destinationFullPath = Path.GetFullPath(destinationDirectory);
		Directory.CreateDirectory(destinationFullPath);

		CopyRecursive(sourceFullPath, destinationFullPath, cancellationToken);

		static void CopyRecursive(string sourcePath, string destinationPath, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Directory.CreateDirectory(destinationPath);

			foreach (var directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.TopDirectoryOnly))
			{
				ct.ThrowIfCancellationRequested();
				var name = Path.GetFileName(directory);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				CopyRecursive(directory, Path.Combine(destinationPath, name), ct);
			}

			foreach (var file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.TopDirectoryOnly))
			{
				ct.ThrowIfCancellationRequested();
				var name = Path.GetFileName(file);
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}

				File.Copy(file, Path.Combine(destinationPath, name), overwrite: true);
			}
		}
	}

	private static bool TryParseAddonVdf(string text, string fallbackTitle, out AddonManifest manifest)
	{
		manifest = null!;
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		Stunstick.Core.Steam.VdfObject root;
		try
		{
			root = Stunstick.Core.Steam.VdfParser.Parse(text);
		}
		catch
		{
			return false;
		}

		if (TryGetObjectIgnoreCase(root, "addon", out var addonRoot))
		{
			root = addonRoot;
		}

		var title = GetStringIgnoreCase(root, "title");
		var description = GetStringIgnoreCase(root, "description");
		var author = GetStringIgnoreCase(root, "author");

		uint addonVersion = 1;
		var versionText = GetStringIgnoreCase(root, "version");
		if (!string.IsNullOrWhiteSpace(versionText) && uint.TryParse(versionText, out var parsed))
		{
			addonVersion = parsed;
		}

		var ignore = new List<string>();
		if (TryGetValueIgnoreCase(root, "ignore", out var ignoreValue))
		{
			AddVdfStringValues(ignore, ignoreValue);
		}

		var tags = new List<string>();
		if (TryGetValueIgnoreCase(root, "tags", out var tagsValue))
		{
			AddVdfStringValues(tags, tagsValue);
		}

		title = string.IsNullOrWhiteSpace(title) ? fallbackTitle : title;
		description ??= string.Empty;
		author ??= string.Empty;

		manifest = new AddonManifest(
			Title: title,
			Description: description,
			Author: author,
			AddonVersion: addonVersion,
			IgnorePatterns: ignore,
			Tags: tags);
		return true;
	}

	private static bool TryGetObjectIgnoreCase(Stunstick.Core.Steam.VdfObject root, string key, out Stunstick.Core.Steam.VdfObject value)
	{
		value = null!;
		foreach (var (name, raw) in root.Properties)
		{
			if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (raw is Stunstick.Core.Steam.VdfObject obj)
			{
				value = obj;
				return true;
			}
		}

		return false;
	}

	private static bool TryGetValueIgnoreCase(Stunstick.Core.Steam.VdfObject root, string key, out Stunstick.Core.Steam.VdfValue value)
	{
		value = null!;
		foreach (var (name, raw) in root.Properties)
		{
			if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
			{
				value = raw;
				return true;
			}
		}

		return false;
	}

	private static string? GetStringIgnoreCase(Stunstick.Core.Steam.VdfObject root, string key)
	{
		foreach (var (name, raw) in root.Properties)
		{
			if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (raw is Stunstick.Core.Steam.VdfString s)
			{
				return s.Value;
			}
		}

		return null;
	}

	private static void AddVdfStringValues(List<string> target, Stunstick.Core.Steam.VdfValue value)
	{
		switch (value)
		{
			case Stunstick.Core.Steam.VdfString s:
				AddDelimitedStrings(target, s.Value);
				break;
			case Stunstick.Core.Steam.VdfObject obj:
				foreach (var raw in obj.Properties.Values)
				{
					if (raw is Stunstick.Core.Steam.VdfString valueString)
					{
						AddDelimitedStrings(target, valueString.Value);
					}
				}
				break;
		}
	}

	private static void AddDelimitedStrings(List<string> target, string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return;
		}

		var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0)
		{
			var trimmed = raw.Trim();
			if (!string.IsNullOrWhiteSpace(trimmed))
			{
				target.Add(trimmed);
			}

			return;
		}

		foreach (var part in parts)
		{
			var trimmed = part.Trim();
			if (!string.IsNullOrWhiteSpace(trimmed))
			{
				target.Add(trimmed);
			}
		}
	}

	private static bool PathsEqual(string left, string right)
	{
		try
		{
			var fullLeft = Path.GetFullPath(left);
			var fullRight = Path.GetFullPath(right);
			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			return string.Equals(fullLeft, fullRight, comparison);
		}
		catch
		{
			return false;
		}
	}

	private static bool IsIgnored(string relativePath, IReadOnlyList<string> ignorePatterns, StringComparison comparison)
	{
		if (ignorePatterns.Count == 0)
		{
			return false;
		}

		var fileName = Path.GetFileName(relativePath);

		foreach (var raw in ignorePatterns)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				continue;
			}

			var pattern = raw.Replace('\\', '/').TrimStart('/');
			if (pattern.Length == 0)
			{
				continue;
			}

			if (pattern.EndsWith('/'))
			{
				var prefix = pattern.TrimEnd('/');
				if (relativePath.StartsWith(prefix + "/", comparison))
				{
					return true;
				}

				continue;
			}

			var isPathPattern = pattern.Contains('/');
			var haystack = isPathPattern ? relativePath : fileName;
			if (MatchesWildcard(haystack, pattern, comparison))
			{
				return true;
			}
		}

		return false;
	}

	private static bool MatchesWildcard(string text, string pattern, StringComparison comparison)
	{
		var textIndex = 0;
		var patternIndex = 0;
		var starIndex = -1;
		var matchIndex = 0;

		while (textIndex < text.Length)
		{
			if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || CharsEqual(text[textIndex], pattern[patternIndex], comparison)))
			{
				textIndex++;
				patternIndex++;
				continue;
			}

			if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
			{
				starIndex = patternIndex;
				matchIndex = textIndex;
				patternIndex++;
				continue;
			}

			if (starIndex != -1)
			{
				patternIndex = starIndex + 1;
				matchIndex++;
				textIndex = matchIndex;
				continue;
			}

			return false;
		}

		while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
		{
			patternIndex++;
		}

		return patternIndex == pattern.Length;
	}

	private static bool CharsEqual(char a, char b, StringComparison comparison)
	{
		if (comparison == StringComparison.OrdinalIgnoreCase)
		{
			return char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
		}

		return a == b;
	}

	private static async Task<uint> ComputeCrc32Async(string path, byte[] buffer, ProgressReporter progress, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: DefaultBufferSize,
			useAsync: true);

		var crc = Crc32.InitialValue;
		while (true)
		{
			var read = await stream.ReadAsync(buffer, cancellationToken);
			if (read <= 0)
			{
				break;
			}

			progress.AddCompletedBytes(read);
			crc = Crc32.Update(crc, buffer.AsSpan(0, read));
		}

		return Crc32.Finalize(crc);
	}

	private static void WriteNullTerminatedString(BinaryWriter writer, string value)
	{
		var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
		writer.Write(bytes);
		writer.Write((byte)0);
	}

	private static string EnsureTrailingSeparator(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return path;
		}

		return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
	}

	private static async Task PackWithGmadAsync(PackRequest request, ToolchainLauncher toolchainLauncher, CancellationToken cancellationToken)
	{
		var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		var inputDirectoryFullPath = Path.GetFullPath(request.InputDirectory);
		var outputFullPath = Path.GetFullPath(request.OutputPackagePath);

		request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "GMA", Message: "Resolving GMAD..."));

		var gmadPath = ResolveGmadPath(request);
		if (string.IsNullOrWhiteSpace(gmadPath))
		{
			throw new FileNotFoundException("GMAD not found. Pass --gmad or install Garry's Mod via Steam.");
		}

		if (!File.Exists(gmadPath))
		{
			throw new FileNotFoundException("GMAD not found.", gmadPath);
		}

		var args = new List<string>();
		args.AddRange(ToolArgumentSplitter.Split(request.DirectOptions));
		args.Add("create");
		var (packInput, _, cleanupRoot) = PrepareAddonJsonPackInput(inputDirectoryFullPath, cancellationToken);
		try
		{
			args.Add("-folder");
			args.Add(packInput);
			args.Add("-out");
			args.Add(outputFullPath);

			request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "GMA", Message: "Running GMAD..."));

			var exitCode = await toolchainLauncher.LaunchExternalToolAsync(
				toolPath: gmadPath,
				toolArguments: args,
				wineOptions: request.WineOptions ?? new WineOptions(),
				steamRootOverride: request.SteamRoot,
				waitForExit: true,
				cancellationToken);

			if (exitCode != 0)
			{
				if (request.IgnoreWhitelistWarnings)
				{
					request.Progress?.Report(new StunstickProgress("Pack", 0, 0, CurrentItem: "GMA", Message: $"GMAD exited with {exitCode} (ignored whitelist warnings)."));
					return;
				}

				throw new InvalidDataException($"GMAD failed with exit code {exitCode}.");
			}
		}
		finally
		{
			if (!string.IsNullOrWhiteSpace(cleanupRoot))
			{
				try
				{
					Directory.Delete(cleanupRoot, recursive: true);
				}
				catch
				{
				}
			}
		}
	}

	private static string? ResolveGmadPath(PackRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.GmadPath))
		{
			return request.GmadPath;
		}

		string? gameDirectory = request.GameDirectory;
		if (string.IsNullOrWhiteSpace(gameDirectory) && request.SteamAppId is not null)
		{
			var preset = ToolchainDiscovery.FindSteamPreset(request.SteamAppId.Value, request.SteamRoot);
			if (preset is not null)
			{
				if (!string.IsNullOrWhiteSpace(preset.GmadPath))
				{
					return preset.GmadPath;
				}

				gameDirectory = preset.GameDirectory;
			}
		}

		if (!string.IsNullOrWhiteSpace(gameDirectory))
		{
			var gmad = ToolchainDiscovery.FindGmadPath(gameDirectory);
			if (!string.IsNullOrWhiteSpace(gmad))
			{
				return gmad;
			}
		}

		var gmodPreset = ToolchainDiscovery.FindSteamPreset(GarrysModAppId, request.SteamRoot);
		if (gmodPreset is null)
		{
			return null;
		}

		return ToolchainDiscovery.FindGmadPath(gmodPreset.GameDirectory);
	}
}
