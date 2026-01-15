using System.Text;

namespace Stunstick.App.Decompile;

internal static class DecompileFileNames
{
	public static string CreateAnimationSmdRelativePathFileName(
		DecompileOptions options,
		string modelName,
		string animationName,
		HashSet<string> usedRelativePathFileNames)
	{
		options ??= new DecompileOptions();

		var safeName = (animationName ?? string.Empty).Trim('\0').Trim();
		if (safeName.StartsWith("@", StringComparison.Ordinal))
		{
			safeName = safeName[1..];
		}

		// Avoid path traversal from stored names like "a_../...".
		safeName = safeName.Replace('\\', '/');
		safeName = Path.GetFileName(safeName);

		if (string.IsNullOrWhiteSpace(safeName))
		{
			safeName = "anim";
		}

		safeName = RemoveInvalidFileNameChars(safeName);
		if (string.IsNullOrWhiteSpace(safeName))
		{
			safeName = "anim";
		}

		var baseFileName = options.BoneAnimationPlaceInSubfolder
			? safeName
			: $"{modelName}_anim_{safeName}";

		var folder = options.BoneAnimationPlaceInSubfolder ? $"{modelName}_anims" : string.Empty;

		var relativePath = MakeUniqueSmdRelativePath(folder, baseFileName, usedRelativePathFileNames);
		usedRelativePathFileNames.Add(relativePath);
		return relativePath;
	}

	private static string MakeUniqueSmdRelativePath(string folder, string baseFileName, HashSet<string> usedRelativePathFileNames)
	{
		var candidate = CombineRelative(folder, baseFileName + ".smd");
		if (!usedRelativePathFileNames.Contains(candidate))
		{
			return candidate;
		}

		for (var suffix = 1; suffix < 10000; suffix++)
		{
			var withSuffix = CombineRelative(folder, $"{baseFileName}_{suffix:00}.smd");
			if (!usedRelativePathFileNames.Contains(withSuffix))
			{
				return withSuffix;
			}
		}

		return candidate;
	}

	private static string CombineRelative(string folder, string fileName)
	{
		if (string.IsNullOrWhiteSpace(folder))
		{
			return fileName.Replace('\\', '/');
		}

		return Path.Combine(folder, fileName).Replace('\\', '/');
	}

	private static string RemoveInvalidFileNameChars(string value)
	{
		var invalid = Path.GetInvalidFileNameChars();
		var builder = new StringBuilder(value.Length);
		foreach (var c in value)
		{
			if (Array.IndexOf(invalid, c) >= 0)
			{
				continue;
			}

			builder.Append(c);
		}

		return builder.ToString();
	}
}
