namespace Stunstick.Desktop;

internal sealed class WorkshopPublishDraft
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

	public string? Name { get; set; }

	public string? AppId { get; set; }
	public string? SteamCmdPath { get; set; }
	public string? SteamCmdUser { get; set; }
	public string? PublishedFileId { get; set; }
	public int VisibilityIndex { get; set; }
	public string? ContentFolder { get; set; }
	public bool StageCleanPayload { get; set; } = true;
	public bool PackToVpkBeforeUpload { get; set; }
	public uint VpkVersion { get; set; } = 1;
	public bool VpkIncludeMd5Sections { get; set; }
	public bool VpkMultiFile { get; set; }
	public string? PreviewFile { get; set; }
	public string? Title { get; set; }
	public string? Description { get; set; }
	public string? ChangeNote { get; set; }
	public string? Tags { get; set; }
	public string? ContentType { get; set; }
	public List<string> ContentTags { get; set; } = new();
	public string? VdfPath { get; set; }
}
