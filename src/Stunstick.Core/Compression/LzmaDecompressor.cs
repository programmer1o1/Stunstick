using System.Diagnostics;

namespace Stunstick.Core.Compression;

public static class LzmaDecompressor
{
	public static Task DecompressFileAsync(
		string inputPath,
		string outputPath,
		CancellationToken cancellationToken,
		IProgress<(long InBytes, long OutBytes)>? progress = null)
	{
		if (string.IsNullOrWhiteSpace(inputPath))
		{
			throw new ArgumentException("Input path is required.", nameof(inputPath));
		}

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			throw new ArgumentException("Output path is required.", nameof(outputPath));
		}

		var inputFullPath = Path.GetFullPath(inputPath);
		if (!File.Exists(inputFullPath))
		{
			throw new FileNotFoundException("Input file not found.", inputFullPath);
		}

		var outputFullPath = Path.GetFullPath(outputPath);

		return Task.Run(() =>
		{
			cancellationToken.ThrowIfCancellationRequested();

			var outDir = Path.GetDirectoryName(outputFullPath);
			if (!string.IsNullOrWhiteSpace(outDir))
			{
				Directory.CreateDirectory(outDir);
			}

			using var input = new FileStream(inputFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var output = new FileStream(outputFullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

			Decompress(input, output, cancellationToken, progress);
			output.Flush(true);
		}, cancellationToken);
	}

	public static void Decompress(
		Stream input,
		Stream output,
		CancellationToken cancellationToken,
		IProgress<(long InBytes, long OutBytes)>? progress = null)
	{
		if (input is null)
		{
			throw new ArgumentNullException(nameof(input));
		}

		if (output is null)
		{
			throw new ArgumentNullException(nameof(output));
		}

		var properties = ReadExactBytes(input, 5);
		var outSize = ReadInt64LittleEndian(input);
		if (outSize < 0)
		{
			throw new NotSupportedException("LZMA streams with unknown uncompressed size are not supported.");
		}

		var inSize = input.CanSeek ? Math.Max(0, input.Length - input.Position) : -1;

		var decoder = new SevenZip.Compression.LZMA.Decoder();
		decoder.SetDecoderProperties(properties);

		var adapter = new ProgressAdapter(cancellationToken, progress);
		decoder.Code(input, output, inSize, outSize, adapter);
	}

	private sealed class ProgressAdapter : SevenZip.ICodeProgress
	{
		private readonly CancellationToken cancellationToken;
		private readonly IProgress<(long InBytes, long OutBytes)>? progress;

		public ProgressAdapter(CancellationToken cancellationToken, IProgress<(long InBytes, long OutBytes)>? progress)
		{
			this.cancellationToken = cancellationToken;
			this.progress = progress;
		}

		public void SetProgress(long inSize, long outSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			progress?.Report((inSize, outSize));
		}
	}

	private static byte[] ReadExactBytes(Stream stream, int count)
	{
		if (count <= 0)
		{
			return Array.Empty<byte>();
		}

		var buffer = new byte[count];
		var offset = 0;
		while (offset < count)
		{
			var read = stream.Read(buffer, offset, count - offset);
			if (read <= 0)
			{
				throw new EndOfStreamException($"Unexpected end of stream while reading {count} bytes.");
			}

			offset += read;
		}

		return buffer;
	}

	private static long ReadInt64LittleEndian(Stream stream)
	{
		var buffer = ReadExactBytes(stream, 8);

		ulong value = 0;
		for (var i = 0; i < 8; i++)
		{
			value |= (ulong)buffer[i] << (8 * i);
		}

		if (value > long.MaxValue)
		{
			return -1;
		}

		return (long)value;
	}
}

internal static class SevenZip
{
	public interface ICodeProgress
	{
		void SetProgress(long inSize, long outSize);
	}

	public static class Compression
	{
		public static class RangeCoder
		{
			public sealed class Decoder
			{
				public const uint TopValue = 1u << 24;
				private Stream? stream;
				private uint range;
				private uint code;

				public void Init(Stream stream)
				{
					this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
					code = 0;
					range = 0xFFFFFFFF;

					for (var i = 0; i < 5; i++)
					{
						code = (code << 8) | (byte)ReadByte();
					}
				}

				public uint DecodeDirectBits(int numTotalBits)
				{
					var result = 0u;
					for (var i = numTotalBits; i > 0; i--)
					{
						range >>= 1;
						var t = (code - range) >> 31;
						code -= range & (t - 1);
						result = (result << 1) | (1 - t);

						if (range < TopValue)
						{
							code = (code << 8) | (byte)ReadByte();
							range <<= 8;
						}
					}

					return result;
				}

