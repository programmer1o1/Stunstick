namespace Stunstick.Core.Vvd;

public sealed record VvdHeader(
	string Id,
	int Version,
	int Checksum,
	int LodCount,
	IReadOnlyList<int> LodVertexCount,
	int FixupCount,
	int FixupTableOffset,
	int VertexDataOffset,
	int TangentDataOffset
);

