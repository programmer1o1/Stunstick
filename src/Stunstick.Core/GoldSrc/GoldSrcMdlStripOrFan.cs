namespace Stunstick.Core.GoldSrc;

public sealed record GoldSrcMdlStripOrFan(
	bool IsTriangleStrip,
	IReadOnlyList<GoldSrcMdlVertexInfo> Vertexes
);

