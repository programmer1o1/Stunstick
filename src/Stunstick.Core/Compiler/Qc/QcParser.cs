using System.Collections.Immutable;
using System.Globalization;

namespace Stunstick.Core.Compiler.Qc;

public static class QcParser
{
	public static QcFile Parse(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException("QC file not found.", path);
		}

		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		return ParseInternal(Path.GetFullPath(path), visited);
	}

	private static QcFile ParseInternal(string path, HashSet<string> visited)
	{
		if (!visited.Add(path))
		{
			throw new InvalidDataException($"QC include loop detected at \"{path}\".");
		}

		var lines = File.ReadAllLines(path);
		var commands = new List<QcCommand>();
		for (var i = 0; i < lines.Length; i++)
		{
			var line = StripComments(lines[i]);
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var tokens = Tokenize(line);
			if (tokens.Count == 0)
			{
				continue;
			}

			var directive = tokens[0];
			if (!directive.StartsWith('$'))
			{
				continue;
			}

			switch (directive.ToLowerInvariant())
			{
				case "$modelname" when tokens.Count >= 2:
					commands.Add(new QcModelName(tokens[1]) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$cdmaterials" when tokens.Count >= 2:
					commands.Add(new QcCdMaterials(tokens.Skip(1).ToImmutableArray()) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$cd" when tokens.Count >= 2:
					commands.Add(new QcCd(tokens[1]) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$body" when tokens.Count >= 3:
					commands.Add(new QcBody(tokens[1], tokens[2]) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$bodygroup" when tokens.Count >= 2:
					{
						var (choices, consumed) = ParseBodyGroup(lines, ref i);
						commands.Add(new QcBodyGroup(tokens[1], choices.ToImmutableArray()) { RawArgs = tokens.ToImmutableArray() });
						if (consumed > 0)
						{
							i += consumed;
						}
					}
					break;

				case "$sequence" when tokens.Count >= 2:
					commands.Add(ParseSequence(tokens));
					break;

				case "$collisionmodel" when tokens.Count >= 2:
					commands.Add(new QcCollisionModel(tokens[1]) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$surfaceprop" when tokens.Count >= 2:
					commands.Add(new QcSurfaceProp(tokens[1]) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$staticprop":
					commands.Add(new QcStaticProp(true) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$origin" when tokens.Count >= 4:
					commands.Add(new QcOrigin(ParseFloat(tokens[1]), ParseFloat(tokens[2]), ParseFloat(tokens[3])) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$scale" when tokens.Count >= 2:
					commands.Add(new QcScale(ParseFloat(tokens[1])) { RawArgs = tokens.ToImmutableArray() });
					break;

				case "$include" when tokens.Count >= 2:
					{
						var includePath = ResolveInclude(path, tokens[1]);
						var included = ParseInternal(includePath, visited);
						commands.AddRange(included.Commands);
					}
					break;

				default:
					commands.Add(new QcUnknown(directive, tokens.Skip(1).ToImmutableArray()) { RawArgs = tokens.ToImmutableArray() });
					break;
			}
		}

		return new QcFile(commands);
	}

	private static (List<QcBodyGroupChoice> Choices, int Consumed) ParseBodyGroup(string[] lines, ref int lineIndex)
	{
		var choices = new List<QcBodyGroupChoice>();
		var consumed = 0;

		// Consume lines until matching }
		for (var i = lineIndex + 1; i < lines.Length; i++)
		{
			consumed++;
			var stripped = StripComments(lines[i]);
			if (string.IsNullOrWhiteSpace(stripped))
			{
				continue;
			}

			if (stripped.TrimStart().StartsWith("}", StringComparison.Ordinal))
			{
				break;
			}

			var tokens = Tokenize(stripped);
			if (tokens.Count == 0)
			{
				continue;
			}

			if (tokens[0].Equals("studio", StringComparison.OrdinalIgnoreCase) && tokens.Count >= 2)
			{
				choices.Add(new QcBodyGroupChoice(tokens.Count >= 3 ? tokens[1] : Path.GetFileNameWithoutExtension(tokens[1]), tokens[^1]));
			}
			else if (tokens[0].Equals("blank", StringComparison.OrdinalIgnoreCase))
			{
				choices.Add(new QcBodyGroupChoice("blank", string.Empty));
			}
		}

		return (choices, consumed);
	}

	private static QcSequence ParseSequence(IReadOnlyList<string> tokens)
	{
		var name = tokens[1];
		var smds = new List<string>();
		var fps = 30f;
		var loop = false;

		for (var i = 2; i < tokens.Count; i++)
		{
			var t = tokens[i];
			if (t is "{" or "}")
			{
				break;
			}

			if (t.Equals("fps", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count && float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFps))
			{
				fps = parsedFps;
				i++;
				continue;
			}

			if (t.Equals("loop", StringComparison.OrdinalIgnoreCase))
			{
				loop = true;
				continue;
			}

			if (t.StartsWith("$", StringComparison.Ordinal))
			{
				// Stop at next directive on the same line.
				break;
			}

			smds.Add(t);
		}

		return new QcSequence(name, smds.ToImmutableArray(), fps, loop) { RawArgs = tokens.ToImmutableArray() };
	}

	private static string StripComments(string line)
	{
		var idx = line.IndexOf("//", StringComparison.Ordinal);
		return idx >= 0 ? line[..idx] : line;
	}

	private static List<string> Tokenize(string line)
	{
		var tokens = new List<string>();
		var current = new List<char>();
		var inQuote = false;

		for (var i = 0; i < line.Length; i++)
		{
			var c = line[i];
			if (c == '\"')
			{
				inQuote = !inQuote;
				continue;
			}

			if (!inQuote && char.IsWhiteSpace(c))
			{
				if (current.Count > 0)
				{
					tokens.Add(new string(current.ToArray()));
					current.Clear();
				}
				continue;
			}

			if (!inQuote && (c == '{' || c == '}'))
			{
				if (current.Count > 0)
				{
					tokens.Add(new string(current.ToArray()));
					current.Clear();
				}
				tokens.Add(c.ToString());
				continue;
			}

			current.Add(c);
		}

		if (current.Count > 0)
		{
			tokens.Add(new string(current.ToArray()));
		}

		return tokens;
	}

	private static float ParseFloat(string token)
	{
		return float.Parse(token, CultureInfo.InvariantCulture);
	}

	private static string ResolveInclude(string currentQcPath, string includePath)
	{
		var baseDir = Path.GetDirectoryName(currentQcPath) ?? string.Empty;
		var resolved = Path.GetFullPath(Path.Combine(baseDir, includePath.Trim('"')));
		if (!File.Exists(resolved))
		{
			throw new FileNotFoundException("Included QC not found.", resolved);
		}

		return resolved;
	}
}
