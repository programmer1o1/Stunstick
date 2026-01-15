using Stunstick.Core.IO;
using System.Globalization;
using System.Text;

namespace Stunstick.App.Decompile;

internal static class AccessedBytesDebugFileWriter
{
	internal readonly record struct DebugFile(string OutputFileName, AccessedBytesLog Log);

	public static async Task WriteAsync(string debugFolder, DebugFile file, DecompileOptions options, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(debugFolder))
		{
			throw new ArgumentException("Debug folder is required.", nameof(debugFolder));
		}

		if (string.IsNullOrWhiteSpace(file.OutputFileName))
		{
			throw new ArgumentException("Output file name is required.", nameof(file));
		}

		Directory.CreateDirectory(debugFolder);

		var pathFileName = Path.Combine(debugFolder, file.OutputFileName);

		var ranges = file.Log.GetCoalescedRanges();

		await using var outStream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(outStream, Encoding.UTF8);

		DecompileFormat.WriteHeaderComment(writer, options);
		await writer.WriteLineAsync($"// Source: {file.Log.DisplayPath}");
		if (!string.Equals(file.Log.DisplayPath, file.Log.ContainerPath, StringComparison.OrdinalIgnoreCase) || file.Log.ContainerOffset != 0)
		{
			await writer.WriteLineAsync($"// Container: {file.Log.ContainerPath} (offset {file.Log.ContainerOffset.ToString("N0", CultureInfo.InvariantCulture)})");
		}

		await writer.WriteLineAsync("====== File Size ======");
		await writer.WriteLineAsync($"\t{file.Log.Length.ToString("N0", CultureInfo.InvariantCulture)}");
		await writer.WriteLineAsync("====== Access Summary ======");
		await writer.WriteLineAsync($"\t{file.Log.DescribeCoverage()}");
		await writer.WriteLineAsync("====== File Seek Log ======");

