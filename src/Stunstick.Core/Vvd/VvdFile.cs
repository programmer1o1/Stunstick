namespace Stunstick.Core.Vvd;

public sealed record VvdFile(
	string SourcePath,
	VvdHeader Header,
	IReadOnlyList<VvdVertex> Vertexes,
	IReadOnlyList<VvdFixup> Fixups,
	IReadOnlyList<IReadOnlyList<VvdVertex>> FixedVertexesByLod
);

