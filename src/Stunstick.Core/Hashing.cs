using System.Security.Cryptography;

namespace Stunstick.Core;

public static class Hashing
{
	public static async Task<string> Sha256HexAsync(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read,
			bufferSize: 1024 * 64,
			useAsync: true);

		using var sha256 = SHA256.Create();
		var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	public static byte[] Md5(ReadOnlySpan<byte> data)
	{
		return MD5.HashData(data);
	}

	public static async Task<byte[]> Md5Async(string path, long offset, long length, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		if (offset < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		if (length < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(length));
		}

		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: 1024 * 128,
			useAsync: true);

		if (offset > stream.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(offset));
		}

		stream.Seek(offset, SeekOrigin.Begin);
		return await Md5FromStreamAsync(stream, bytesToRead: length, cancellationToken);
	}

	public static async Task<byte[]> Md5PrefixAsync(string path, long bytesToHash, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		if (bytesToHash < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(bytesToHash));
		}

		await using var stream = new FileStream(
			path,
			FileMode.Open,
			FileAccess.Read,
			FileShare.ReadWrite,
			bufferSize: 1024 * 128,
			useAsync: true);

		if (bytesToHash > stream.Length)
		{
			throw new InvalidDataException("File is smaller than expected while hashing.");
		}

		var toRead = bytesToHash;
		stream.Seek(0, SeekOrigin.Begin);
		return await Md5FromStreamAsync(stream, bytesToRead: toRead, cancellationToken);
	}

	private static async Task<byte[]> Md5FromStreamAsync(Stream stream, long bytesToRead, CancellationToken cancellationToken)
	{
		using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		var buffer = new byte[1024 * 128];

		long remaining = bytesToRead;
		while (remaining > 0)
		{
			var toRead = (int)Math.Min(buffer.Length, remaining);
			var read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
			if (read <= 0)
			{
				throw new EndOfStreamException("Unexpected end of stream while hashing.");
			}

			hasher.AppendData(buffer, 0, read);
			remaining -= read;
		}

		return hasher.GetHashAndReset();
	}
}