		var entries = BuildEntries(file.Log, ranges);
		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await writer.WriteLineAsync($"\t{entry.Start.ToString("N0", CultureInfo.InvariantCulture)} - {entry.End.ToString("N0", CultureInfo.InvariantCulture)} {entry.Description}");
		}

		await writer.WriteLineAsync("========================");
	}

	private static IReadOnlyList<LogEntry> BuildEntries(AccessedBytesLog log, IReadOnlyList<AccessedBytesLog.ByteRange> accessed)
	{
		var results = new List<LogEntry>(capacity: accessed.Count + 32);

		foreach (var range in accessed)
		{
			results.Add(new LogEntry(range.Start, range.End, "[READ]"));
		}

		// Match Crowbar's behavior: report gaps as unread bytes with a byte preview.
		if (log.Length > 0)
		{
			var gaps = ComputeGaps(accessed, log.Length);
			foreach (var gap in gaps)
			{
				var description = BuildUnreadDescription(log, gap.Start, gap.End);
				results.Add(new LogEntry(gap.Start, gap.End, description));
			}
		}

		results.Sort(static (a, b) =>
		{
			var c = a.Start.CompareTo(b.Start);
			return c != 0 ? c : a.End.CompareTo(b.End);
		});

		return results;
	}

	private static IReadOnlyList<AccessedBytesLog.ByteRange> ComputeGaps(IReadOnlyList<AccessedBytesLog.ByteRange> accessed, long length)
	{
		if (length <= 0)
		{
			return Array.Empty<AccessedBytesLog.ByteRange>();
		}

		if (accessed.Count == 0)
		{
			return new[] { new AccessedBytesLog.ByteRange(0, length - 1) };
		}

		var gaps = new List<AccessedBytesLog.ByteRange>();

		var pos = 0L;
		foreach (var range in accessed)
		{
			if (range.Start > pos)
			{
				gaps.Add(new AccessedBytesLog.ByteRange(pos, Math.Min(length - 1, range.Start - 1)));
			}

			pos = Math.Max(pos, range.End + 1);
			if (pos >= length)
			{
				break;
			}
		}

		if (pos < length)
		{
			gaps.Add(new AccessedBytesLog.ByteRange(pos, length - 1));
		}

		return gaps;
	}

	private static string BuildUnreadDescription(AccessedBytesLog log, long start, long end)
	{
		var (preview, allZeroes, fullyScanned) = TryReadBytePreview(log, start, end);
		var label = allZeroes ? "Unread bytes (all zeroes)" : "Unread bytes (non-zero)";
		if (!fullyScanned)
		{
			label = allZeroes ? "Unread bytes (all zeroes; partial scan)" : "Unread bytes (non-zero; partial scan)";
		}

		return $"[ERROR] {label}{preview}";
	}

	private static (string Preview, bool AllZeroes, bool FullyScanned) TryReadBytePreview(AccessedBytesLog log, long start, long end)
	{
		try
		{
				if (start < 0 || end < start || log.Length <= 0)
				{
					return ("", false, true);
				}

				if (!File.Exists(log.ContainerPath))
				{
					return (" [unavailable]", false, true);
				}

			var gapLength = end - start + 1;
				if (gapLength <= 0)
				{
					return ("", false, true);
				}

			const int previewBytesMax = 21; // match Crowbar's behavior (first ~20 bytes)
			var previewBytes = (int)Math.Min(gapLength, previewBytesMax);

			var absoluteStart = log.ContainerOffset + start;
			var absoluteEnd = log.ContainerOffset + end;

				using var stream = new FileStream(log.ContainerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				if (absoluteStart < 0 || absoluteStart >= stream.Length)
				{
					return (" [unavailable]", false, true);
				}

			var maxReadableEnd = Math.Min(absoluteEnd, stream.Length - 1);
			var readableLength = maxReadableEnd - absoluteStart + 1;
				if (readableLength <= 0)
				{
					return (" [unavailable]", false, true);
				}

			previewBytes = (int)Math.Min(previewBytes, readableLength);

			stream.Seek(absoluteStart, SeekOrigin.Begin);
			var bytes = new byte[previewBytes];
			var read = stream.Read(bytes, 0, previewBytes);
				if (read <= 0)
				{
					return (" [unavailable]", false, true);
				}

			var allZeroes = true;
			var sb = new StringBuilder(capacity: 3 * previewBytes + 8);
			sb.Append(" [");
			for (var i = 0; i < read; i++)
			{
				var b = bytes[i];
				sb.Append(' ');
				sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
				if (b != 0)
				{
					allZeroes = false;
				}
			}

			var fullyScanned = true;

			if (readableLength > previewBytesMax)
			{
				sb.Append(" ...");

				// If preview is all zeros, scan ahead (capped) to find non-zero bytes.
				if (allZeroes)
				{
					const int scanLimitBytes = 1024 * 1024;
					var remaining = readableLength - previewBytesMax;
					var toScan = Math.Min(remaining, scanLimitBytes);

					var scanBuffer = new byte[16 * 1024];
					long scanned = 0;
					while (scanned < toScan)
					{
						var chunk = (int)Math.Min(scanBuffer.Length, toScan - scanned);
						var chunkRead = stream.Read(scanBuffer, 0, chunk);
						if (chunkRead <= 0)
						{
							break;
						}

						for (var i = 0; i < chunkRead; i++)
						{
							if (scanBuffer[i] != 0)
							{
								allZeroes = false;
								break;
							}
						}

						if (!allZeroes)
						{
							break;
						}

						scanned += chunkRead;
					}

					if (remaining > scanLimitBytes)
					{
						fullyScanned = false;
					}
				}
				else
				{
					// Non-zero found in preview, don't bother scanning the rest.
					fullyScanned = false;
				}
			}

			sb.Append(" ]");
			return (sb.ToString(), allZeroes, fullyScanned);
		}
		catch
		{
			return (" [unavailable]", false, true);
		}
	}

	private readonly record struct LogEntry(long Start, long End, string Description);
}
