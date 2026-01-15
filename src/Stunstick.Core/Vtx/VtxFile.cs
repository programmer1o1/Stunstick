namespace Stunstick.Core.Vtx;

public sealed record VtxFile(
	string SourcePath,
	VtxHeader Header,
	bool UsesExtraStripGroupFields,
	IReadOnlyList<VtxBodyPart> BodyParts,
	IReadOnlyList<VtxMaterialReplacementList> MaterialReplacementLists
);
