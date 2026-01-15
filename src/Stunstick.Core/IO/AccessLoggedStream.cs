namespace Stunstick.Core.IO;

public sealed class AccessLoggedStream : Stream
{
	public AccessLoggedStream(Stream inner, AccessedBytesLog log)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_log = log ?? throw new ArgumentNullException(nameof(log));

		if (!_inner.CanRead || !_inner.CanSeek)
		{
			throw new ArgumentException("Stream must be readable and seekable.", nameof(inner));
		}
	}

	private readonly Stream _inner;
	private readonly AccessedBytesLog _log;

	public override bool CanRead => _inner.CanRead;
	public override bool CanSeek => _inner.CanSeek;
	public override bool CanWrite => false;
	public override long Length => _inner.Length;

	public override long Position
	{
		get => _inner.Position;
		set => _inner.Position = value;
	}

	public override void Flush()
	{
		// No-op for a read-only stream wrapper.
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		var start = _inner.Position;
		var read = _inner.Read(buffer, offset, count);
		if (read > 0)
		{
			_log.AddRange(start, start + read - 1);
		}

		return read;
	}

	public override int Read(Span<byte> buffer)
	{
		var start = _inner.Position;
		var read = _inner.Read(buffer);
		if (read > 0)
		{
			_log.AddRange(start, start + read - 1);
		}

		return read;
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		return ReadAsyncInternal(buffer, cancellationToken);
	}

	private async ValueTask<int> ReadAsyncInternal(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var start = _inner.Position;
		var read = await _inner.ReadAsync(buffer, cancellationToken);
		if (read > 0)
		{
			_log.AddRange(start, start + read - 1);
		}

		return read;
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		return _inner.Seek(offset, origin);
	}

	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_inner.Dispose();
		}

		base.Dispose(disposing);
	}
}

