namespace Stunstick.Core.IO;

/// <summary>
/// A read-only, seekable view over a contiguous region of another seekable stream.
/// Disposing this stream does not dispose the underlying stream.
/// </summary>
public sealed class BoundedReadOnlyStream : Stream
{
	private readonly Stream baseStream;
	private readonly long startOffset;
	private readonly long length;
	private long position;

	public BoundedReadOnlyStream(Stream baseStream, long startOffset, long length)
	{
		this.baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
		if (!baseStream.CanSeek)
		{
			throw new ArgumentException("Base stream must be seekable.", nameof(baseStream));
		}
		if (!baseStream.CanRead)
		{
			throw new ArgumentException("Base stream must be readable.", nameof(baseStream));
		}
		if (startOffset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(startOffset));
		}
		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		this.startOffset = startOffset;
		this.length = length;
		position = 0;
	}

	public override bool CanRead => true;
	public override bool CanSeek => true;
	public override bool CanWrite => false;
	public override long Length => length;

	public override long Position
	{
		get => position;
		set => Seek(value, SeekOrigin.Begin);
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		ArgumentNullException.ThrowIfNull(buffer);

		if (offset < 0 || count < 0)
		{
			throw new ArgumentOutOfRangeException();
		}
		if (buffer.Length - offset < count)
		{
			throw new ArgumentException("Invalid offset/count for buffer length.");
		}
		if (position >= length)
		{
			return 0;
		}

		var remaining = length - position;
		if (count > remaining)
		{
			count = (int)remaining;
		}

		baseStream.Seek(startOffset + position, SeekOrigin.Begin);
		var read = baseStream.Read(buffer, offset, count);
		position += read;
		return read;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		long newPosition = origin switch
		{
			SeekOrigin.Begin => offset,
			SeekOrigin.Current => position + offset,
			SeekOrigin.End => length + offset,
			_ => throw new ArgumentOutOfRangeException(nameof(origin))
		};

		if (newPosition < 0)
		{
			throw new IOException("Attempted to seek before the beginning of the stream.");
		}
		if (newPosition > length)
		{
			throw new IOException("Attempted to seek beyond the end of the stream.");
		}

		position = newPosition;
		return position;
	}

	public override void Flush()
	{
		// No-op (read-only).
	}

	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

	protected override void Dispose(bool disposing)
	{
		// Intentionally do not dispose baseStream.
		base.Dispose(disposing);
	}
}

