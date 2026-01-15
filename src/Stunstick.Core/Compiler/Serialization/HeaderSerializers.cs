using System.Text;
using Stunstick.Core.Mdl;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;

namespace Stunstick.Core.Compiler.Serialization;

public static class MdlHeaderSerializer
{
	public static void Write(BinaryWriter writer, MdlHeader header)
	{
		if (writer is null) throw new ArgumentNullException(nameof(writer));
		if (header is null) throw new ArgumentNullException(nameof(header));

		writer.Write(header.Id);
		writer.Write(header.Version);
		writer.Write(header.Checksum);
		WriteFixedString(writer, header.Name, 64);
		writer.Write(header.Length);

		// For this lightweight serializer we only persist the core fields that
		// the current MdlHeader record carries. Additional header fields will
		// be added as the internal compiler grows.
		writer.Write(header.Flags);
		writer.Write(header.BoneCount);
		writer.Write(header.BoneOffset);
		writer.Write(header.LocalAnimationCount);
		writer.Write(header.LocalAnimationOffset);
		writer.Write(header.LocalSequenceCount);
		writer.Write(header.LocalSequenceOffset);
		writer.Write(header.TextureCount);
		writer.Write(header.TextureOffset);
		writer.Write(header.TexturePathCount);
		writer.Write(header.TexturePathOffset);
		writer.Write(header.SkinReferenceCount);
		writer.Write(header.SkinFamilyCount);
		writer.Write(header.SkinFamilyOffset);
		writer.Write(header.BodyPartCount);
		writer.Write(header.BodyPartOffset);
		writer.Write(header.LocalAttachmentCount);
		writer.Write(header.LocalAttachmentOffset);
		writer.Write(header.FlexDescCount);
		writer.Write(header.FlexDescOffset);
		writer.Write(header.FlexControllerCount);
		writer.Write(header.FlexControllerOffset);
		writer.Write(header.FlexRuleCount);
		writer.Write(header.FlexRuleOffset);
		writer.Write(header.AnimBlockNameOffset);
		writer.Write(header.AnimBlockCount);
		writer.Write(header.AnimBlockOffset);
	}

	public static MdlHeader Read(BinaryReader reader)
	{
		if (reader is null) throw new ArgumentNullException(nameof(reader));

		var id = reader.ReadUInt32();
		var version = reader.ReadInt32();
		var checksum = reader.ReadInt32();
		var name = ReadFixedString(reader, 64);
		var length = reader.ReadInt32();

		var flags = reader.ReadInt32();
		var boneCount = reader.ReadInt32();
		var boneOffset = reader.ReadInt32();
		var localAnimationCount = reader.ReadInt32();
		var localAnimationOffset = reader.ReadInt32();
		var localSequenceCount = reader.ReadInt32();
		var localSequenceOffset = reader.ReadInt32();
		var textureCount = reader.ReadInt32();
		var textureOffset = reader.ReadInt32();
		var texturePathCount = reader.ReadInt32();
		var texturePathOffset = reader.ReadInt32();
		var skinReferenceCount = reader.ReadInt32();
		var skinFamilyCount = reader.ReadInt32();
		var skinFamilyOffset = reader.ReadInt32();
		var bodyPartCount = reader.ReadInt32();
		var bodyPartOffset = reader.ReadInt32();
		var localAttachmentCount = reader.ReadInt32();
		var localAttachmentOffset = reader.ReadInt32();
		var flexDescCount = reader.ReadInt32();
		var flexDescOffset = reader.ReadInt32();
		var flexControllerCount = reader.ReadInt32();
		var flexControllerOffset = reader.ReadInt32();
		var flexRuleCount = reader.ReadInt32();
		var flexRuleOffset = reader.ReadInt32();
		var animBlockNameOffset = reader.ReadInt32();
		var animBlockCount = reader.ReadInt32();
		var animBlockOffset = reader.ReadInt32();

		return new MdlHeader(
			Id: id,
			Version: version,
			Checksum: checksum,
			Name: name,
			Length: length,
			Flags: flags,
			BoneCount: boneCount,
			BoneOffset: boneOffset,
			LocalAnimationCount: localAnimationCount,
			LocalAnimationOffset: localAnimationOffset,
			LocalSequenceCount: localSequenceCount,
			LocalSequenceOffset: localSequenceOffset,
			TextureCount: textureCount,
			TextureOffset: textureOffset,
			TexturePathCount: texturePathCount,
			TexturePathOffset: texturePathOffset,
			SkinReferenceCount: skinReferenceCount,
			SkinFamilyCount: skinFamilyCount,
			SkinFamilyOffset: skinFamilyOffset,
			BodyPartCount: bodyPartCount,
			BodyPartOffset: bodyPartOffset,
			LocalAttachmentCount: localAttachmentCount,
			LocalAttachmentOffset: localAttachmentOffset,
			FlexDescCount: flexDescCount,
			FlexDescOffset: flexDescOffset,
			FlexControllerCount: flexControllerCount,
			FlexControllerOffset: flexControllerOffset,
			FlexRuleCount: flexRuleCount,
			FlexRuleOffset: flexRuleOffset,
			AnimBlockNameOffset: animBlockNameOffset,
			AnimBlockCount: animBlockCount,
			AnimBlockOffset: animBlockOffset);
	}

