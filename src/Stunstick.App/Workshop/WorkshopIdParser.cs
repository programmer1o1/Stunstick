using System.Text.RegularExpressions;

namespace Stunstick.App.Workshop;

public static class WorkshopIdParser
{
	public static bool TryParsePublishedFileId(string? input, out ulong publishedFileId)
	{
		publishedFileId = 0;

		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		input = input.Trim();

		if (ulong.TryParse(input, out publishedFileId))
		{
			return true;
		}

		if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
		{
			var idFromQuery = TryGetQueryParameter(uri, "id");
			if (!string.IsNullOrWhiteSpace(idFromQuery) && ulong.TryParse(idFromQuery, out publishedFileId))
			{
				return true;
			}

			var lastSegment = uri.Segments.Length > 0 ? uri.Segments[^1].Trim('/') : null;
			if (!string.IsNullOrWhiteSpace(lastSegment) && ulong.TryParse(lastSegment, out publishedFileId))
			{
				return true;
			}
		}

		var matches = Regex.Matches(input, @"\b(\d{6,})\b");
		for (var i = matches.Count - 1; i >= 0; i--)
		{
			if (ulong.TryParse(matches[i].Groups[1].Value, out publishedFileId))
			{
				return true;
			}
		}

		return false;
	}

	private static string? TryGetQueryParameter(Uri uri, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(uri.Query))
		{
			return null;
		}

		var query = uri.Query;
		if (query.Length > 0 && query[0] == '?')
		{
			query = query[1..];
		}

		var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var part in parts)
		{
			var equalsIndex = part.IndexOf('=', StringComparison.Ordinal);
			if (equalsIndex <= 0)
			{
				continue;
			}

			var name = Uri.UnescapeDataString(part[..equalsIndex]);
			if (!string.Equals(name, parameterName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			return Uri.UnescapeDataString(part[(equalsIndex + 1)..]);
		}

		return null;
	}
}