				public uint DecodeBit(ref ushort prob)
				{
					var bound = (range >> 11) * prob;
					if (code < bound)
					{
						range = bound;
						prob = (ushort)(prob + ((2048 - prob) >> 5));
						if (range < TopValue)
						{
							code = (code << 8) | (byte)ReadByte();
							range <<= 8;
						}

						return 0;
					}

					range -= bound;
					code -= bound;
					prob = (ushort)(prob - (prob >> 5));
					if (range < TopValue)
					{
						code = (code << 8) | (byte)ReadByte();
						range <<= 8;
					}

					return 1;
				}

				private int ReadByte()
				{
					Debug.Assert(stream is not null);
					var value = stream!.ReadByte();
					if (value < 0)
					{
						throw new EndOfStreamException("Unexpected end of stream in LZMA decoder.");
					}

					return value;
				}
			}
		}

		public static class LZMA
		{
			public sealed class Decoder
			{
				private readonly OutWindow outWindow = new();
				private readonly RangeCoderBitDecoder[] isMatchDecoders = new RangeCoderBitDecoder[Base.KNumStates << Base.KNumPosStatesBitsMax];
				private readonly RangeCoderBitDecoder[] isRepDecoders = new RangeCoderBitDecoder[Base.KNumStates];
				private readonly RangeCoderBitDecoder[] isRepG0Decoders = new RangeCoderBitDecoder[Base.KNumStates];
				private readonly RangeCoderBitDecoder[] isRepG1Decoders = new RangeCoderBitDecoder[Base.KNumStates];
				private readonly RangeCoderBitDecoder[] isRepG2Decoders = new RangeCoderBitDecoder[Base.KNumStates];
				private readonly RangeCoderBitDecoder[] isRep0LongDecoders = new RangeCoderBitDecoder[Base.KNumStates << Base.KNumPosStatesBitsMax];

				private readonly LenDecoder lenDecoder = new();
				private readonly LenDecoder repLenDecoder = new();

				private readonly LiteralDecoder literalDecoder = new();

				private readonly uint[] posSlotDecoder = new uint[Base.KNumLenToPosStates];
				private readonly RangeCoderBitTreeDecoder[] posSlotDecoders = new RangeCoderBitTreeDecoder[Base.KNumLenToPosStates];
				private readonly RangeCoderBitDecoder[] posDecoders = new RangeCoderBitDecoder[Base.KNumFullDistances - Base.KEndPosModelIndex];
				private readonly RangeCoderBitTreeDecoder posAlignDecoder = new(Base.KNumAlignBits);

				private uint dictionarySize;
				private uint dictionarySizeCheck;

				private uint posStateMask;

				public Decoder()
				{
					for (var i = 0; i < Base.KNumLenToPosStates; i++)
					{
						posSlotDecoders[i] = new RangeCoderBitTreeDecoder(Base.KNumPosSlotBits);
					}
				}

				public void SetDecoderProperties(byte[] properties)
				{
					if (properties is null)
					{
						throw new ArgumentNullException(nameof(properties));
					}

					if (properties.Length < 5)
					{
						throw new ArgumentException("LZMA properties must be 5 bytes.", nameof(properties));
					}

					var prop0 = properties[0];
					if (prop0 >= 9 * 5 * 5)
					{
						throw new InvalidDataException("Invalid LZMA properties.");
					}

					var lc = prop0 % 9;
					var remainder = prop0 / 9;
					var lp = remainder % 5;
					var pb = remainder / 5;

					var dict = 0u;
					for (var i = 0; i < 4; i++)
					{
						dict |= (uint)properties[1 + i] << (8 * i);
					}

					SetDictionarySize(dict);
					SetLcLpPb(lc, lp, pb);
				}

				private void SetDictionarySize(uint dictionarySize)
				{
					if (dictionarySize < 1)
					{
						throw new InvalidDataException("Invalid LZMA dictionary size.");
					}

					this.dictionarySize = dictionarySize;
					dictionarySizeCheck = Math.Max(this.dictionarySize, 1u);
					outWindow.Create(Math.Max(dictionarySizeCheck, 1u << 12));
				}

				private void SetLcLpPb(int lc, int lp, int pb)
				{
					if (lc > Base.KNumLitContextBitsMax || lp > Base.KNumLitPosStatesBitsMax || pb > Base.KNumPosStatesBitsMax)
					{
						throw new InvalidDataException("Invalid LZMA LC/LP/PB properties.");
					}

					literalDecoder.Create(lp, lc);
					lenDecoder.Create(pb);
					repLenDecoder.Create(pb);
					posStateMask = (uint)((1 << pb) - 1);
				}

