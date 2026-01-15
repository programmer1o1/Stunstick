using Stunstick.Core.Compiler.Qc;

namespace Stunstick.App.Compile;

public sealed record QcDryRunIssue(string Severity, string Message, string? Path = null);

public sealed record QcDryRunResult(bool Success, IReadOnlyList<QcDryRunIssue> Issues);

public static class QcDryRunValidator
{
	public static string NormalizeSeparators(string path) =>
		string.IsNullOrEmpty(path) ? path : path.Replace('\\', Path.DirectorySeparatorChar);

	public static QcDryRunResult Validate(string qcPath)
	{
		var issues = new List<QcDryRunIssue>();

		if (string.IsNullOrWhiteSpace(qcPath))
		{
			return new QcDryRunResult(false, new[] { new QcDryRunIssue("error", "QC path is required.") });
		}

		if (!File.Exists(qcPath))
		{
			return new QcDryRunResult(false, new[] { new QcDryRunIssue("error", $"QC not found: {qcPath}") });
		}

		QcFile qc;
		try
		{
			qc = QcParser.Parse(qcPath);
		}
		catch (Exception ex)
		{
			return new QcDryRunResult(false, new[] { new QcDryRunIssue("error", $"Parse failed: {ex.Message}") });
		}

		var baseDir = Path.GetDirectoryName(Path.GetFullPath(qcPath)) ?? string.Empty;
		var cdDirs = new List<string> { baseDir };
		var cdMaterialDirs = new List<(string Raw, string Abs)>();

		foreach (var cmd in qc.Commands)
		{
			switch (cmd)
			{
				case QcCd cd when !string.IsNullOrWhiteSpace(cd.Directory):
					{
						var path = NormalizeSeparators(cd.Directory);
						cdDirs.Add(Path.GetFullPath(Path.Combine(baseDir, path)));
					}
					break;
				case QcCdMaterials mats:
					foreach (var p in mats.Paths)
					{
						var normalized = NormalizeSeparators(p);
						cdMaterialDirs.Add((Raw: normalized, Abs: Path.GetFullPath(Path.Combine(baseDir, normalized))));
					}
					break;
			}
		}

		foreach (var dir in cdDirs.Skip(1))
		{
			if (!Directory.Exists(dir))
			{
				issues.Add(new QcDryRunIssue("warning", $"$cd directory missing: {dir}", dir));
			}
		}

		foreach (var (raw, abs) in cdMaterialDirs)
		{
			if (Directory.Exists(abs))
			{
				continue;
			}

			// Common layout: author keeps materials under ./materials/...
			var alt = Path.GetFullPath(Path.Combine(baseDir, "materials", raw.TrimStart(Path.DirectorySeparatorChar, '/')));
			if (Directory.Exists(alt))
			{
				continue;
			}

			issues.Add(new QcDryRunIssue("warning", $"$cdmaterials directory missing: {abs}", abs));
		}

		void RequireAsset(string relativePath, string origin)
		{
			if (string.IsNullOrWhiteSpace(relativePath))
			{
				return;
			}

			var normalizedRel = NormalizeSeparators(relativePath);

			if (File.Exists(normalizedRel))
			{
				return;
			}

			foreach (var dir in cdDirs)
			{
				var candidate = Path.GetFullPath(Path.Combine(dir, normalizedRel));
				if (File.Exists(candidate))
				{
					return;
				}
			}

			issues.Add(new QcDryRunIssue("error", $"Missing asset referenced by {origin}: {relativePath}", relativePath));
		}

		foreach (var cmd in qc.Commands)
		{
			switch (cmd)
			{
				case QcBody body:
					RequireAsset(body.SmdPath, "$body");
					break;
				case QcBodyGroup group:
					foreach (var choice in group.Choices)
					{
						if (!string.IsNullOrWhiteSpace(choice.SmdPath))
						{
							RequireAsset(choice.SmdPath, "$bodygroup");
						}
					}
					break;
				case QcSequence seq:
					foreach (var smd in seq.SmdPaths)
					{
						RequireAsset(smd, "$sequence");
					}
					break;
				case QcCollisionModel col:
					RequireAsset(col.SmdPath, "$collisionmodel");
					break;
			}
		}

		// Material check: find all referenced materials in SMDs and confirm .vmt exists in cdmaterials paths.
		var materialNames = CollectMaterials(qc, baseDir);
		if (materialNames.Count > 0 && cdMaterialDirs.Count > 0)
		{
			foreach (var material in materialNames)
			{
				if (!MaterialExists(material, cdMaterialDirs))
				{
					issues.Add(new QcDryRunIssue("warning", $"Material .vmt not found for \"{material}\"", material));
				}
			}
		}

		var success = issues.All(i => i.Severity != "error");
		return new QcDryRunResult(success, issues);
	}

	private static HashSet<string> CollectMaterials(QcFile qc, string baseDir)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var cmd in qc.Commands.OfType<QcBody>())
		{
			var smdPath = Path.Combine(baseDir, NormalizeSeparators(cmd.SmdPath));
			if (!File.Exists(smdPath))
			{
				continue;
			}

			try
			{
				var smd = Core.Compiler.Smd.SmdReader.Read(smdPath);
				foreach (var mat in smd.Triangles.Select(t => t.Material))
				{
					if (!string.IsNullOrWhiteSpace(mat))
					{
						set.Add(mat);
					}
				}
			}
			catch
			{
				// Ignore SMD parse errors here; already surfaced via missing asset checks.
			}
		}

		return set;
	}

	private static bool MaterialExists(string material, List<(string Raw, string Abs)> cdMaterialDirs)
	{
		// Materials are typically "models/props/hammer/hammer" (no extension).
		var rel = NormalizeSeparators(material);
		if (rel.StartsWith("materials" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
		{
			rel = rel.Substring("materials".Length + 1);
		}

		foreach (var (_, abs) in cdMaterialDirs)
		{
			var candidate = Path.Combine(abs, rel) + ".vmt";
			if (File.Exists(candidate))
			{
				return true;
			}
		}

		return false;
	}
}
