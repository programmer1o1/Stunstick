namespace Stunstick.Core.Vtx;

public sealed record VtxStripGroup(
	IReadOnlyList<VtxVertex> Vertexes,
	IReadOnlyList<ushort> Indexes,
	byte Flags
);
