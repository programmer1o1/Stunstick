namespace Stunstick.Core.Vtx;

public sealed record VtxHeader(
	int Version,
	int Checksum,
	int LodCount,
	int MaterialReplacementListOffset,
	int BodyPartCount,
	int BodyPartOffset
);
