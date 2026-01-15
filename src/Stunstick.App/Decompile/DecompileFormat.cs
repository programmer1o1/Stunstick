namespace Stunstick.App.Decompile;

internal static class DecompileFormat
{
	private static readonly Lazy<string> HeaderComment = new(BuildHeaderComment);

	public static string GetTimePrefix(DecompileOptions options)
	{
		options ??= new DecompileOptions();
		return options.StricterFormat ? "time " : "  time ";
	}

	public static string GetHeaderCommentLine()
	{
		return "// " + HeaderComment.Value;
	}

	public static void WriteHeaderComment(TextWriter writer, DecompileOptions options)
	{
		options ??= new DecompileOptions();
		if (options.StricterFormat || !options.WriteDebugInfoFiles)
		{
			return;
		}

		writer.WriteLine(GetHeaderCommentLine());
	}

	public static async Task WriteHeaderCommentAsync(TextWriter writer, DecompileOptions options)
	{
		options ??= new DecompileOptions();
		if (options.StricterFormat || !options.WriteDebugInfoFiles)
		{
			return;
		}

		await writer.WriteLineAsync(GetHeaderCommentLine());
	}

	private static string BuildHeaderComment()
	{
		var version = typeof(DecompileFormat).Assembly.GetName().Version;
		return version is null
			? "Created by Stunstick"
			: $"Created by Stunstick {version.ToString(2)}";
	}
}
