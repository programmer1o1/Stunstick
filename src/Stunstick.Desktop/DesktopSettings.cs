namespace Stunstick.Desktop;

internal sealed class DesktopSettings
{
	public double? WindowWidth { get; set; }
	public double? WindowHeight { get; set; }

	public string? WorkFolder { get; set; }

	public bool EnableGlobalDropRouting { get; set; } = true;
	public int DropMdlActionIndex { get; set; } = 0;
	public int DropPackageActionIndex { get; set; } = 0;
	public int DropFolderActionIndex { get; set; } = 0;
	public int DropQcActionIndex { get; set; } = 0;

	public bool FileAssocVpk { get; set; } = true;
	public bool FileAssocGma { get; set; } = true;
	public bool FileAssocFpx { get; set; } = true;
	public bool FileAssocMdl { get; set; } = true;
	public bool FileAssocQc { get; set; } = true;

	public string? InspectPath { get; set; }
	public bool InspectComputeSha256 { get; set; } = true;
	public int InspectMdlVersionIndex { get; set; } = 0;

	public string? UnpackInput { get; set; }
	public string? UnpackOutput { get; set; }
	public bool UnpackVerifyCrc32 { get; set; }
	public bool UnpackVerifyMd5 { get; set; }
	public bool UnpackFolderPerPackage { get; set; }
	public bool UnpackKeepFullPath { get; set; } = true;
	public bool UnpackWriteLogFile { get; set; }
	public bool UnpackOpenOutput { get; set; }
	public bool UnpackShowSizesInBytes { get; set; }
	public List<string> UnpackSavedSearches { get; set; } = new();

	public string? PackInput { get; set; }
	public string? PackOutput { get; set; }
	public int PackInputModeIndex { get; set; } = 0;
	public int PackBatchOutputTypeIndex { get; set; } = 0;
	public bool PackMultiFile { get; set; }
	public bool PackWithMd5 { get; set; }
	public string? PackSplitMb { get; set; }
	public string? PackPreloadBytes { get; set; }
	public int PackVpkVersionIndex { get; set; } = 0;
	public string? PackVpkToolPath { get; set; }
	public string? PackDirectOptions { get; set; }
	public bool PackWriteLogFile { get; set; }
	public bool PackOpenOutput { get; set; }
	public bool PackGmaCreateAddonJson { get; set; }
	public string? PackGmaTitle { get; set; }
	public string? PackGmaDescription { get; set; }
	public string? PackGmaAuthor { get; set; }
	public string? PackGmaVersion { get; set; }
	public string? PackGmaTags { get; set; }
	public string? PackGmaIgnore { get; set; }
	public bool PackGmaIgnoreWhitelist { get; set; }

	public string? DecompileMdl { get; set; }
	public string? DecompileOutput { get; set; }
	public int DecompileMdlVersionIndex { get; set; } = 0;
	public bool DecompileWriteQc { get; set; } = true;
	public bool DecompileQcGroupIntoQciFiles { get; set; } = true;
	public bool DecompileQcSkinFamilyOnSingleLine { get; set; } = true;
	public bool DecompileQcOnlyChangedMaterialsInTextureGroupLines { get; set; } = true;
	public bool DecompileQcIncludeDefineBoneLines { get; set; } = true;
	public bool DecompileWriteRefSmd { get; set; } = true;
	public bool DecompileWriteLodSmd { get; set; } = true;
	public bool DecompileWritePhysicsSmd { get; set; } = true;
	public bool DecompileWriteTextureBmps { get; set; } = true;
	public bool DecompileWriteProceduralBonesVrd { get; set; } = true;
	public bool DecompileWriteDeclareSequenceQci { get; set; }
	public bool DecompileWriteDebugInfoFiles { get; set; }
	public bool DecompileWriteAnims { get; set; } = true;
	public bool DecompileAnimsSameFolder { get; set; } = true;
	public bool DecompileWriteVta { get; set; }
	public bool DecompileStripMaterialPaths { get; set; }
	public bool DecompileMixedCaseQc { get; set; } = true;
	public bool DecompileNonValveUv { get; set; } = true;
	public bool DecompileFlatOutput { get; set; }
	public bool DecompileIncludeSubfolders { get; set; }
	public bool DecompilePrefixFileNamesWithModelName { get; set; }
	public bool DecompileStricterFormat { get; set; } = true;
	public bool DecompileWriteLogFile { get; set; }
	public bool DecompileOpenOutput { get; set; }

