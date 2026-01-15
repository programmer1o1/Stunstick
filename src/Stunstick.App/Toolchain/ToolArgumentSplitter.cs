using System.Text;

namespace Stunstick.App.Toolchain;

internal static class ToolArgumentSplitter
{
	public static IReadOnlyList<string> Split(string? text)
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
}

