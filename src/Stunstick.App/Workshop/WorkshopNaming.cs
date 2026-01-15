using System.Text;

namespace Stunstick.App.Workshop;

public static class WorkshopNaming
{
	public static string BuildOutputBaseName(ulong publishedFileId, WorkshopItemDetails? details, WorkshopDownloadNamingOptions options, string? contentNameBase = null)
	{
		if (options is null)
		{
			options = new WorkshopDownloadNamingOptions();
		}

		var parts = new List<string>(capacity: 3);

		if (options.IncludeTitle && !string.IsNullOrWhiteSpace(details?.Title))
		{
			parts.Add(SanitizeFileName(details!.Title!, options.ReplaceSpacesWithUnderscores));
		}

		if (options.IncludeId)
		{
			parts.Add(publishedFileId.ToString());
		}
		else if (!string.IsNullOrWhiteSpace(contentNameBase))
		{
			parts.Add(SanitizeFileName(contentNameBase, options.ReplaceSpacesWithUnderscores));
		}
		else if (parts.Count == 0)
		{
			parts.Add(publishedFileId.ToString());
		}

		if (options.AppendUpdatedTimestamp && details?.UpdatedAtUtc is not null)
		{
			parts.Add(details.UpdatedAtUtc.Value.UtcDateTime.ToString("yyyyMMdd_HHmmss"));
		}

		var separator = options.ReplaceSpacesWithUnderscores ? "_" : " ";
		var combined = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));
		var sanitized = SanitizeFileName(combined, options.ReplaceSpacesWithUnderscores);

		if (sanitized.Length > 120)
		{
			sanitized = sanitized[..120].Trim().TrimEnd('_', '.', ' ');
		}

		return string.IsNullOrWhiteSpace(sanitized) ? publishedFileId.ToString() : sanitized;
	}

	private static string SanitizeFileName(string input, bool replaceSpacesWithUnderscores)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		var invalid = Path.GetInvalidFileNameChars();
		var sb = new StringBuilder(input.Length);

		var lastWasUnderscore = false;

		foreach (var c in input)
		{
			var output = c;

			if (invalid.Contains(c) || c is '/' or '\\')
			{
				output = '_';
			}
			else if (replaceSpacesWithUnderscores && char.IsWhiteSpace(c))
			{
				output = '_';
			}

			if (replaceSpacesWithUnderscores && output == '_')
			{
				if (sb.Length == 0 || lastWasUnderscore)
				{
					lastWasUnderscore = true;
					continue;
				}

				lastWasUnderscore = true;
				sb.Append('_');
				continue;
			}

			lastWasUnderscore = false;
			sb.Append(output);
		}

		var result = sb.ToString().Trim();
		result = result.TrimEnd('.', ' ');
		return result;
	}
}