	public string? CompileQc { get; set; }
	public string? CompileGameDir { get; set; }
	public string? CompileStudioMdl { get; set; }
	public string? CompileSteamAppId { get; set; }
	public string? CompileSteamRoot { get; set; }
	public string? CompileWinePrefix { get; set; }
	public string? CompileWineCommand { get; set; }
	public bool CompileNoP4 { get; set; } = true;
	public bool CompileVerbose { get; set; } = true;
	public bool CompileIncludeSubfolders { get; set; }
	public bool CompileDefineBones { get; set; }
	public bool CompileDefineBonesWriteQciFile { get; set; }
	public string? CompileDefineBonesQciFileName { get; set; } = "DefineBones";
	public bool CompileDefineBonesOverwriteQciFile { get; set; }
	public bool CompileDefineBonesModifyQcFile { get; set; }
	public string? CompileDirectOptions { get; set; }
	public bool CompileWriteLogFile { get; set; }
	public bool CompileCopyOutput { get; set; }
	public string? CompileOutputCopyFolder { get; set; }

	public string? ViewMdl { get; set; }
	public string? ViewGameDir { get; set; }
	public string? ViewHlmv { get; set; }
	public string? ViewSteamAppId { get; set; }
	public string? ViewSteamRoot { get; set; }
	public string? ViewWinePrefix { get; set; }
	public string? ViewWineCommand { get; set; }
	public bool ViewDataViewerAutoRun { get; set; } = true;
	public int ViewDataViewerMdlVersionIndex { get; set; } = 0;

	public string? ToolchainSteamRoot { get; set; }
	public List<ToolchainPresetOverrides> ToolchainOverrides { get; set; } = new();
	public uint? ToolchainSelectedAppId { get; set; }
	public List<ToolchainCustomPreset> ToolchainCustomPresets { get; set; } = new();
	public string? ToolchainSelectedCustomId { get; set; }
	public string? ToolchainGoldSrcStudioMdlPath { get; set; }
	public string? ToolchainSource2StudioMdlPath { get; set; }
	public List<string> ToolchainLibraryRoots { get; set; } = new();
	public List<ToolchainMacro> ToolchainMacros { get; set; } = new();

	public string? WorkshopDownloadAppId { get; set; }
	public string? WorkshopDownloadSteamRoot { get; set; }
	public string? WorkshopDownloadIdOrLink { get; set; }
	public string? WorkshopDownloadOutput { get; set; }
	public bool WorkshopDownloadFetchDetails { get; set; }
	public bool WorkshopDownloadIncludeTitleInName { get; set; }
	public bool WorkshopDownloadIncludeIdInName { get; set; } = true;
	public bool WorkshopDownloadAppendUpdatedTimestamp { get; set; }
	public bool WorkshopDownloadReplaceSpacesWithUnderscores { get; set; } = true;
	public bool WorkshopDownloadConvertToExpectedFileOrFolder { get; set; } = true;
	public bool WorkshopDownloadOverwriteOutput { get; set; }
	public bool WorkshopDownloadOpenOutput { get; set; }
	public bool WorkshopDownloadUseSteamworksFallback { get; set; }
	public bool WorkshopDownloadUseSteamCmdFallback { get; set; }
	public string? WorkshopDownloadSteamCmdPath { get; set; }
	public string? WorkshopDownloadSteamCmdUser { get; set; }
	public string? WorkshopDownloadSteamCmdInstallDirectory { get; set; }

	public string? WorkshopPublishAppId { get; set; }
	public string? WorkshopPublishSteamCmdPath { get; set; }
	public string? WorkshopPublishSteamCmdUser { get; set; }
	public string? WorkshopPublishPublishedFileId { get; set; }
	public int WorkshopPublishVisibilityIndex { get; set; } = 0;
	public string? WorkshopPublishContentFolder { get; set; }
	public bool WorkshopPublishStageCleanPayload { get; set; } = true;
	public bool WorkshopPublishPackToVpkBeforeUpload { get; set; }
	public uint WorkshopPublishVpkVersion { get; set; } = 1;
	public bool WorkshopPublishVpkIncludeMd5Sections { get; set; }
	public bool WorkshopPublishVpkMultiFile { get; set; }
	public string? WorkshopPublishPreviewFile { get; set; }
	public string? WorkshopPublishTitle { get; set; }
	public string? WorkshopPublishDescription { get; set; }
	public string? WorkshopPublishChangeNote { get; set; }
	public string? WorkshopPublishTags { get; set; }
	public string? WorkshopPublishContentType { get; set; }
	public List<string> WorkshopPublishContentTags { get; set; } = new();
	public string? WorkshopPublishVdfPath { get; set; }
	public bool WorkshopPublishOpenPageWhenDone { get; set; }
	public uint? WorkshopPublishSelectedAppId { get; set; }

	public List<WorkshopPublishDraft> WorkshopPublishDrafts { get; set; } = new();
	public string? WorkshopPublishSelectedDraftId { get; set; }
}
