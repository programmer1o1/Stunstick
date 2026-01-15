namespace Stunstick.Core;

public static class Crc32
{
	private const uint Polynomial = 0xEDB88320u;
	private static readonly uint[] Table = CreateTable();

	public const uint InitialValue = 0xFFFFFFFFu;

	public static uint Compute(ReadOnlySpan<byte> data)
	{
		return Finalize(Update(InitialValue, data));
	}

	public static uint Update(uint crc, ReadOnlySpan<byte> data)
	{
		var current = crc;
		foreach (var b in data)
		{
			var tableIndex = (current ^ b) & 0xFFu;
			current = (current >> 8) ^ Table[(int)tableIndex];
		}

		return current;
	}

	public static uint Finalize(uint crc)
	{
		return crc ^ 0xFFFFFFFFu;
	}

	private static uint[] CreateTable()
	{
		var table = new uint[256];
		for (uint i = 0; i < table.Length; i++)
		{
			var value = i;
			for (var bit = 0; bit < 8; bit++)
			{
				value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
			}

			table[i] = value;
		}

		return table;
	}
}
