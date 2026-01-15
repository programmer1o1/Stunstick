using Stunstick.Core.Mdl;

namespace Stunstick.App.Toolchain;

internal static class HlmvReplacement
{
	public sealed record ReplacementLaunchInfo(
		string WorkingDirectory,
		string RelativeMdlPath,
		string TempRootDirectory);

	public static ReplacementLaunchInfo Prepare(string sourceMdlPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(sourceMdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(sourceMdlPath));
		}

		var fullSourcePath = Path.GetFullPath(sourceMdlPath);
		if (!File.Exists(fullSourcePath))
		{
			throw new FileNotFoundException("MDL file not found.", fullSourcePath);
		}

		var sourceDirectory = Path.GetDirectoryName(fullSourcePath) ?? string.Empty;
		var fileName = Path.GetFileName(fullSourcePath);
		var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);

		var root = Path.Combine(Path.GetTempPath(), "Stunstick", "hlmv-replacement");
		Directory.CreateDirectory(root);

		var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
		var tempRoot = Path.Combine(root, runId);
		var modelsDir = Path.Combine(tempRoot, "models");
		var viewDir = Path.Combine(modelsDir, "-view");
		Directory.CreateDirectory(viewDir);

		foreach (var file in Directory.EnumerateFiles(sourceDirectory, $"{baseName}.*", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var target = Path.Combine(viewDir, Path.GetFileName(file));
			File.Copy(file, target, overwrite: true);
		}

		var replacementMdlPath = Path.Combine(viewDir, fileName);
		if (!File.Exists(replacementMdlPath))
		{
			throw new FileNotFoundException("Replacement MDL was not created.", replacementMdlPath);
		}

		var internalMdlFileName = $"-view/{fileName}";
		MdlFileNameRewriter.RewriteInternalModelAndAniFileNames(replacementMdlPath, internalMdlFileName);

		return new ReplacementLaunchInfo(
			WorkingDirectory: modelsDir,
			RelativeMdlPath: Path.Combine("-view", fileName),
			TempRootDirectory: tempRoot);
	}
}