	private static void WriteFixedString(BinaryWriter writer, string value, int length)
	{
		var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
		if (bytes.Length >= length)
		{
			writer.Write(bytes, 0, length - 1);
			writer.Write((byte)0);
		}
		else
		{
			writer.Write(bytes);
			writer.Write(new byte[length - bytes.Length]);
		}
	}

	private static string ReadFixedString(BinaryReader reader, int length)
	{
		var bytes = reader.ReadBytes(length);
		var zeroIndex = Array.IndexOf(bytes, (byte)0);
		var actualLength = zeroIndex >= 0 ? zeroIndex : bytes.Length;
		return Encoding.ASCII.GetString(bytes, 0, actualLength);
	}
}

public static class VvdHeaderSerializer
{
	private const int MaxLods = 8;

	public static void Write(BinaryWriter writer, VvdHeader header)
	{
		if (writer is null) throw new ArgumentNullException(nameof(writer));
		if (header is null) throw new ArgumentNullException(nameof(header));

		WriteFixedString(writer, header.Id, 4);
		writer.Write(header.Version);
		writer.Write(header.Checksum);
		writer.Write(header.LodCount);

		for (var i = 0; i < MaxLods; i++)
		{
			var count = i < header.LodVertexCount.Count ? header.LodVertexCount[i] : 0;
			writer.Write(count);
		}

		writer.Write(header.FixupCount);
		writer.Write(header.FixupTableOffset);
		writer.Write(header.VertexDataOffset);
		writer.Write(header.TangentDataOffset);
	}

	public static VvdHeader Read(BinaryReader reader)
	{
		if (reader is null) throw new ArgumentNullException(nameof(reader));

		var id = ReadFixedString(reader, 4);
		var version = reader.ReadInt32();
		var checksum = reader.ReadInt32();
		var lodCount = reader.ReadInt32();

		var lodVertexCount = new int[MaxLods];
		for (var i = 0; i < MaxLods; i++)
		{
			lodVertexCount[i] = reader.ReadInt32();
		}

		var fixupCount = reader.ReadInt32();
		var fixupTableOffset = reader.ReadInt32();
		var vertexDataOffset = reader.ReadInt32();
		var tangentDataOffset = reader.ReadInt32();

		return new VvdHeader(
			Id: id,
			Version: version,
			Checksum: checksum,
			LodCount: lodCount,
			LodVertexCount: lodVertexCount,
			FixupCount: fixupCount,
			FixupTableOffset: fixupTableOffset,
			VertexDataOffset: vertexDataOffset,
			TangentDataOffset: tangentDataOffset);
	}

	private static void WriteFixedString(BinaryWriter writer, string value, int length)
	{
		var bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
		if (bytes.Length >= length)
		{
			writer.Write(bytes, 0, length);
		}
		else
		{
			writer.Write(bytes);
			writer.Write(new byte[length - bytes.Length]);
		}
	}

	private static string ReadFixedString(BinaryReader reader, int length)
	{
		var bytes = reader.ReadBytes(length);
		return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
	}
}

public static class VtxHeaderSerializer
{
	public static void Write(BinaryWriter writer, VtxHeader header)
	{
		if (writer is null) throw new ArgumentNullException(nameof(writer));
		if (header is null) throw new ArgumentNullException(nameof(header));

		writer.Write(header.Version);
		writer.Write(header.Checksum);
		writer.Write(header.LodCount);
		writer.Write(header.MaterialReplacementListOffset);
		writer.Write(header.BodyPartCount);
		writer.Write(header.BodyPartOffset);
	}

	public static VtxHeader Read(BinaryReader reader)
	{
		if (reader is null) throw new ArgumentNullException(nameof(reader));

		var version = reader.ReadInt32();
		var checksum = reader.ReadInt32();
		var lodCount = reader.ReadInt32();
		var materialReplacementListOffset = reader.ReadInt32();
		var bodyPartCount = reader.ReadInt32();
		var bodyPartOffset = reader.ReadInt32();

		return new VtxHeader(
			Version: version,
			Checksum: checksum,
			LodCount: lodCount,
			MaterialReplacementListOffset: materialReplacementListOffset,
			BodyPartCount: bodyPartCount,
			BodyPartOffset: bodyPartOffset);
	}
}