				public void Code(Stream inStream, Stream outStream, long inSize, long outSize, ICodeProgress? progress)
				{
					Init(inStream, outStream);

					var state = Base.StateInit();
					var rep0 = 0u;
					var rep1 = 0u;
					var rep2 = 0u;
					var rep3 = 0u;

					long nowPos64 = 0;
					long outSize64 = outSize;
					if (outSize64 < 0)
					{
						throw new NotSupportedException("Unknown output size is not supported.");
					}

					var decoder = new RangeCoder.Decoder();
					decoder.Init(inStream);

					while (nowPos64 < outSize64)
					{
						progress?.SetProgress(inStream.CanSeek ? inStream.Position : inSize, nowPos64);

						var posState = (uint)nowPos64 & posStateMask;
						if (isMatchDecoders[(state << Base.KNumPosStatesBitsMax) + posState].Decode(decoder) == 0)
						{
							var prevByte = outWindow.GetByte(0);
							var decoded = literalDecoder.Decode(decoder, (uint)nowPos64, prevByte, outWindow.GetByte(rep0));
							outWindow.PutByte(decoded);
							state = Base.StateUpdateChar(state);
							nowPos64++;
							continue;
						}

						uint len;
						if (isRepDecoders[state].Decode(decoder) == 1)
						{
							if (isRepG0Decoders[state].Decode(decoder) == 0)
							{
								if (isRep0LongDecoders[(state << Base.KNumPosStatesBitsMax) + posState].Decode(decoder) == 0)
								{
									state = Base.StateUpdateShortRep(state);
									outWindow.PutByte(outWindow.GetByte(rep0));
									nowPos64++;
									continue;
								}
							}
							else
							{
								uint distance;
								if (isRepG1Decoders[state].Decode(decoder) == 0)
								{
									distance = rep1;
								}
								else
								{
									if (isRepG2Decoders[state].Decode(decoder) == 0)
									{
										distance = rep2;
									}
									else
									{
										distance = rep3;
										rep3 = rep2;
									}

									rep2 = rep1;
								}

								rep1 = rep0;
								rep0 = distance;
							}

							len = repLenDecoder.Decode(decoder, posState) + Base.KMatchMinLen;
							state = Base.StateUpdateRep(state);
						}
						else
						{
							rep3 = rep2;
							rep2 = rep1;
							rep1 = rep0;
							len = lenDecoder.Decode(decoder, posState) + Base.KMatchMinLen;
							state = Base.StateUpdateMatch(state);
							var posSlot = posSlotDecoders[Base.GetLenToPosState(len)].Decode(decoder);
							if (posSlot >= Base.KStartPosModelIndex)
							{
								var numDirectBits = (int)((posSlot >> 1) - 1);
								rep0 = (2 | (posSlot & 1)) << numDirectBits;
								if (posSlot < Base.KEndPosModelIndex)
								{
									rep0 += RangeCoderBitTreeDecoder.ReverseDecode(posDecoders, rep0 - posSlot - 1, decoder, numDirectBits);
								}
								else
								{
									rep0 += decoder.DecodeDirectBits(numDirectBits - Base.KNumAlignBits) << Base.KNumAlignBits;
									rep0 += posAlignDecoder.ReverseDecode(decoder);
								}
							}
							else
							{
								rep0 = posSlot;
							}
						}

						if (rep0 >= nowPos64 || rep0 >= dictionarySizeCheck)
						{
							throw new InvalidDataException("Invalid LZMA data (bad distance).");
						}

						outWindow.CopyBlock(rep0, len);
						nowPos64 += len;
					}

					outWindow.Flush();
				}

				private void Init(Stream inStream, Stream outStream)
				{
					outWindow.Init(outStream);

					for (var i = 0; i < Base.KNumStates; i++)
					{
						for (var j = 0; j <= posStateMask; j++)
						{
							isMatchDecoders[(i << Base.KNumPosStatesBitsMax) + j].Init();
							isRep0LongDecoders[(i << Base.KNumPosStatesBitsMax) + j].Init();
						}

						isRepDecoders[i].Init();
						isRepG0Decoders[i].Init();
						isRepG1Decoders[i].Init();
						isRepG2Decoders[i].Init();
					}

					literalDecoder.Init();
					lenDecoder.Init();
					repLenDecoder.Init();

					for (var i = 0; i < Base.KNumLenToPosStates; i++)
					{
						posSlotDecoders[i].Init();
					}

					for (var i = 0; i < Base.KNumFullDistances - Base.KEndPosModelIndex; i++)
					{
						posDecoders[i].Init();
					}

					posAlignDecoder.Init();
				}
			}

