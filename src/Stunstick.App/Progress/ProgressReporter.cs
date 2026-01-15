namespace Stunstick.App.Progress;

internal sealed class ProgressReporter
{
	private readonly IProgress<StunstickProgress>? progress;
	private readonly string operation;
	private readonly long totalBytes;
	private readonly long reportIntervalBytes;

	private long completedBytes;
	private long lastReportedBytes;
	private string? currentItem;
	private string? message;

	public ProgressReporter(IProgress<StunstickProgress>? progress, string operation, long totalBytes, long reportIntervalBytes = 1024 * 512)
	{
		this.progress = progress;
		this.operation = operation;
		this.totalBytes = totalBytes;
		this.reportIntervalBytes = reportIntervalBytes;
	}

	public void SetCurrentItem(string? item)
	{
		currentItem = item;
		Report(force: true);
	}

	public void SetMessage(string? value)
	{
		message = value;
		Report(force: true);
	}

	public void AddCompletedBytes(long bytes)
	{
		completedBytes = checked(completedBytes + bytes);

		if (completedBytes - lastReportedBytes >= reportIntervalBytes)
		{
			Report(force: false);
		}
	}

	public void Complete()
	{
		completedBytes = totalBytes;
		Report(force: true);
	}

	private void Report(bool force)
	{
		if (progress is null)
		{
			return;
		}

		if (!force && completedBytes == lastReportedBytes)
		{
			return;
		}

		lastReportedBytes = completedBytes;
		progress.Report(new StunstickProgress(
			Operation: operation,
			CompletedBytes: completedBytes,
			TotalBytes: totalBytes,
			CurrentItem: currentItem,
			Message: message));
	}
}

