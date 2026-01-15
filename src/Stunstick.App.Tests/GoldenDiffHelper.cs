using System.Text;

namespace Stunstick.App.Tests;

internal static class GoldenDiffHelper
{
	public static void AssertFilesEqualExceptChecksums(string originalPath, string generatedPath, int compareLength, params (int Offset, int Length)[] ignoreSpans)
	{
		var orig = File.ReadAllBytes(originalPath);
		var gen = File.ReadAllBytes(generatedPath);

		Assert.True(orig.Length >= compareLength, $"Original shorter than compare length ({compareLength}).");
		Assert.True(gen.Length >= compareLength, $"Generated shorter than compare length ({compareLength}).");

		for (int i = 0; i < compareLength; i++)
		{
			if (IsIgnored(i, ignoreSpans))
			{
				continue;
			}

			Assert.True(orig[i] == gen[i], $"Byte mismatch at {i}: orig=0x{orig[i]:X2}, gen=0x{gen[i]:X2}");
		}
	}

	private static bool IsIgnored(int index, (int Offset, int Length)[] spans)
	{
		foreach (var (offset, length) in spans)
		{
			if (index >= offset && index < offset + length)
			{
				return true;
			}
		}
		return false;
	}
}