			private static class Base
			{
				public const int KNumStates = 12;
				public const int KNumPosSlotBits = 6;
				public const int KNumLenToPosStates = 4;
				public const int KMatchMinLen = 2;
				public const int KNumAlignBits = 4;
				public const uint KAlignTableSize = 1u << KNumAlignBits;
				public const uint KAlignMask = KAlignTableSize - 1;
				public const uint KStartPosModelIndex = 4;
				public const uint KEndPosModelIndex = 14;
				public const uint KNumFullDistances = 1u << ((int)KEndPosModelIndex / 2);
				public const int KNumLitPosStatesBitsMax = 4;
				public const int KNumLitContextBitsMax = 8;
				public const int KNumPosStatesBitsMax = 4;
				public const int KNumPosStatesBitsMaxMask = (1 << KNumPosStatesBitsMax) - 1;

				public static uint StateInit() => 0;

				public static uint StateUpdateChar(uint index)
				{
					if (index < 4)
					{
						return 0;
					}

					if (index < 10)
					{
						return index - 3;
					}

					return index - 6;
				}

				public static uint StateUpdateMatch(uint index) => index < 7 ? 7u : 10u;

				public static uint StateUpdateRep(uint index) => index < 7 ? 8u : 11u;

				public static uint StateUpdateShortRep(uint index) => index < 7 ? 9u : 11u;

				public static uint GetLenToPosState(uint len)
				{
					len -= KMatchMinLen;
					if (len < KNumLenToPosStates)
					{
						return len;
					}

					return KNumLenToPosStates - 1;
				}
			}

			private struct RangeCoderBitDecoder
			{
				private ushort prob;

				public void Init()
				{
					prob = 1024;
				}

				public uint Decode(RangeCoder.Decoder decoder)
				{
					return decoder.DecodeBit(ref prob);
				}
			}

			private sealed class RangeCoderBitTreeDecoder
			{
				private readonly RangeCoderBitDecoder[] models;
				private readonly int numBitLevels;

				public RangeCoderBitTreeDecoder(int numBitLevels)
				{
					this.numBitLevels = numBitLevels;
					models = new RangeCoderBitDecoder[1 << numBitLevels];
				}

				public void Init()
				{
					for (uint i = 1; i < (1 << numBitLevels); i++)
					{
						models[i].Init();
					}
				}

				public uint Decode(RangeCoder.Decoder decoder)
				{
					uint m = 1;
					for (var i = numBitLevels; i > 0; i--)
					{
						m = (m << 1) + models[m].Decode(decoder);
					}

					return m - (uint)(1 << numBitLevels);
				}

				public uint ReverseDecode(RangeCoder.Decoder decoder)
				{
					uint m = 1;
					uint symbol = 0;
					for (var i = 0; i < numBitLevels; i++)
					{
						var bit = models[m].Decode(decoder);
						m = (m << 1) + bit;
						symbol |= bit << i;
					}

					return symbol;
				}

				public static uint ReverseDecode(RangeCoderBitDecoder[] models, uint startIndex, RangeCoder.Decoder decoder, int numBitLevels)
				{
					uint m = 1;
					uint symbol = 0;
					for (var i = 0; i < numBitLevels; i++)
					{
						var bit = models[startIndex + m].Decode(decoder);
						m = (m << 1) + bit;
						symbol |= bit << i;
					}

					return symbol;
				}
			}

			private sealed class LenDecoder
			{
				private RangeCoderBitDecoder choice;
				private RangeCoderBitDecoder choice2;
				private readonly RangeCoderBitTreeDecoder[] lowCoder = new RangeCoderBitTreeDecoder[Base.KNumPosStatesBitsMax];
				private readonly RangeCoderBitTreeDecoder[] midCoder = new RangeCoderBitTreeDecoder[Base.KNumPosStatesBitsMax];
				private readonly RangeCoderBitTreeDecoder highCoder = new(8);

				private uint numPosStates;

				public void Create(int numPosStates)
				{
					this.numPosStates = (uint)1 << numPosStates;
					for (var posState = 0; posState < this.numPosStates; posState++)
					{
						lowCoder[posState] = new RangeCoderBitTreeDecoder(3);
						midCoder[posState] = new RangeCoderBitTreeDecoder(3);
					}
				}

