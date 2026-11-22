using System.Globalization;

namespace Stunstick.App.Decompile;

internal static class MdlCorrectiveAnimationWriter
{
	public static async Task<string?> WriteAsync(
		string outputFolder, string animationPath, IReadOnlySet<int> rootBones, CancellationToken cancellationToken)
	{
		using var reader = File.OpenText(Path.Combine(outputFolder, animationPath));
		var lines = new List<string>();
		while (await reader.ReadLineAsync(cancellationToken) is { } line)
		{
			lines.Add(line);
			if (line.Trim() == "skeleton")
			{
				break;
			}
		}

		if (lines.Count == 0 || lines[^1].Trim() != "skeleton" ||
			await reader.ReadLineAsync(cancellationToken) is not { } frame || frame.Trim() != "time 0")
		{
			return null;
		}

		lines.Add(frame);
		var rootCount = 0;
		while (await reader.ReadLineAsync(cancellationToken) is { } row)
		{
			var fields = row.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (fields.Length == 0 || fields[0] == "time" || fields[0] == "end")
			{
				break;
			}

			if (fields.Length == 7 &&
				int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var boneIndex) &&
				rootBones.Contains(boneIndex))
			{
				// Preserve the exported root's first-frame rotation with zero translation.
				lines.Add($"{boneIndex} 0 0 0 {fields[4]} {fields[5]} {fields[6]}");
				rootCount++;
			}
		}

		if (rootCount == 0)
		{
			return null;
		}

		lines.Add("end");
		var name = Path.GetFileNameWithoutExtension(animationPath) + "_corrective_animation.smd";
		await File.WriteAllLinesAsync(Path.Combine(outputFolder, name), lines, cancellationToken);
		return name;
	}
}
