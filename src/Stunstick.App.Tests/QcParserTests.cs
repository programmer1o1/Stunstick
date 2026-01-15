using Stunstick.App.Compile;
using Stunstick.Core.Compiler.Qc;
using Stunstick.Core.Compiler.Smd;

namespace Stunstick.App.Tests;

public class QcParserTests
{
	[Fact]
	public void QcParser_ParsesSequenceFpsAndLoop()
	{
		var qcText = """
			$modelname "models/props/test.mdl"
			$body "body" "body.smd"
			$sequence idle "idle.smd" fps 24 loop
			""";

		var temp = FileHelpers.WriteTempFile("test.qc", qcText);
		var qc = QcParser.Parse(temp);

		var seq = Assert.IsType<QcSequence>(qc.Commands.First(c => c is QcSequence));
		Assert.Equal("idle", seq.SequenceName);
		Assert.Equal(24f, seq.Fps);
		Assert.True(seq.Loop);
		Assert.Contains("idle.smd", seq.SmdPaths);
	}

	[Fact]
	public void DryRun_FindsMissingAssets()
	{
		var temp = Directory.CreateTempSubdirectory();
		try
		{
			var qcPath = Path.Combine(temp.FullName, "test.qc");
			File.WriteAllText(qcPath, """
				$cdmaterials "materials"
				$body "body" "body.smd"
				$sequence idle "idle.smd" fps 30
				$collisionmodel "phys.smd"
				""");

			var result = QcDryRunValidator.Validate(qcPath);

			Assert.False(result.Success);
			Assert.Contains(result.Issues, i => i.Severity == "error" && i.Message.Contains("body.smd", StringComparison.OrdinalIgnoreCase));
			Assert.Contains(result.Issues, i => i.Severity == "warning" && i.Message.Contains("cdmaterials", StringComparison.OrdinalIgnoreCase));
		}
		finally
		{
			temp.Delete(recursive: true);
		}
	}

	[Fact]
	public void QcParser_IgnoresBlockSequenceBraces()
	{
		var qcText = """
			$sequence idle
			{
				$animation idle1 idle1.smd
			}
			""";

		var temp = FileHelpers.WriteTempFile("block.qc", qcText);
		var qc = QcParser.Parse(temp);

		var seq = Assert.IsType<QcSequence>(qc.Commands.First(c => c is QcSequence));
		Assert.Equal("idle", seq.SequenceName);
		Assert.Empty(seq.SmdPaths); // block sequences not yet expanded, but braces must not be treated as paths
	}
}

internal static class FileHelpers
{
	public static string WriteTempFile(string name, string contents)
	{
		var dir = Directory.CreateTempSubdirectory();
		var path = Path.Combine(dir.FullName, name);
		File.WriteAllText(path, contents);
		return path;
	}
}