				public void Init()
				{
					choice.Init();
					choice2.Init();
					for (uint posState = 0; posState < numPosStates; posState++)
					{
						lowCoder[posState].Init();
						midCoder[posState].Init();
					}

					highCoder.Init();
				}

				public uint Decode(RangeCoder.Decoder decoder, uint posState)
				{
					if (choice.Decode(decoder) == 0)
					{
						return lowCoder[posState].Decode(decoder);
					}

					uint symbol = 8;
					if (choice2.Decode(decoder) == 0)
					{
						symbol += midCoder[posState].Decode(decoder);
					}
					else
					{
						symbol += 8 + highCoder.Decode(decoder);
					}

					return symbol;
				}
			}

			private sealed class LiteralDecoder
			{
				private Decoder2[] coders = Array.Empty<Decoder2>();
				private int numPrevBits;
				private int numPosBits;
				private uint posMask;

				public void Create(int numPosBits, int numPrevBits)
				{
					if (coders.Length != 0 && this.numPrevBits == numPrevBits && this.numPosBits == numPosBits)
					{
						return;
					}

					this.numPosBits = numPosBits;
					posMask = (uint)((1 << numPosBits) - 1);
					this.numPrevBits = numPrevBits;

					var numStates = 1u << (this.numPrevBits + this.numPosBits);
					coders = new Decoder2[numStates];
					for (uint i = 0; i < numStates; i++)
					{
						coders[i].Create();
					}
				}

				public void Init()
				{
					var numStates = 1u << (numPrevBits + numPosBits);
					for (uint i = 0; i < numStates; i++)
					{
						coders[i].Init();
					}
				}

				private uint GetState(uint pos, byte prevByte)
				{
					return ((pos & posMask) << numPrevBits) + (uint)(prevByte >> (8 - numPrevBits));
				}

				public byte Decode(RangeCoder.Decoder decoder, uint pos, byte prevByte, byte matchByte)
				{
					return coders[GetState(pos, prevByte)].DecodeWithMatchByte(decoder, matchByte);
				}

				private sealed class Decoder2
				{
					private RangeCoderBitDecoder[] decoders = Array.Empty<RangeCoderBitDecoder>();

					public void Create()
					{
						decoders = new RangeCoderBitDecoder[0x300];
					}

					public void Init()
					{
						for (uint i = 0; i < 0x300; i++)
						{
							decoders[i].Init();
						}
					}

					public byte DecodeWithMatchByte(RangeCoder.Decoder decoder, byte matchByte)
					{
						uint symbol = 1;
						do
						{
							var matchBit = (uint)(matchByte >> 7) & 1;
							matchByte <<= 1;
							var bit = decoders[((1 + matchBit) << 8) + symbol].Decode(decoder);
							symbol = (symbol << 1) | bit;
							if (matchBit != bit)
							{
								while (symbol < 0x100)
								{
									symbol = (symbol << 1) | decoders[symbol].Decode(decoder);
								}

								break;
							}
						}
						while (symbol < 0x100);

						return (byte)symbol;
					}
				}
			}

			private sealed class OutWindow
			{
				private byte[] buffer = Array.Empty<byte>();
				private uint pos;
				private uint windowSize;
				private uint streamPos;
				private Stream? stream;

				public void Create(uint windowSize)
				{
					if (this.windowSize != windowSize)
					{
						buffer = new byte[windowSize];
					}

					this.windowSize = windowSize;
					pos = 0;
					streamPos = 0;
				}

				public void Init(Stream stream)
				{
					this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
					streamPos = 0;
					pos = 0;
				}

				public void Flush()
				{
					var size = pos - streamPos;
					if (size == 0)
					{
						return;
					}

					Debug.Assert(stream is not null);
					stream!.Write(buffer, (int)streamPos, (int)size);
					if (pos >= windowSize)
					{
						pos = 0;
					}

					streamPos = pos;
				}

				public void PutByte(byte b)
				{
					buffer[pos++] = b;
					if (pos >= windowSize)
					{
						Flush();
					}
				}

				public byte GetByte(uint distance)
				{
					var idx = pos - distance - 1;
					if (idx >= windowSize)
					{
						idx += windowSize;
					}

					return buffer[idx];
				}

				public void CopyBlock(uint distance, uint len)
				{
					var pos2 = pos - distance - 1;
					if (pos2 >= windowSize)
					{
						pos2 += windowSize;
					}

					for (; len > 0; len--)
					{
						if (pos2 >= windowSize)
						{
							pos2 = 0;
						}

						buffer[pos++] = buffer[pos2++];
						if (pos >= windowSize)
						{
							Flush();
						}
					}
				}
			}
		}
	}
}
