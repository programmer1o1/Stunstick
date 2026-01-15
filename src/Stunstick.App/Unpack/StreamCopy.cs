namespace Stunstick.App.Unpack;

internal static class StreamCopy
{
	public static async Task CopyExactBytesAsync(Stream input, Stream output, long bytesToCopy, CancellationToken cancellationToken, Action<int>? onBytesCopied = null)
	{
		await CopyExactBytesAsync(input, output, bytesToCopy, crc32: null, cancellationToken, onBytesCopied);
	}

	public static async Task<uint> CopyExactBytesAsync(Stream input, Stream output, long bytesToCopy, uint crc32, CancellationToken cancellationToken, Action<int>? onBytesCopied = null)
	{
		return await CopyExactBytesAsync(input, output, bytesToCopy, crc32: (uint?)crc32, cancellationToken, onBytesCopied);
	}

	private static async Task<uint> CopyExactBytesAsync(Stream input, Stream output, long bytesToCopy, uint? crc32, CancellationToken cancellationToken, Action<int>? onBytesCopied)
	{
		if (bytesToCopy < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(bytesToCopy));
		}

		var buffer = new byte[1024 * 128];
		long remaining = bytesToCopy;
		var crc = crc32;

		while (remaining > 0)
		{
			var toRead = (int)Math.Min(buffer.Length, remaining);
			var read = await input.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while copying entry data.");
			}

			if (crc is not null)
			{
				crc = Stunstick.Core.Crc32.Update(crc.Value, buffer.AsSpan(0, read));
			}

			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
			onBytesCopied?.Invoke(read);
			remaining -= read;
		}

		return crc ?? 0;
	}
}
