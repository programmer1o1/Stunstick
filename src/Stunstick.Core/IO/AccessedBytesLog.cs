using System.Globalization;

namespace Stunstick.Core.IO;

public sealed class AccessedBytesLog
{
	public AccessedBytesLog(string displayPath, string containerPath, long containerOffset, long length)
	{
		if (string.IsNullOrWhiteSpace(displayPath))
		{
			throw new ArgumentException("Display path is required.", nameof(displayPath));
		}

		if (string.IsNullOrWhiteSpace(containerPath))
		{
			throw new ArgumentException("Container path is required.", nameof(containerPath));
		}

		if (containerOffset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(containerOffset), "Container offset must be non-negative.");
		}

		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
		}

		DisplayPath = displayPath;
		ContainerPath = containerPath;
		ContainerOffset = containerOffset;
		Length = length;
	}

	public string DisplayPath { get; }
	public string ContainerPath { get; }
	public long ContainerOffset { get; }
	public long Length { get; }

	private readonly object _lock = new();
	private readonly List<ByteRange> _ranges = new();

	public bool HasEntries
	{
		get
		{
			lock (_lock)
			{
				return _ranges.Count > 0;
			}
		}
	}

	public void AddRange(long startOffset, long endOffset)
	{
		if (startOffset < 0 || endOffset < 0)
		{
			return;
		}

		if (endOffset < startOffset)
		{
			return;
		}

		lock (_lock)
		{
			_ranges.Add(new ByteRange(startOffset, endOffset));
		}
	}

	public IReadOnlyList<ByteRange> GetCoalescedRanges()
	{
		List<ByteRange> copy;
		lock (_lock)
		{
			if (_ranges.Count == 0)
			{
				return Array.Empty<ByteRange>();
			}

			copy = new List<ByteRange>(_ranges);
		}

		copy.Sort(static (a, b) =>
		{
			var c = a.Start.CompareTo(b.Start);
			return c != 0 ? c : a.End.CompareTo(b.End);
		});

		var merged = new List<ByteRange>(capacity: copy.Count);
		var current = copy[0];

		for (var i = 1; i < copy.Count; i++)
		{
			var next = copy[i];

			if (next.Start <= current.End + 1)
			{
				current = current with { End = Math.Max(current.End, next.End) };
				continue;
			}

			merged.Add(current);
			current = next;
		}

		merged.Add(current);
		return merged;
	}

	public long GetCoalescedByteCount()
	{
		var ranges = GetCoalescedRanges();
		long total = 0;

		foreach (var range in ranges)
		{
			var len = range.End - range.Start + 1;
			if (len > 0)
			{
				total += len;
			}
		}

		return total;
	}

	public string DescribeCoverage()
	{
		if (Length <= 0)
		{
			return "0 / 0 bytes";
		}

		var accessed = GetCoalescedByteCount();
		var pct = Math.Clamp((double)accessed / Length * 100.0, 0.0, 100.0);
		return string.Format(CultureInfo.InvariantCulture, "{0:N0} / {1:N0} bytes ({2:0.00}%)", accessed, Length, pct);
	}

	public readonly record struct ByteRange(long Start, long End);
}

