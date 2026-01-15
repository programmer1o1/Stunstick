using System.Text.Json;

namespace Stunstick.App.Workshop;

internal static class SteamWorkshopClient
{
	private static readonly HttpClient Client = new(new HttpClientHandler
	{
		AutomaticDecompression = System.Net.DecompressionMethods.All
	})
	{
		Timeout = TimeSpan.FromSeconds(10)
	};

	public static async Task<WorkshopItemDetails?> TryGetPublishedFileDetailsAsync(ulong publishedFileId, CancellationToken cancellationToken)
	{
		try
		{
			var content = new FormUrlEncodedContent(new[]
			{
				new KeyValuePair<string, string>("itemcount", "1"),
				new KeyValuePair<string, string>("publishedfileids[0]", publishedFileId.ToString())
			});

			using var response = await Client.PostAsync(
				"https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
				content,
				cancellationToken).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!document.RootElement.TryGetProperty("response", out var responseElement))
			{
				return null;
			}

			if (!responseElement.TryGetProperty("publishedfiledetails", out var detailsArray) ||
				detailsArray.ValueKind != JsonValueKind.Array ||
				detailsArray.GetArrayLength() == 0)
			{
				return null;
			}

			var details = detailsArray[0];
			var title = details.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
				? titleElement.GetString()
				: null;

			DateTimeOffset? updatedAtUtc = null;
			if (TryGetUnixTimeSeconds(details, "time_updated", out var updatedSeconds))
			{
				updatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(updatedSeconds).ToUniversalTime();
			}

			uint? consumerAppId = null;
			if (TryGetUInt32(details, "consumer_app_id", out var parsedConsumerAppId))
			{
				consumerAppId = parsedConsumerAppId;
			}

			string? fileUrl = null;
			if (TryGetString(details, "file_url", out var parsedFileUrl))
			{
				fileUrl = parsedFileUrl;
			}

			string? fileName = null;
			if (TryGetString(details, "filename", out var parsedFileName))
			{
				fileName = parsedFileName;
			}

			long? fileSizeBytes = null;
			if (TryGetInt64(details, "file_size", out var parsedFileSizeBytes))
			{
				fileSizeBytes = parsedFileSizeBytes;
			}

			return new WorkshopItemDetails(publishedFileId, title, updatedAtUtc, consumerAppId, fileUrl, fileName, fileSizeBytes);
		}
		catch
		{
			return null;
		}
	}

	private static bool TryGetUnixTimeSeconds(JsonElement element, string propertyName, out long seconds)
	{
		seconds = 0;

		if (!element.TryGetProperty(propertyName, out var property))
		{
			return false;
		}

		if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out seconds))
		{
			return seconds > 0;
		}

		if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out seconds))
		{
			return seconds > 0;
		}

		return false;
	}

	private static bool TryGetUInt32(JsonElement element, string propertyName, out uint value)
	{
		value = 0;

		if (!element.TryGetProperty(propertyName, out var property))
		{
			return false;
		}

		if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt32(out value))
		{
			return value > 0;
		}

		if (property.ValueKind == JsonValueKind.String && uint.TryParse(property.GetString(), out value))
		{
			return value > 0;
		}

		return false;
	}

	private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
	{
		value = 0;

		if (!element.TryGetProperty(propertyName, out var property))
		{
			return false;
		}

		if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
		{
			return value > 0;
		}

		if (property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value))
		{
			return value > 0;
		}

		return false;
	}

	private static bool TryGetString(JsonElement element, string propertyName, out string? value)
	{
		value = null;

		if (!element.TryGetProperty(propertyName, out var property))
		{
			return false;
		}

		if (property.ValueKind == JsonValueKind.String)
		{
			value = property.GetString();
			return !string.IsNullOrWhiteSpace(value);
		}

		return false;
	}
}
