namespace Stunstick.App.Decompile;

public sealed record DecompileOptions(
	bool WriteQcFile = true,
	bool QcGroupIntoQciFiles = true,
	bool QcSkinFamilyOnSingleLine = true,
	bool QcOnlyChangedMaterialsInTextureGroupLines = true,
	bool QcIncludeDefineBoneLines = true,
	bool WriteReferenceMeshSmdFiles = true,
	bool WriteBoneAnimationSmdFiles = true,
	bool BoneAnimationPlaceInSubfolder = false,
	bool WriteVertexAnimationVtaFile = false,
	bool WritePhysicsMeshSmdFile = true,
	bool WriteTextureBmpFiles = true,
	bool WriteProceduralBonesVrdFile = true,
	bool WriteDeclareSequenceQciFile = false,
	bool WriteDebugInfoFiles = false,
	bool WriteLodMeshSmdFiles = true,
	bool RemovePathFromSmdMaterialFileNames = false,
	bool UseNonValveUvConversion = false,
	bool FolderForEachModel = true,
	bool PrefixFileNamesWithModelName = false,
	bool StricterFormat = true,
	bool IndentPhysicsTriangles = true,
	bool QcUseMixedCaseForKeywords = true,
	int? VersionOverride = null
);
