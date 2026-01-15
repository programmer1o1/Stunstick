using System.Linq;
using Stunstick.Core.Compiler.Serialization;
using Stunstick.Core.Mdl;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;

namespace Stunstick.App.Tests;

public class HeaderSerializerTests
{
	[Fact]
	public void MdlHeader_RoundTrips()
	{
		var header = new MdlHeader(
			Id: 0x54534449, // IDST
			Version: 49,
			Checksum: 123456,
			Name: "props/test_model",
			Length: 1337,
			Flags: 0x20,
			BoneCount: 4,
			BoneOffset: 200,
			LocalAnimationCount: 2,
			LocalAnimationOffset: 260,
			LocalSequenceCount: 3,
			LocalSequenceOffset: 300,
			TextureCount: 1,
			TextureOffset: 400,
			TexturePathCount: 1,
			TexturePathOffset: 420,
			SkinReferenceCount: 1,
			SkinFamilyCount: 1,
			SkinFamilyOffset: 440,
			BodyPartCount: 1,
			BodyPartOffset: 460,
			LocalAttachmentCount: 0,
			LocalAttachmentOffset: 0,
			FlexDescCount: 0,
			FlexDescOffset: 0,
			FlexControllerCount: 0,
			FlexControllerOffset: 0,
			FlexRuleCount: 0,
			FlexRuleOffset: 0,
			AnimBlockNameOffset: 0,
			AnimBlockCount: 0,
			AnimBlockOffset: 0);

		var roundTripped = RoundTrip(header, MdlHeaderSerializer.Write, MdlHeaderSerializer.Read);
		Assert.Equal(header, roundTripped);
	}

	[Fact]
	public void VvdHeader_RoundTrips()
	{
		var header = new VvdHeader(
			Id: "IDSV",
			Version: 4,
			Checksum: 789,
			LodCount: 3,
			LodVertexCount: new[] { 10, 6, 3 },
			FixupCount: 2,
			FixupTableOffset: 128,
			VertexDataOffset: 256,
			TangentDataOffset: 512);

		var roundTripped = RoundTrip(header, VvdHeaderSerializer.Write, VvdHeaderSerializer.Read);
		Assert.Equal(header.Id, roundTripped.Id);
		Assert.Equal(header.Version, roundTripped.Version);
		Assert.Equal(header.Checksum, roundTripped.Checksum);
		Assert.Equal(header.LodCount, roundTripped.LodCount);
		Assert.Equal(header.LodVertexCount.Take(header.LodCount), roundTripped.LodVertexCount.Take(header.LodCount));
		Assert.Equal(header.FixupCount, roundTripped.FixupCount);
		Assert.Equal(header.FixupTableOffset, roundTripped.FixupTableOffset);
		Assert.Equal(header.VertexDataOffset, roundTripped.VertexDataOffset);
		Assert.Equal(header.TangentDataOffset, roundTripped.TangentDataOffset);
	}

	[Fact]
	public void VtxHeader_RoundTrips()
	{
		var header = new VtxHeader(
			Version: 7,
			Checksum: 456,
			LodCount: 2,
			MaterialReplacementListOffset: 1024,
			BodyPartCount: 1,
			BodyPartOffset: 2048);

		var roundTripped = RoundTrip(header, VtxHeaderSerializer.Write, VtxHeaderSerializer.Read);
		Assert.Equal(header, roundTripped);
	}

	private static T RoundTrip<T>(T value, Action<BinaryWriter, T> write, Func<BinaryReader, T> read)
	{
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
		{
			write(writer, value);
		}

		stream.Seek(0, SeekOrigin.Begin);
		using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
		return read(reader);
	}
}
