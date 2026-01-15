using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Stunstick.App;
using Stunstick.App.Compile;
using Stunstick.App.Decompile;
using Stunstick.App.Inspect;
using Stunstick.App.Pack;
using Stunstick.App.Progress;
using Stunstick.App.Toolchain;
using Stunstick.App.Unpack;
using Stunstick.App.Viewer;
using Stunstick.App.Workshop;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Stunstick.Desktop;

public sealed partial class MainWindow : Window
{
	private readonly StunstickApplication app = new(new ToolchainLauncher(new SystemProcessLauncher()));
	private CancellationTokenSource? operationCancellation;
	private CancellationTokenSource? packSkipCurrentFolderCancellation;
	private int operationProgressVersion;
	private IReadOnlyList<PackageEntry>? unpackEntries;
	private IReadOnlyList<PackageEntry>? unpackFilteredEntries;
	private readonly List<string> unpackSavedSearches = new();
	private bool isUnpackSavedSearchesSyncing;
	private IReadOnlyList<ToolchainPresetListItem> toolchainAllPresets = Array.Empty<ToolchainPresetListItem>();
	private readonly List<ToolchainPresetOverrides> toolchainOverrides = new();
	private readonly List<ToolchainCustomPreset> toolchainCustomPresets = new();
	private readonly List<string> toolchainLibraryRoots = new();
	private readonly List<ToolchainMacro> toolchainMacros = new();
	private uint? toolchainSelectedAppId;
	private string? toolchainSelectedCustomId;
	private string? lastDecompileQcPath;
	private string? lastCompileOutputMdlPath;
	private bool enableGlobalDropRouting = true;
	private int dropMdlActionIndex;
	private int dropPackageActionIndex;
	private int dropFolderActionIndex;
	private int dropQcActionIndex;
	private bool isLoadingSettings = true;
	private string? lastWorkshopDownloadOutputPath;
	private bool isWorkshopDownloadUiSyncing;
	private ulong? lastWorkshopPublishPublishedFileId;
	private readonly List<WorkshopPublishDraft> workshopPublishDrafts = new();
	private string? workshopPublishSelectedDraftId;
	private bool isWorkshopPublishDraftFormLoading;
	private bool isWorkshopPublishGmodTagEnforcingLimit;
	private bool isWorkshopPublishTagPresetsSyncing;
	private uint? workshopPublishTagPresetsForAppId;
	private bool isWorkshopPublishGamePresetSyncing;
	private uint? workshopPublishSelectedAppId;
	private readonly List<WorkshopPublishedItemListItem> workshopPublishMyItems = new();
	private MdlPreviewResult? viewPreview;
	private bool isViewPreviewUiUpdating;
	private CancellationTokenSource? viewDataViewerCancellation;
		private static readonly ToolchainGameEngine[] ToolchainEngines = new[]
		{
			ToolchainGameEngine.Source,
			ToolchainGameEngine.GoldSrc,
			ToolchainGameEngine.Source2,
			ToolchainGameEngine.Unknown
		};

	private static readonly IReadOnlyDictionary<uint, IReadOnlyList<string>> WorkshopPublishTagPresets = new Dictionary<uint, IReadOnlyList<string>>
	{
		[362890] = new[] { "Singleplayer", "Multiplayer", "Creature", "Environment", "NPC", "Weapon" }, // Black Mesa
		[355180] = new[] { "Bomb Escape", "Survival", "Custom", "PvP", "Packs", "Skins", "Models", "Sounds", "Miscellaneous" }, // Codename CURE
		[238430] = new[]
		{
			"Escape (Co-op)",
			"Extraction (Co-op)",
			"Hunted (PVP)",
			"Contagion Panic Classic (CPC)",
			"Contagion Panic Objective (CPO)",
			"Weapons (Model/Texture)",
			"Survivors (Model/Texture)",
			"Zombies (Model/Texture)",
			"User Interface",
			"Sounds",
			"Flashlight",
			"Smartphone Wallpapers",
			"Misc",
			"Flatline"
		}, // Contagion
		[1583720] = new[] { "Custom Map", "Weapon", "Campaign Addon / Mod", "Misc Map", "NPC", "Item", "Sound", "Script", "Misc Asset" }, // Entropy : Zero 2
		[397680] = new[] { "Works with Store Menu", "Works with Reward System", "Spawner Spawnlist", "Skin", "Sound", "Player Voice", "Gamemode", "Character", "Map", "Overhaul", "Weapon" }, // Firefight Reloaded
		[723390] = new[] { "Alternate Story", "Particles", "Materials", "Models", "Sounds" }, // Hunt Down The Freeman
		[2158860] = new[] { "Singleplayer", "Multiplayer", "Skin", "Script", "Model", "Map" }, // JBMod
		[550] = new[]
		{
			"Campaigns",
			"Survival",
			"Co-op",
			"Single Player",
			"Versus",
			"Mutations",
			"Scripts",
			"Weapons",
			"Items",
			"Sounds",
			"Miscellaneous",
			"UI",
			"Bill",
			"Scavenge",
			"Francis",
			"Louis",
			"Zoey",
			"Coach",
			"Ellis",
			"Nick",
			"Rochelle",
			"Witch",
			"Tank",
			"Spitter",
			"Smoker",
			"Jockey",
			"Hunter",
			"Charger",
			"Boomer",
			"Common Infected",
			"Special Infected",
			"Models",
			"Textures",
			"Realism",
			"Realism Versus",
			"Grenade Launcher",
			"M60",
			"Melee",
			"Pistol",
			"Rifle",
			"Shotgun",
			"SMG",
			"Sniper",
			"Throwable",
			"Adrenaline",
			"Defibrillator",
			"Medkit",
			"Other",
			"Pills",
			"Survivors"
		}, // Left 4 Dead 2
		[313240] = new[] { "Particles", "Props", "Weapons", "NPCs", "Campaign", "Map", "Fixes" }, // Wilson Chronicles
		[17500] = new[] { "Survival", "Objective", "Hardcore", "Custom", "GUIs", "Weapons", "Props", "Characters", "Sound Pack", "Model Pack", "Weapon Sounds", "Characters Sounds" }, // Zombie Panic! Source
		[1012110] = new[]
		{
			"Weapons",
			"Deathmatch",
			"Conquest",
			"Capture The Flag",
			"Items",
			"Maps",
			"Models",
			"Scripts",
			"Sounds",
			"Maps ( Port )",
			"Miscellaneous",
			"UI",
			"Textures",
			"Team Deathmatch",
			"Gun Game",
			"Gun Game Deathmatch",
			"Custom"
		} // Military Conflict: Vietnam
	};
	private sealed class PackageEntryListItem
	{
		public PackageEntry Entry { get; }
		public string RelativePath => Entry.RelativePath;
		public string SizeText { get; }

		public PackageEntryListItem(PackageEntry entry, string sizeText)
		{
			Entry = entry ?? throw new ArgumentNullException(nameof(entry));
			SizeText = sizeText ?? string.Empty;
		}
	}
		private sealed class ToolchainPresetListItem
		{
			public ToolchainPreset Preset { get; }
			public string? SteamRoot { get; }

		public ToolchainPresetListItem(ToolchainPreset preset, string? steamRoot)
		{
			Preset = preset ?? throw new ArgumentNullException(nameof(preset));
			SteamRoot = steamRoot;
		}

		public override string ToString()
		{
				return $"{Preset.Name} ({Preset.AppId})";
			}
		}

		private sealed class ToolchainCustomPresetListItem
		{
			public ToolchainCustomPreset Preset { get; }

			public ToolchainCustomPresetListItem(ToolchainCustomPreset preset)
			{
				Preset = preset ?? throw new ArgumentNullException(nameof(preset));
			}

			public override string ToString()
			{
				var name = string.IsNullOrWhiteSpace(Preset.Name) ? "Custom preset" : Preset.Name!.Trim();
				return $"{name} (Custom)";
			}
		}

		private sealed class WorkshopPublishDraftListItem
		{
			public WorkshopPublishDraft Draft { get; }
			public bool IsDirty { get; }

		public WorkshopPublishDraftListItem(WorkshopPublishDraft draft, bool isDirty)
		{
			Draft = draft ?? throw new ArgumentNullException(nameof(draft));
			IsDirty = isDirty;
		}

		public override string ToString()
		{
			var name = !string.IsNullOrWhiteSpace(Draft.Name)
				? Draft.Name!.Trim()
				: !string.IsNullOrWhiteSpace(Draft.Title)
					? Draft.Title!.Trim()
					: Draft.PublishedFileId;

			name = string.IsNullOrWhiteSpace(name) ? "Draft" : name;
			var prefix = IsDirty ? "* " : string.Empty;
			var idText = string.IsNullOrWhiteSpace(Draft.PublishedFileId) || Draft.PublishedFileId == "0" ? string.Empty : $" ({Draft.PublishedFileId})";
			return prefix + name + idText;
		}
	}

	private sealed class WorkshopPublishedItemListItem
	{
		public WorkshopPublishedItem Item { get; }

		public WorkshopPublishedItemListItem(WorkshopPublishedItem item)
		{
			Item = item ?? throw new ArgumentNullException(nameof(item));
		}

		public override string ToString()
		{
			var title = string.IsNullOrWhiteSpace(Item.Title) ? "(untitled)" : Item.Title.Trim();
			var updated = Item.UpdatedAtUtc.HasValue ? Item.UpdatedAtUtc.Value.ToString("yyyy-MM-dd") : string.Empty;
			var updatedText = string.IsNullOrWhiteSpace(updated) ? string.Empty : $" ({updated})";
			return $"{Item.PublishedFileId}: {title}{updatedText}";
		}
	}

	private sealed class ViewPreviewMaterialListItem
	{
		public int Index { get; }
		public string Name { get; }

		public ViewPreviewMaterialListItem(int index, string name)
		{
			Index = index;
			Name = name ?? string.Empty;
		}

		public override string ToString()
		{
			if (Index < 0)
			{
				return string.IsNullOrWhiteSpace(Name) ? "(All)" : Name;
			}

			var name = string.IsNullOrWhiteSpace(Name) ? $"material_{Index}" : Name;
			return $"[{Index}] {name}";
		}
	}

	public MainWindow()
	{
		InitializeComponent();
		InitializeHelpAndAbout();
		InitializeViewPreviewUi();
		InitializeToolchainEngineComboBox();
			EnablePathDragDrop();
			EnableGlobalDropRouting();
			LoadSettings();
			InitializeWorkshopDownloadUi();
			UpdatePackUiForMode(convertOutput: false);
			EnableWorkshopPublishDraftTracking();
			HandleStartupArgs(Environment.GetCommandLineArgs().Skip(1).ToArray());
			Closing += (_, _) => SaveSettings();
		}

		private void InitializeWorkshopDownloadUi()
		{
			try
			{
				GetRequiredControl<CheckBox>("WorkshopDownloadSteamworksFallbackCheckBox").IsCheckedChanged += (_, _) =>
				{
					if (isLoadingSettings)
					{
						return;
					}

					UpdateWorkshopDownloadUiState();
				};

				GetRequiredControl<CheckBox>("WorkshopDownloadSteamCmdFallbackCheckBox").IsCheckedChanged += (_, _) =>
				{
					if (isLoadingSettings)
					{
						return;
					}

					UpdateWorkshopDownloadUiState();
				};
			}
			catch
			{
			}

			UpdateWorkshopDownloadUiState();
			UpdateWorkshopDownloadExampleOutput();
		}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void InitializeHelpAndAbout()
	{
		try
		{
			GetRequiredControl<TextBox>("HelpSettingsPathTextBox").Text = GetSettingsPath();

			var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
			GetRequiredControl<TextBlock>("AboutVersionTextBlock").Text = $"Version: {version}";
		}
		catch
		{
		}
	}

	private void InitializeViewPreviewUi()
	{
		try
		{
			isViewPreviewUiUpdating = true;

			GetRequiredControl<TextBlock>("ViewPreviewInfoTextBlock").Text = string.Empty;
			GetRequiredControl<TextBox>("ViewPreviewSummaryTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ViewPreviewMaterialsTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ViewPreviewSequencesTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ViewPreviewPhysicsTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ViewDataViewerOutputTextBox").Text = string.Empty;

			GetRequiredControl<ComboBox>("ViewPreviewModeComboBox").SelectedIndex = 0;
			GetRequiredControl<ComboBox>("ViewPreviewLodComboBox").ItemsSource = new[] { 0 };
			GetRequiredControl<ComboBox>("ViewPreviewLodComboBox").SelectedIndex = 0;

			GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox").ItemsSource = new[]
			{
				new ViewPreviewMaterialListItem(index: -1, name: "(All)")
			};
			GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox").SelectedIndex = 0;
		}
		catch
		{
		}
		finally
		{
			isViewPreviewUiUpdating = false;
		}
	}

	private void InitializeToolchainEngineComboBox()
	{
		try
		{
			var combo = GetRequiredControl<ComboBox>("ToolchainEngineComboBox");
			combo.ItemsSource = ToolchainEngines.Select(e => e.ToString()).ToArray();
			combo.SelectedIndex = 0;
		}
		catch
		{
		}
	}

	private void EnablePathDragDrop()
	{
		EnableFileOrFolderDrop("InspectPathTextBox");

		EnableFolderDrop("OptionsWorkFolderTextBox");

		EnableFileDrop("UnpackInputTextBox");
		EnableFolderDrop("UnpackOutputTextBox");

		EnableFolderDrop("PackInputTextBox");
		EnableFileDrop("PackOutputTextBox");
		EnableFileDrop("PackVpkToolTextBox");

			EnableFileOrFolderDrop("DecompileMdlTextBox");
			EnableFolderDrop("DecompileOutputTextBox");

			EnableFileOrFolderDrop("CompileQcTextBox");
			EnableFolderDrop("CompileGameDirTextBox");
			EnableFileDrop("CompileStudioMdlTextBox");
		EnableFolderDrop("CompileSteamRootTextBox");
		EnableFolderDrop("CompileWinePrefixTextBox");

		EnableFileDrop("ViewMdlTextBox");
		EnableFolderDrop("ViewGameDirTextBox");
		EnableFileDrop("ViewHlmvTextBox");
		EnableFolderDrop("ViewSteamRootTextBox");
		EnableFolderDrop("ViewWinePrefixTextBox");

		EnableFolderDrop("ToolchainSteamRootTextBox");
		EnableFileDrop("ToolchainStudioMdlTextBox");
		EnableFileDrop("ToolchainHlmvTextBox");
		EnableFileDrop("ToolchainHammerTextBox");
		EnableFileDrop("ToolchainPackerToolTextBox");

		EnableFolderDrop("WorkshopDownloadSteamRootTextBox");
		EnableFolderDrop("WorkshopDownloadOutputTextBox");
		EnableFileDrop("WorkshopDownloadSteamCmdTextBox");
		EnableFolderDrop("WorkshopDownloadSteamCmdInstallTextBox");

		EnableFileDrop("WorkshopPublishSteamCmdTextBox");
		EnableFileOrFolderDrop("WorkshopPublishContentTextBox");
		EnableFileDrop("WorkshopPublishPreviewTextBox");
		EnableFileDrop("WorkshopPublishVdfTextBox");
	}

	private void EnableGlobalDropRouting()
	{
		DragDrop.SetAllowDrop(this, enableGlobalDropRouting);
		AddHandler(DragDrop.DragOverEvent, OnGlobalDragOver);
		AddHandler(DragDrop.DropEvent, OnGlobalDrop);
	}

	private void OnGlobalDragOver(object? sender, DragEventArgs e)
	{
		if (!enableGlobalDropRouting)
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		var path = TryGetFirstDroppedPath(e);
		if (string.IsNullOrWhiteSpace(path))
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		if (!File.Exists(path) && !Directory.Exists(path))
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		e.DragEffects = DragDropEffects.Copy;
		e.Handled = true;
	}

	private async void OnGlobalDrop(object? sender, DragEventArgs e)
	{
		if (!enableGlobalDropRouting)
		{
			return;
		}

		var path = await TryGetFirstDroppedPathAsync(e);
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		if (TryRoutePathToTab(path))
		{
			e.Handled = true;
		}
	}

	private bool TryRoutePathToTab(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}

		var fullPath = Path.GetFullPath(path);
		if (File.Exists(fullPath))
		{
			var ext = Path.GetExtension(fullPath).ToLowerInvariant();
			if (ext is ".vpk" or ".fpx" or ".gma")
			{
				switch (dropPackageActionIndex)
				{
					case 1:
						GetRequiredControl<TextBox>("InspectPathTextBox").Text = fullPath;
						SelectMainTab("InspectTabItem");
						return true;
					case 2:
						GetRequiredControl<TextBox>("PackInputTextBox").Text = Path.GetDirectoryName(fullPath) ?? fullPath;
						SelectMainTab("PackTabItem");
						return true;
					default:
						GetRequiredControl<TextBox>("UnpackInputTextBox").Text = fullPath;
						SelectMainTab("ExploreTabItem");
						return true;
				}
			}

			if (ext is ".mdl")
			{
				switch (dropMdlActionIndex)
				{
					case 1:
						GetRequiredControl<TextBox>("ViewMdlTextBox").Text = fullPath;
						SelectMainTab("ViewTabItem");
						return true;
					case 2:
						GetRequiredControl<TextBox>("InspectPathTextBox").Text = fullPath;
						SelectMainTab("InspectTabItem");
						return true;
					default:
						GetRequiredControl<TextBox>("DecompileMdlTextBox").Text = fullPath;
						SelectMainTab("DecompileTabItem");
						return true;
				}
			}

			if (ext is ".qc")
			{
				switch (dropQcActionIndex)
				{
					case 1:
						GetRequiredControl<TextBox>("ViewMdlTextBox").Text = fullPath;
						SelectMainTab("ViewTabItem");
						return true;
					case 2:
						GetRequiredControl<TextBox>("InspectPathTextBox").Text = fullPath;
						SelectMainTab("InspectTabItem");
						return true;
					default:
						GetRequiredControl<TextBox>("CompileQcTextBox").Text = fullPath;
						SelectMainTab("CompileTabItem");
						return true;
				}
			}

			GetRequiredControl<TextBox>("InspectPathTextBox").Text = fullPath;
			SelectMainTab("InspectTabItem");
			return true;
		}

		if (Directory.Exists(fullPath))
		{
			if (dropFolderActionIndex == 1)
			{
				GetRequiredControl<TextBox>("InspectPathTextBox").Text = fullPath;
				SelectMainTab("InspectTabItem");
				return true;
			}

			GetRequiredControl<TextBox>("PackInputTextBox").Text = fullPath;
			SelectMainTab("PackTabItem");
			return true;
		}

		return false;
	}

	private static string MapExtractedPath(string outputRoot, string relativePath, bool keepFullPath)
	{
		var normalized = (relativePath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
		normalized = normalized.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var leaf = Path.GetFileName(normalized);
		var targetRelative = keepFullPath ? normalized : (leaf ?? string.Empty);
		return Path.GetFullPath(Path.Combine(outputRoot, targetRelative));
	}

	private void ProcessActivationArgs(IEnumerable<string>? args)
	{
		if (args is null)
		{
			return;
		}

		foreach (var arg in args)
		{
			if (string.IsNullOrWhiteSpace(arg))
			{
				continue;
			}

			var normalized = arg.Trim().Trim('"');
			try
			{
				if (TryRoutePathToTab(normalized))
				{
					return;
				}
			}
			catch
			{
				// Ignore malformed args; keep scanning remaining ones.
			}
		}
	}

	internal void HandleStartupArgs(string[] args)
	{
		ProcessActivationArgs(args);
	}

	internal void HandleActivationArgsFromIpc(string[] args)
	{
		Dispatcher.UIThread.Post(() =>
		{
			BringToFrontFromIpc();
			ProcessActivationArgs(args);
		});
	}

	private void EnableWorkshopPublishDraftTracking()
	{
		GetRequiredControl<TextBox>("WorkshopPublishAppIdTextBox").TextChanged += (_, _) =>
		{
				UpdateWorkshopPublishGmodUiVisibility();
				UpdateWorkshopPublishTagPresetsUiVisibility();
				SyncWorkshopPublishGameSelectionFromAppIdText();
				OnWorkshopPublishDraftFieldChanged();
			};
		HookTextChanged("WorkshopPublishSteamCmdTextBox");
		HookTextChanged("WorkshopPublishSteamCmdUserTextBox");
		HookTextChanged("WorkshopPublishPublishedIdTextBox");
		HookTextChanged("WorkshopPublishContentTextBox");
		HookTextChanged("WorkshopPublishPreviewTextBox");
		HookTextChanged("WorkshopPublishTitleTextBox");
			HookTextChanged("WorkshopPublishDescriptionTextBox");
			HookTextChanged("WorkshopPublishChangeNoteTextBox");
			GetRequiredControl<TextBox>("WorkshopPublishTagsTextBox").TextChanged += (_, _) =>
			{
				SyncWorkshopPublishTagPresetCheckboxesFromText();
				OnWorkshopPublishDraftFieldChanged();
			};
			HookTextChanged("WorkshopPublishVdfTextBox");

		GetRequiredControl<ComboBox>("WorkshopPublishVisibilityComboBox").SelectionChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
		GetRequiredControl<ComboBox>("WorkshopPublishGmodTypeComboBox").SelectionChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
		GetRequiredControl<CheckBox>("WorkshopPublishStageCleanCheckBox").IsCheckedChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
		GetRequiredControl<CheckBox>("WorkshopPublishPackVpkCheckBox").IsCheckedChanged += (_, _) =>
		{
			UpdateWorkshopPublishVpkOptionsUiVisibility();
			OnWorkshopPublishDraftFieldChanged();
		};
		GetRequiredControl<ComboBox>("WorkshopPublishVpkVersionComboBox").SelectionChanged += (_, _) =>
		{
			UpdateWorkshopPublishVpkOptionsUiVisibility();
			OnWorkshopPublishDraftFieldChanged();
		};
		GetRequiredControl<CheckBox>("WorkshopPublishVpkMd5CheckBox").IsCheckedChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
		GetRequiredControl<CheckBox>("WorkshopPublishVpkMultiFileCheckBox").IsCheckedChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
	}

	private void HookTextChanged(string textBoxName)
	{
		GetRequiredControl<TextBox>(textBoxName).TextChanged += (_, _) => OnWorkshopPublishDraftFieldChanged();
	}

	private void OnWorkshopPublishDraftFieldChanged()
	{
		if (isWorkshopPublishDraftFormLoading)
		{
			return;
		}

		UpdateWorkshopPublishDraftStatus();
	}

	private WorkshopPublishDraft? GetSelectedWorkshopPublishDraft()
	{
		if (string.IsNullOrWhiteSpace(workshopPublishSelectedDraftId))
		{
			return null;
		}

		return workshopPublishDrafts.FirstOrDefault(d => d.Id == workshopPublishSelectedDraftId);
	}

	private void RefreshWorkshopPublishDraftsList()
	{
		var listBox = GetRequiredControl<ListBox>("WorkshopPublishDraftsListBox");

		var selectedDraft = GetSelectedWorkshopPublishDraft();
		var isDirty = selectedDraft is not null && IsWorkshopPublishFormDirtyComparedToDraft(selectedDraft);

		var items = workshopPublishDrafts
			.OrderByDescending(d => d.UpdatedAtUtc)
			.Select(d => new WorkshopPublishDraftListItem(d, isDirty: selectedDraft is not null && d.Id == selectedDraft.Id && isDirty))
			.ToArray();

		listBox.ItemsSource = items;

		if (selectedDraft is null)
		{
			listBox.SelectedItem = null;
			return;
		}

		listBox.SelectedItem = items.FirstOrDefault(i => i.Draft.Id == selectedDraft.Id);
	}

		private void UpdateWorkshopPublishDraftStatus()
		{
			var status = GetRequiredControl<TextBlock>("WorkshopPublishDraftStatusTextBlock");
			var selected = GetSelectedWorkshopPublishDraft();

			if (selected is null)
			{
				status.Text = "No draft selected.";
				RefreshWorkshopPublishDraftsList();
				UpdateWorkshopPublishPayloadPreview();
				return;
			}

		var dirty = IsWorkshopPublishFormDirtyComparedToDraft(selected);
		var name = !string.IsNullOrWhiteSpace(selected.Name) ? selected.Name!.Trim() : (!string.IsNullOrWhiteSpace(selected.Title) ? selected.Title!.Trim() : "Draft");

			status.Text = dirty ? $"Draft: {name} (unsaved changes)" : $"Draft: {name}";
			RefreshWorkshopPublishDraftsList();
			UpdateWorkshopPublishPayloadPreview();
		}

	private void UpdateWorkshopPublishPayloadPreview()
	{
		try
		{
			var previewTextBox = GetRequiredControl<TextBox>("WorkshopPublishPayloadPreviewTextBox");
			var noteTextBlock = GetRequiredControl<TextBlock>("WorkshopPublishPayloadPreviewNoteTextBlock");

			var rawContentPath = GetText("WorkshopPublishContentTextBox");
			if (string.IsNullOrWhiteSpace(rawContentPath))
			{
				previewTextBox.Text = string.Empty;
				noteTextBlock.Text = string.Empty;
				return;
			}

			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			var appId = 4000u;
			if (!string.IsNullOrWhiteSpace(appIdText) && (!uint.TryParse(appIdText.Trim(), out appId) || appId == 0))
			{
				previewTextBox.Text = string.Empty;
				noteTextBlock.Text = "Invalid AppID.";
				return;
			}

			var contentPath = Path.GetFullPath(rawContentPath);
			var inputIsFile = File.Exists(contentPath);
			var inputIsDirectory = !inputIsFile && Directory.Exists(contentPath);
			if (!inputIsFile && !inputIsDirectory)
			{
				previewTextBox.Text = contentPath;
				noteTextBlock.Text = "Content path not found.";
				return;
			}

			var isGmod = appId == 4000;
			var stageClean = GetChecked("WorkshopPublishStageCleanCheckBox");
			var packToVpk = GetChecked("WorkshopPublishPackVpkCheckBox");
			var vpkMultiFile = GetChecked("WorkshopPublishVpkMultiFileCheckBox");
			var vpkVersion = GetWorkshopPublishVpkVersionFromForm();
			var includeMd5 = GetWorkshopPublishVpkIncludeMd5SectionsFromForm();

			var title = GetText("WorkshopPublishTitleTextBox");
			var baseName = BuildWorkshopPayloadBaseName(title, contentPath);

			var steps = new List<string>();
			var notes = new List<string>();

			if (inputIsFile)
			{
				steps.Add("Single-file payload");
				steps.Add("Upload");

				var ext = Path.GetExtension(contentPath);
				if (isGmod && !string.Equals(ext, ".gma", StringComparison.OrdinalIgnoreCase))
				{
					notes.Add("Garry's Mod content must be a folder or a .gma file.");
				}
				else if (string.Equals(ext, ".vpk", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".fpx", StringComparison.OrdinalIgnoreCase))
				{
					notes.Add("Multi-file VPK sets are auto-included when selecting a _dir.vpk/_fdr.fpx or numbered archive.");
				}
			}
			else
			{
				var looksLikeGmaPayload = LooksLikeGarrysModPayloadFolder(contentPath);
				var looksLikeVpkPayload = LooksLikeVpkPayloadFolder(contentPath);

				if (isGmod)
				{
					if (looksLikeGmaPayload)
					{
						steps.Add("Upload .gma payload folder");
					}
					else
					{
						if (stageClean)
						{
							steps.Add("Stage clean payload");
						}

						steps.Add($"Pack → {baseName}.gma");
						steps.Add("Upload");
						notes.Add("Garry's Mod folders are packed to .gma automatically.");
					}
				}
				else
				{
					if (looksLikeVpkPayload)
					{
						steps.Add("Upload VPK payload folder");
					}
					else if (packToVpk)
					{
						if (stageClean)
						{
							steps.Add("Stage clean payload");
						}

						var vpkName = vpkMultiFile ? $"{baseName}_dir.vpk (+ archives)" : $"{baseName}.vpk";
						var meta = $"v{vpkVersion}" + (includeMd5 ? " + MD5" : string.Empty);

						steps.Add($"Pack → {vpkName} ({meta})");
						steps.Add("Upload");
					}
					else
					{
						if (stageClean)
						{
							steps.Add("Stage clean payload");
						}

						steps.Add("Upload folder");
					}
				}
			}

			previewTextBox.Text = string.Join(" → ", steps);
			noteTextBlock.Text = notes.Count == 0 ? string.Empty : string.Join(" ", notes);
		}
		catch
		{
		}
	}

	private static bool LooksLikeGarrysModPayloadFolder(string folder)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
			{
				return false;
			}

			var hasTopDir = Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly).Any();
			if (hasTopDir)
			{
				return false;
			}

			var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToArray();
			return files.Length == 1 && string.Equals(Path.GetExtension(files[0]), ".gma", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private static bool LooksLikeVpkPayloadFolder(string folder)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
			{
				return false;
			}

			var hasTopDir = Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly).Any();
			if (hasTopDir)
			{
				return false;
			}

			var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly).ToArray();
			if (files.Length == 0)
			{
				return false;
			}

			return files.All(file =>
			{
				var ext = Path.GetExtension(file);
				return string.Equals(ext, ".vpk", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(ext, ".fpx", StringComparison.OrdinalIgnoreCase);
			});
		}
		catch
		{
			return false;
		}
	}

	private static string BuildWorkshopPayloadBaseName(string? title, string contentPath)
	{
		var input = !string.IsNullOrWhiteSpace(title)
			? title.Trim()
			: Path.GetFileName(Path.TrimEndingDirectorySeparator(contentPath.Trim()));

		var name = SanitizeFileName(input);
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "addon";
		}

		return name.Length > 80 ? name[..80] : name;
	}

	private static string SanitizeFileName(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		var invalid = Path.GetInvalidFileNameChars();
		var sb = new System.Text.StringBuilder(input.Length);
		foreach (var c in input.Trim())
		{
			sb.Append(invalid.Contains(c) || c is '/' or '\\' ? '_' : c);
		}

		return sb.ToString().Trim().TrimEnd('.', ' ');
	}

	private sealed record WorkshopPublishDraftSnapshot(
		string AppId,
		string SteamCmdPath,
		string SteamCmdUser,
		string PublishedFileId,
		int VisibilityIndex,
		string ContentFolder,
		bool StageCleanPayload,
		bool PackToVpkBeforeUpload,
		uint VpkVersion,
		bool VpkIncludeMd5Sections,
		bool VpkMultiFile,
		string PreviewFile,
		string Title,
		string Description,
		string ChangeNote,
		string Tags,
		string ContentType,
		string ContentTags,
		string VdfPath);

	private WorkshopPublishDraftSnapshot GetWorkshopPublishDraftSnapshotFromForm()
	{
		return new WorkshopPublishDraftSnapshot(
			AppId: GetText("WorkshopPublishAppIdTextBox"),
			SteamCmdPath: GetText("WorkshopPublishSteamCmdTextBox"),
			SteamCmdUser: GetText("WorkshopPublishSteamCmdUserTextBox"),
			PublishedFileId: GetText("WorkshopPublishPublishedIdTextBox"),
			VisibilityIndex: GetComboBoxIndex("WorkshopPublishVisibilityComboBox"),
			ContentFolder: GetText("WorkshopPublishContentTextBox"),
			StageCleanPayload: GetChecked("WorkshopPublishStageCleanCheckBox"),
			PackToVpkBeforeUpload: GetChecked("WorkshopPublishPackVpkCheckBox"),
			VpkVersion: GetWorkshopPublishVpkVersionFromForm(),
			VpkIncludeMd5Sections: GetWorkshopPublishVpkIncludeMd5SectionsFromForm(),
			VpkMultiFile: GetChecked("WorkshopPublishVpkMultiFileCheckBox"),
			PreviewFile: GetText("WorkshopPublishPreviewTextBox"),
			Title: GetText("WorkshopPublishTitleTextBox"),
			Description: GetRequiredControl<TextBox>("WorkshopPublishDescriptionTextBox").Text ?? string.Empty,
			ChangeNote: GetRequiredControl<TextBox>("WorkshopPublishChangeNoteTextBox").Text ?? string.Empty,
			Tags: GetText("WorkshopPublishTagsTextBox"),
			ContentType: GetComboBoxText("WorkshopPublishGmodTypeComboBox") ?? string.Empty,
			ContentTags: NormalizeTagList(GetWorkshopPublishGmodSelectedTags()),
			VdfPath: GetText("WorkshopPublishVdfTextBox"));
	}

	private static string Normalize(string? value)
	{
		return value?.Trim() ?? string.Empty;
	}

	private static string NormalizeTagList(IEnumerable<string>? tags)
	{
		if (tags is null)
		{
			return string.Empty;
		}

		return string.Join(
			",",
			tags
				.Select(t => t?.Trim())
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t!.ToLowerInvariant())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
	}

	private bool IsWorkshopPublishFormDirtyComparedToDraft(WorkshopPublishDraft draft)
	{
		var snapshot = GetWorkshopPublishDraftSnapshotFromForm();

		return Normalize(draft.AppId) != Normalize(snapshot.AppId) ||
			Normalize(draft.SteamCmdPath) != Normalize(snapshot.SteamCmdPath) ||
			Normalize(draft.SteamCmdUser) != Normalize(snapshot.SteamCmdUser) ||
			Normalize(draft.PublishedFileId) != Normalize(snapshot.PublishedFileId) ||
			draft.VisibilityIndex != snapshot.VisibilityIndex ||
			Normalize(draft.ContentFolder) != Normalize(snapshot.ContentFolder) ||
			draft.StageCleanPayload != snapshot.StageCleanPayload ||
			draft.PackToVpkBeforeUpload != snapshot.PackToVpkBeforeUpload ||
			draft.VpkVersion != snapshot.VpkVersion ||
			draft.VpkIncludeMd5Sections != snapshot.VpkIncludeMd5Sections ||
			draft.VpkMultiFile != snapshot.VpkMultiFile ||
			Normalize(draft.PreviewFile) != Normalize(snapshot.PreviewFile) ||
			Normalize(draft.Title) != Normalize(snapshot.Title) ||
			Normalize(draft.Description) != Normalize(snapshot.Description) ||
			Normalize(draft.ChangeNote) != Normalize(snapshot.ChangeNote) ||
			Normalize(draft.Tags) != Normalize(snapshot.Tags) ||
			Normalize(draft.ContentType) != Normalize(snapshot.ContentType) ||
			NormalizeTagList(draft.ContentTags) != Normalize(snapshot.ContentTags) ||
			Normalize(draft.VdfPath) != Normalize(snapshot.VdfPath);
	}

	private void EnableFileDrop(string textBoxName)
	{
		var textBox = GetRequiredControl<TextBox>(textBoxName);
		DragDrop.SetAllowDrop(textBox, true);
		textBox.Tag = "file";
		textBox.AddHandler(DragDrop.DragOverEvent, OnPathDragOver);
		textBox.AddHandler(DragDrop.DropEvent, OnPathDrop);
	}

	private void EnableFolderDrop(string textBoxName)
	{
		var textBox = GetRequiredControl<TextBox>(textBoxName);
		DragDrop.SetAllowDrop(textBox, true);
		textBox.Tag = "folder";
		textBox.AddHandler(DragDrop.DragOverEvent, OnPathDragOver);
		textBox.AddHandler(DragDrop.DropEvent, OnPathDrop);
	}

	private void EnableFileOrFolderDrop(string textBoxName)
	{
		var textBox = GetRequiredControl<TextBox>(textBoxName);
		DragDrop.SetAllowDrop(textBox, true);
		textBox.Tag = "fileOrFolder";
		textBox.AddHandler(DragDrop.DragOverEvent, OnPathDragOver);
		textBox.AddHandler(DragDrop.DropEvent, OnPathDrop);
	}

	private void OnPathDragOver(object? sender, DragEventArgs e)
	{
		if (sender is not TextBox textBox)
		{
			return;
		}

		var tag = textBox.Tag?.ToString();

		var path = TryGetFirstDroppedPath(e);
		if (string.IsNullOrWhiteSpace(path))
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		var ok = tag switch
		{
			"folder" => Directory.Exists(path) || File.Exists(path),
			"fileOrFolder" => Directory.Exists(path) || File.Exists(path),
			_ => File.Exists(path)
		};
		e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private async void OnPathDrop(object? sender, DragEventArgs e)
	{
		if (sender is not TextBox textBox)
		{
			return;
		}

		var path = await TryGetFirstDroppedPathAsync(e);
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

			var tag = textBox.Tag?.ToString();
			if (tag == "folder")
			{
				if (Directory.Exists(path))
				{
					textBox.Text = path;
					e.Handled = true;
					return;
				}

				if (File.Exists(path))
				{
					textBox.Text = Path.GetDirectoryName(path) ?? path;
					e.Handled = true;
					return;
				}

				return;
			}

			if (tag == "fileOrFolder")
			{
				if (Directory.Exists(path) || File.Exists(path))
				{
					textBox.Text = path;
					e.Handled = true;
				}

				return;
			}

			if (File.Exists(path))
			{
				textBox.Text = path;
				e.Handled = true;
			}
	}

	private async Task<string?> TryGetFirstDroppedPathAsync(DragEventArgs e)
	{
		var path = TryGetFirstDroppedPath(e);
		if (!string.IsNullOrWhiteSpace(path))
		{
			return path;
		}

		try
		{
			if (e.DataTransfer is IAsyncDataTransfer asyncData)
			{
				var files = await asyncData.TryGetFilesAsync().ConfigureAwait(false);
				if (files is not null)
				{
					foreach (var file in files)
					{
						var p = file?.TryGetLocalPath();
						if (!string.IsNullOrWhiteSpace(p))
						{
							return p;
						}
					}
				}

				var text = await asyncData.TryGetTextAsync().ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(text))
				{
					var first = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
					var p = NormalizeDroppedPath(first);
					if (!string.IsNullOrWhiteSpace(p))
					{
						return p;
					}
				}
			}
		}
		catch
		{
			// best-effort
		}

		return null;
	}

	private static string? TryGetFirstDroppedPath(DragEventArgs e)
	{
		try
		{
			var data = e.DataTransfer;
			if (data is null)
			{
				return null;
			}

			// Preferred: file list provided by platform (covers KDE/Nautilus/Explorer/Win/Mac).
			var storageFiles = data.TryGetFiles();
			if (storageFiles is not null)
			{
				foreach (var file in storageFiles)
				{
					var path = file?.TryGetLocalPath();
					if (!string.IsNullOrWhiteSpace(path))
					{
						return path;
					}
				}
			}

			// Fallback: text/uri-list or plain text path.
			var text = data.TryGetText();
			if (!string.IsNullOrWhiteSpace(text))
			{
				var first = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
				var path = NormalizeDroppedPath(first);
				if (!string.IsNullOrWhiteSpace(path))
				{
					return path;
				}
			}
		}
		catch
		{
			// Ignore drag/drop probing failures.
		}

		return null;
	}

	private static string? NormalizeDroppedPath(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}

		try
		{
			if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
			{
				var uri = new Uri(raw);
				if (uri.IsFile)
				{
					return uri.LocalPath;
				}
			}

			if (Path.IsPathRooted(raw))
			{
				return Path.GetFullPath(raw);
			}
		}
		catch
		{
			// Ignore and let caller continue probing.
		}

		return null;
	}

	private static void OpenUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return;
		}

		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = url,
				UseShellExecute = true
			};

			Process.Start(psi);
		}
		catch
		{
			// Swallow; help links are non-critical.
		}
	}

	private void LoadSettings()
	{
		isLoadingSettings = true;
		try
		{
			var settingsPath = GetSettingsPath();
			if (!File.Exists(settingsPath))
			{
				UpdateUnpackSavedSearchesComboBox();
				return;
			}

			var json = File.ReadAllText(settingsPath);
			var settings = System.Text.Json.JsonSerializer.Deserialize<DesktopSettings>(json, new System.Text.Json.JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true,
				TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
			});

			if (settings is null)
			{
				return;
			}

			if (settings.WindowWidth is > 0)
			{
				Width = settings.WindowWidth.Value;
			}

			if (settings.WindowHeight is > 0)
			{
				Height = settings.WindowHeight.Value;
			}

			SetText("OptionsWorkFolderTextBox", settings.WorkFolder);

			enableGlobalDropRouting = settings.EnableGlobalDropRouting;
			dropMdlActionIndex = settings.DropMdlActionIndex;
			dropPackageActionIndex = settings.DropPackageActionIndex;
			dropFolderActionIndex = settings.DropFolderActionIndex;
			dropQcActionIndex = settings.DropQcActionIndex;
			SetText("ToolchainGoldSrcStudioMdlTextBox", settings.ToolchainGoldSrcStudioMdlPath);
			SetText("ToolchainSource2StudioMdlTextBox", settings.ToolchainSource2StudioMdlPath);
			if (dropMdlActionIndex < 0 || dropMdlActionIndex > 2)
			{
				dropMdlActionIndex = 0;
			}
			if (dropPackageActionIndex < 0 || dropPackageActionIndex > 2)
			{
				dropPackageActionIndex = 0;
			}
			if (dropFolderActionIndex < 0 || dropFolderActionIndex > 1)
			{
				dropFolderActionIndex = 0;
			}
			if (dropQcActionIndex < 0 || dropQcActionIndex > 2)
			{
				dropQcActionIndex = 0;
			}

			DragDrop.SetAllowDrop(this, enableGlobalDropRouting);
			SetChecked("OptionsGlobalDropRoutingCheckBox", enableGlobalDropRouting);
			SetComboBoxIndex("OptionsMdlDropActionComboBox", dropMdlActionIndex);
			SetComboBoxIndex("OptionsPackageDropActionComboBox", dropPackageActionIndex);
			SetComboBoxIndex("OptionsFolderDropActionComboBox", dropFolderActionIndex);
			SetComboBoxIndex("OptionsQcDropActionComboBox", dropQcActionIndex);
			SetChecked("OptionsAssocVpkCheckBox", settings.FileAssocVpk);
			SetChecked("OptionsAssocGmaCheckBox", settings.FileAssocGma);
			SetChecked("OptionsAssocFpxCheckBox", settings.FileAssocFpx);
			SetChecked("OptionsAssocMdlCheckBox", settings.FileAssocMdl);
			SetChecked("OptionsAssocQcCheckBox", settings.FileAssocQc);

			SetText("InspectPathTextBox", settings.InspectPath);
			SetChecked("InspectSha256CheckBox", settings.InspectComputeSha256);
			SetComboBoxIndex("InspectMdlVersionComboBox", settings.InspectMdlVersionIndex);

			SetText("UnpackInputTextBox", settings.UnpackInput);
			SetText("UnpackOutputTextBox", settings.UnpackOutput);
			SetChecked("UnpackVerifyCrcCheckBox", settings.UnpackVerifyCrc32);
			SetChecked("UnpackVerifyMd5CheckBox", settings.UnpackVerifyMd5);
			SetChecked("UnpackFolderPerPackageCheckBox", settings.UnpackFolderPerPackage);
			SetChecked("UnpackKeepFullPathCheckBox", settings.UnpackKeepFullPath);
			SetChecked("UnpackWriteLogFileCheckBox", settings.UnpackWriteLogFile);
			SetChecked("UnpackOpenOutputCheckBox", settings.UnpackOpenOutput);
			SetChecked("UnpackShowSizesInBytesCheckBox", settings.UnpackShowSizesInBytes);
				unpackSavedSearches.Clear();
				if (settings.UnpackSavedSearches is not null)
				{
					unpackSavedSearches.AddRange(settings.UnpackSavedSearches
						.Where(s => !string.IsNullOrWhiteSpace(s))
						.Distinct(StringComparer.OrdinalIgnoreCase));
				}
				UpdateUnpackSavedSearchesComboBox();

				SetText("PackInputTextBox", settings.PackInput);
				SetText("PackOutputTextBox", settings.PackOutput);
				SetComboBoxIndex("PackInputModeComboBox", settings.PackInputModeIndex);
				SetComboBoxIndex("PackBatchOutputTypeComboBox", settings.PackBatchOutputTypeIndex);
				SetChecked("PackMultiFileCheckBox", settings.PackMultiFile);
				SetChecked("PackWithMd5CheckBox", settings.PackWithMd5);
					SetText("PackSplitMbTextBox", settings.PackSplitMb);
					SetText("PackPreloadBytesTextBox", settings.PackPreloadBytes);
					SetComboBoxIndex("PackVpkVersionComboBox", settings.PackVpkVersionIndex);
					SetText("PackVpkToolTextBox", settings.PackVpkToolPath);
					SetText("PackDirectOptionsTextBox", settings.PackDirectOptions);
					SetChecked("PackWriteLogFileCheckBox", settings.PackWriteLogFile);
					SetChecked("PackOpenOutputCheckBox", settings.PackOpenOutput);
					SetChecked("PackGmaCreateAddonJsonCheckBox", settings.PackGmaCreateAddonJson);
					SetText("PackGmaTitleTextBox", settings.PackGmaTitle);
					SetText("PackGmaDescriptionTextBox", settings.PackGmaDescription);
					SetText("PackGmaAuthorTextBox", settings.PackGmaAuthor);
					SetText("PackGmaVersionTextBox", settings.PackGmaVersion);
					SetText("PackGmaTagsTextBox", settings.PackGmaTags);
					SetText("PackGmaIgnoreTextBox", settings.PackGmaIgnore);
					SetChecked("PackGmaIgnoreWhitelistCheckBox", settings.PackGmaIgnoreWhitelist);

				SetText("DecompileMdlTextBox", settings.DecompileMdl);
				SetText("DecompileOutputTextBox", settings.DecompileOutput);
				SetComboBoxIndex("DecompileMdlVersionComboBox", settings.DecompileMdlVersionIndex);
				SetChecked("DecompileWriteQcCheckBox", settings.DecompileWriteQc);
				SetChecked("DecompileQcGroupIntoQciFilesCheckBox", settings.DecompileQcGroupIntoQciFiles);
				SetChecked("DecompileQcSkinFamilyOnSingleLineCheckBox", settings.DecompileQcSkinFamilyOnSingleLine);
				SetChecked("DecompileQcOnlyChangedMaterialsInTextureGroupLinesCheckBox", settings.DecompileQcOnlyChangedMaterialsInTextureGroupLines);
				SetChecked("DecompileQcIncludeDefineBoneLinesCheckBox", settings.DecompileQcIncludeDefineBoneLines);
				SetChecked("DecompileWriteRefSmdCheckBox", settings.DecompileWriteRefSmd);
				SetChecked("DecompileWriteLodSmdCheckBox", settings.DecompileWriteLodSmd);
					SetChecked("DecompileWritePhysicsSmdCheckBox", settings.DecompileWritePhysicsSmd);
					SetChecked("DecompileWriteTextureBmpsCheckBox", settings.DecompileWriteTextureBmps);
					SetChecked("DecompileWriteProceduralBonesVrdCheckBox", settings.DecompileWriteProceduralBonesVrd);
					SetChecked("DecompileWriteDeclareSequenceQciCheckBox", settings.DecompileWriteDeclareSequenceQci);
					SetChecked("DecompileWriteDebugInfoFilesCheckBox", settings.DecompileWriteDebugInfoFiles);
					SetChecked("DecompileWriteAnimsCheckBox", settings.DecompileWriteAnims);
					SetChecked("DecompileAnimsSameFolderCheckBox", settings.DecompileAnimsSameFolder);
					SetChecked("DecompileWriteVtaCheckBox", settings.DecompileWriteVta);
				SetChecked("DecompileStripMaterialPathsCheckBox", settings.DecompileStripMaterialPaths);
							SetChecked("DecompileMixedCaseQcCheckBox", settings.DecompileMixedCaseQc);
						SetChecked("DecompileNonValveUvCheckBox", settings.DecompileNonValveUv);
						SetChecked("DecompileFlatOutputCheckBox", settings.DecompileFlatOutput);
						SetChecked("DecompileIncludeSubfoldersCheckBox", settings.DecompileIncludeSubfolders);
						SetChecked("DecompilePrefixFileNamesWithModelNameCheckBox", settings.DecompilePrefixFileNamesWithModelName);
						SetChecked("DecompileStricterFormatCheckBox", settings.DecompileStricterFormat);
						SetChecked("DecompileWriteLogFileCheckBox", settings.DecompileWriteLogFile);
						SetChecked("DecompileOpenOutputCheckBox", settings.DecompileOpenOutput);

			SetText("CompileQcTextBox", settings.CompileQc);
			SetText("CompileGameDirTextBox", settings.CompileGameDir);
			SetText("CompileStudioMdlTextBox", settings.CompileStudioMdl);
			SetText("CompileSteamAppIdTextBox", settings.CompileSteamAppId);
			SetText("CompileSteamRootTextBox", settings.CompileSteamRoot);
			SetText("CompileWinePrefixTextBox", settings.CompileWinePrefix);
			SetText("CompileWineCommandTextBox", settings.CompileWineCommand);
			SetChecked("CompileNoP4CheckBox", settings.CompileNoP4);
			SetChecked("CompileVerboseCheckBox", settings.CompileVerbose);
				SetChecked("CompileIncludeSubfoldersCheckBox", settings.CompileIncludeSubfolders);
				SetChecked("CompileDefineBonesCheckBox", settings.CompileDefineBones);
			SetChecked("CompileDefineBonesWriteQciFileCheckBox", settings.CompileDefineBonesWriteQciFile);
			SetText("CompileDefineBonesQciFileNameTextBox", settings.CompileDefineBonesQciFileName);
					SetChecked("CompileDefineBonesOverwriteQciFileCheckBox", settings.CompileDefineBonesOverwriteQciFile);
					SetChecked("CompileDefineBonesModifyQcFileCheckBox", settings.CompileDefineBonesModifyQcFile);
					SetText("CompileDirectOptionsTextBox", settings.CompileDirectOptions);
						SetChecked("CompileWriteLogFileCheckBox", settings.CompileWriteLogFile);
						SetChecked("CompileCopyOutputCheckBox", settings.CompileCopyOutput);
						SetText("CompileOutputCopyFolderTextBox", settings.CompileOutputCopyFolder);

			SetText("ViewMdlTextBox", settings.ViewMdl);
			SetText("ViewGameDirTextBox", settings.ViewGameDir);
			SetText("ViewHlmvTextBox", settings.ViewHlmv);
			SetText("ViewSteamAppIdTextBox", settings.ViewSteamAppId);
			SetText("ViewSteamRootTextBox", settings.ViewSteamRoot);
			SetText("ViewWinePrefixTextBox", settings.ViewWinePrefix);
			SetText("ViewWineCommandTextBox", settings.ViewWineCommand);
			SetChecked("ViewDataViewerAutoRunCheckBox", settings.ViewDataViewerAutoRun);
			SetComboBoxIndex("ViewDataViewerMdlVersionComboBox", settings.ViewDataViewerMdlVersionIndex);

				SetText("ToolchainSteamRootTextBox", settings.ToolchainSteamRoot);
				toolchainOverrides.Clear();
				if (settings.ToolchainOverrides is not null)
				{
					toolchainOverrides.AddRange(settings.ToolchainOverrides);
				}
				toolchainSelectedAppId = settings.ToolchainSelectedAppId;
				toolchainCustomPresets.Clear();
				if (settings.ToolchainCustomPresets is not null)
				{
					toolchainCustomPresets.AddRange(settings.ToolchainCustomPresets);
				}
				toolchainSelectedCustomId = settings.ToolchainSelectedCustomId;
				toolchainLibraryRoots.Clear();
				if (settings.ToolchainLibraryRoots is not null)
				{
					toolchainLibraryRoots.AddRange(settings.ToolchainLibraryRoots.Where(p => !string.IsNullOrWhiteSpace(p)));
				}
				toolchainMacros.Clear();
				if (settings.ToolchainMacros is not null)
				{
					toolchainMacros.AddRange(settings.ToolchainMacros.Where(m => !string.IsNullOrWhiteSpace(m?.Name)));
				}
				UpdateToolchainLibraryUi();
				UpdateToolchainMacroUi();
				UpdateToolchainPresetList();

			SetText("WorkshopDownloadAppIdTextBox", settings.WorkshopDownloadAppId);
			SetText("WorkshopDownloadSteamRootTextBox", settings.WorkshopDownloadSteamRoot);
			SetText("WorkshopDownloadIdTextBox", settings.WorkshopDownloadIdOrLink);
			SetText("WorkshopDownloadOutputTextBox", settings.WorkshopDownloadOutput);
			SetChecked("WorkshopDownloadFetchDetailsCheckBox", settings.WorkshopDownloadFetchDetails);
			SetChecked("WorkshopDownloadIncludeTitleCheckBox", settings.WorkshopDownloadIncludeTitleInName);
			SetChecked("WorkshopDownloadIncludeIdCheckBox", settings.WorkshopDownloadIncludeIdInName);
			SetChecked("WorkshopDownloadAppendUpdatedCheckBox", settings.WorkshopDownloadAppendUpdatedTimestamp);
			SetChecked("WorkshopDownloadUnderscoresCheckBox", settings.WorkshopDownloadReplaceSpacesWithUnderscores);
			SetChecked("WorkshopDownloadConvertCheckBox", settings.WorkshopDownloadConvertToExpectedFileOrFolder);
			SetChecked("WorkshopDownloadOverwriteCheckBox", settings.WorkshopDownloadOverwriteOutput);
			SetChecked("WorkshopDownloadOpenOutputCheckBox", settings.WorkshopDownloadOpenOutput);
			SetChecked("WorkshopDownloadSteamworksFallbackCheckBox", settings.WorkshopDownloadUseSteamworksFallback);
			SetChecked("WorkshopDownloadSteamCmdFallbackCheckBox", settings.WorkshopDownloadUseSteamCmdFallback);
			SetText("WorkshopDownloadSteamCmdTextBox", settings.WorkshopDownloadSteamCmdPath);
			SetText("WorkshopDownloadSteamCmdUserTextBox", settings.WorkshopDownloadSteamCmdUser);
			SetText("WorkshopDownloadSteamCmdInstallTextBox", settings.WorkshopDownloadSteamCmdInstallDirectory);

			SetText("WorkshopPublishAppIdTextBox", settings.WorkshopPublishAppId);
			SetText("WorkshopPublishSteamCmdTextBox", settings.WorkshopPublishSteamCmdPath);
			SetText("WorkshopPublishSteamCmdUserTextBox", settings.WorkshopPublishSteamCmdUser);
			SetText("WorkshopPublishPublishedIdTextBox", settings.WorkshopPublishPublishedFileId);
			SetComboBoxIndex("WorkshopPublishVisibilityComboBox", settings.WorkshopPublishVisibilityIndex);
			SetText("WorkshopPublishContentTextBox", settings.WorkshopPublishContentFolder);
			SetChecked("WorkshopPublishStageCleanCheckBox", settings.WorkshopPublishStageCleanPayload);
			SetChecked("WorkshopPublishPackVpkCheckBox", settings.WorkshopPublishPackToVpkBeforeUpload);
			SetWorkshopPublishVpkVersionInForm(settings.WorkshopPublishVpkVersion);
			SetChecked("WorkshopPublishVpkMd5CheckBox", settings.WorkshopPublishVpkIncludeMd5Sections);
			SetChecked("WorkshopPublishVpkMultiFileCheckBox", settings.WorkshopPublishVpkMultiFile);
			SetText("WorkshopPublishPreviewTextBox", settings.WorkshopPublishPreviewFile);
			SetText("WorkshopPublishTitleTextBox", settings.WorkshopPublishTitle);
			SetText("WorkshopPublishDescriptionTextBox", settings.WorkshopPublishDescription);
			SetText("WorkshopPublishChangeNoteTextBox", settings.WorkshopPublishChangeNote);
			SetText("WorkshopPublishTagsTextBox", settings.WorkshopPublishTags);
			SetComboBoxSelectionByText("WorkshopPublishGmodTypeComboBox", settings.WorkshopPublishContentType, defaultIndex: 0);
			SetWorkshopPublishGmodSelectedTags(settings.WorkshopPublishContentTags);
			SetText("WorkshopPublishVdfTextBox", settings.WorkshopPublishVdfPath);
				SetChecked("WorkshopPublishOpenPageCheckBox", settings.WorkshopPublishOpenPageWhenDone);
				workshopPublishSelectedAppId = settings.WorkshopPublishSelectedAppId;

				workshopPublishDrafts.Clear();
				if (settings.WorkshopPublishDrafts is not null)
				{
				workshopPublishDrafts.AddRange(settings.WorkshopPublishDrafts);
			}

			workshopPublishSelectedDraftId = settings.WorkshopPublishSelectedDraftId;
			var selectedDraft = GetSelectedWorkshopPublishDraft();
			if (selectedDraft is not null)
			{
				LoadWorkshopPublishDraftIntoForm(selectedDraft);
			}
				RefreshWorkshopPublishDraftsList();
					UpdateWorkshopPublishDraftStatus();
					UpdateWorkshopPublishGmodUiVisibility();
					UpdateWorkshopPublishVpkOptionsUiVisibility();
					UpdateWorkshopPublishTagPresetsUiVisibility();
					UpdateWorkshopPublishGamePresetList();
				}
		catch (Exception ex)
		{
			AppendLog($"Failed to load settings: {ex.Message}");
		}
		finally
		{
			isLoadingSettings = false;
			ScheduleViewDataViewerUpdate(delayMs: 0);
		}
	}

	private void SaveSettings()
	{
		try
		{
				var settings = new DesktopSettings
				{
				WindowWidth = Width,
				WindowHeight = Height,

				WorkFolder = GetText("OptionsWorkFolderTextBox"),

				EnableGlobalDropRouting = enableGlobalDropRouting,
				DropMdlActionIndex = dropMdlActionIndex,
				DropPackageActionIndex = dropPackageActionIndex,
				DropFolderActionIndex = dropFolderActionIndex,
				DropQcActionIndex = dropQcActionIndex,
				FileAssocVpk = GetChecked("OptionsAssocVpkCheckBox"),
				FileAssocGma = GetChecked("OptionsAssocGmaCheckBox"),
				FileAssocFpx = GetChecked("OptionsAssocFpxCheckBox"),
				FileAssocMdl = GetChecked("OptionsAssocMdlCheckBox"),
				FileAssocQc = GetChecked("OptionsAssocQcCheckBox"),
				ToolchainGoldSrcStudioMdlPath = GetText("ToolchainGoldSrcStudioMdlTextBox"),
				ToolchainSource2StudioMdlPath = GetText("ToolchainSource2StudioMdlTextBox"),

				InspectPath = GetText("InspectPathTextBox"),
				InspectComputeSha256 = GetChecked("InspectSha256CheckBox"),
				InspectMdlVersionIndex = GetComboBoxIndex("InspectMdlVersionComboBox"),

				UnpackInput = GetText("UnpackInputTextBox"),
				UnpackOutput = GetText("UnpackOutputTextBox"),
				UnpackVerifyCrc32 = GetChecked("UnpackVerifyCrcCheckBox"),
				UnpackVerifyMd5 = GetChecked("UnpackVerifyMd5CheckBox"),
				UnpackFolderPerPackage = GetChecked("UnpackFolderPerPackageCheckBox"),
				UnpackKeepFullPath = GetChecked("UnpackKeepFullPathCheckBox"),
				UnpackWriteLogFile = GetChecked("UnpackWriteLogFileCheckBox"),
				UnpackOpenOutput = GetChecked("UnpackOpenOutputCheckBox"),
				UnpackShowSizesInBytes = GetChecked("UnpackShowSizesInBytesCheckBox"),
				UnpackSavedSearches = new List<string>(unpackSavedSearches),

				PackInput = GetText("PackInputTextBox"),
				PackOutput = GetText("PackOutputTextBox"),
				PackInputModeIndex = GetComboBoxIndex("PackInputModeComboBox"),
				PackBatchOutputTypeIndex = GetComboBoxIndex("PackBatchOutputTypeComboBox"),
					PackMultiFile = GetChecked("PackMultiFileCheckBox"),
					PackWithMd5 = GetChecked("PackWithMd5CheckBox"),
						PackSplitMb = GetText("PackSplitMbTextBox"),
						PackPreloadBytes = GetText("PackPreloadBytesTextBox"),
						PackVpkVersionIndex = GetComboBoxIndex("PackVpkVersionComboBox"),
						PackVpkToolPath = GetText("PackVpkToolTextBox"),
						PackDirectOptions = GetText("PackDirectOptionsTextBox"),
						PackWriteLogFile = GetChecked("PackWriteLogFileCheckBox"),
						PackOpenOutput = GetChecked("PackOpenOutputCheckBox"),
						PackGmaCreateAddonJson = GetChecked("PackGmaCreateAddonJsonCheckBox"),
						PackGmaTitle = GetText("PackGmaTitleTextBox"),
					PackGmaDescription = GetText("PackGmaDescriptionTextBox"),
					PackGmaAuthor = GetText("PackGmaAuthorTextBox"),
					PackGmaVersion = GetText("PackGmaVersionTextBox"),
					PackGmaTags = GetText("PackGmaTagsTextBox"),
					PackGmaIgnore = GetText("PackGmaIgnoreTextBox"),
					PackGmaIgnoreWhitelist = GetChecked("PackGmaIgnoreWhitelistCheckBox"),

					DecompileMdl = GetText("DecompileMdlTextBox"),
					DecompileOutput = GetText("DecompileOutputTextBox"),
					DecompileMdlVersionIndex = GetComboBoxIndex("DecompileMdlVersionComboBox"),
					DecompileWriteQc = GetChecked("DecompileWriteQcCheckBox"),
					DecompileQcGroupIntoQciFiles = GetChecked("DecompileQcGroupIntoQciFilesCheckBox"),
					DecompileQcSkinFamilyOnSingleLine = GetChecked("DecompileQcSkinFamilyOnSingleLineCheckBox"),
					DecompileQcOnlyChangedMaterialsInTextureGroupLines = GetChecked("DecompileQcOnlyChangedMaterialsInTextureGroupLinesCheckBox"),
					DecompileQcIncludeDefineBoneLines = GetChecked("DecompileQcIncludeDefineBoneLinesCheckBox"),
					DecompileWriteRefSmd = GetChecked("DecompileWriteRefSmdCheckBox"),
					DecompileWriteLodSmd = GetChecked("DecompileWriteLodSmdCheckBox"),
						DecompileWritePhysicsSmd = GetChecked("DecompileWritePhysicsSmdCheckBox"),
						DecompileWriteTextureBmps = GetChecked("DecompileWriteTextureBmpsCheckBox"),
						DecompileWriteProceduralBonesVrd = GetChecked("DecompileWriteProceduralBonesVrdCheckBox"),
						DecompileWriteDeclareSequenceQci = GetChecked("DecompileWriteDeclareSequenceQciCheckBox"),
						DecompileWriteDebugInfoFiles = GetChecked("DecompileWriteDebugInfoFilesCheckBox"),
						DecompileWriteAnims = GetChecked("DecompileWriteAnimsCheckBox"),
						DecompileAnimsSameFolder = GetChecked("DecompileAnimsSameFolderCheckBox"),
						DecompileWriteVta = GetChecked("DecompileWriteVtaCheckBox"),
					DecompileStripMaterialPaths = GetChecked("DecompileStripMaterialPathsCheckBox"),
								DecompileMixedCaseQc = GetChecked("DecompileMixedCaseQcCheckBox"),
							DecompileNonValveUv = GetChecked("DecompileNonValveUvCheckBox"),
							DecompileFlatOutput = GetChecked("DecompileFlatOutputCheckBox"),
							DecompileIncludeSubfolders = GetChecked("DecompileIncludeSubfoldersCheckBox"),
							DecompilePrefixFileNamesWithModelName = GetChecked("DecompilePrefixFileNamesWithModelNameCheckBox"),
							DecompileStricterFormat = GetChecked("DecompileStricterFormatCheckBox"),
							DecompileWriteLogFile = GetChecked("DecompileWriteLogFileCheckBox"),
							DecompileOpenOutput = GetChecked("DecompileOpenOutputCheckBox"),

				CompileQc = GetText("CompileQcTextBox"),
				CompileGameDir = GetText("CompileGameDirTextBox"),
				CompileStudioMdl = GetText("CompileStudioMdlTextBox"),
				CompileSteamAppId = GetText("CompileSteamAppIdTextBox"),
				CompileSteamRoot = GetText("CompileSteamRootTextBox"),
				CompileWinePrefix = GetText("CompileWinePrefixTextBox"),
				CompileWineCommand = GetText("CompileWineCommandTextBox"),
				CompileNoP4 = GetChecked("CompileNoP4CheckBox"),
					CompileVerbose = GetChecked("CompileVerboseCheckBox"),
					CompileIncludeSubfolders = GetChecked("CompileIncludeSubfoldersCheckBox"),
					CompileDefineBones = GetChecked("CompileDefineBonesCheckBox"),
				CompileDefineBonesWriteQciFile = GetChecked("CompileDefineBonesWriteQciFileCheckBox"),
				CompileDefineBonesQciFileName = GetText("CompileDefineBonesQciFileNameTextBox"),
				CompileDefineBonesOverwriteQciFile = GetChecked("CompileDefineBonesOverwriteQciFileCheckBox"),
						CompileDefineBonesModifyQcFile = GetChecked("CompileDefineBonesModifyQcFileCheckBox"),
						CompileDirectOptions = GetText("CompileDirectOptionsTextBox"),
							CompileWriteLogFile = GetChecked("CompileWriteLogFileCheckBox"),
							CompileCopyOutput = GetChecked("CompileCopyOutputCheckBox"),
							CompileOutputCopyFolder = GetText("CompileOutputCopyFolderTextBox"),

					ViewMdl = GetText("ViewMdlTextBox"),
				ViewGameDir = GetText("ViewGameDirTextBox"),
				ViewHlmv = GetText("ViewHlmvTextBox"),
				ViewSteamAppId = GetText("ViewSteamAppIdTextBox"),
				ViewSteamRoot = GetText("ViewSteamRootTextBox"),
				ViewWinePrefix = GetText("ViewWinePrefixTextBox"),
				ViewWineCommand = GetText("ViewWineCommandTextBox"),
				ViewDataViewerAutoRun = GetChecked("ViewDataViewerAutoRunCheckBox"),
				ViewDataViewerMdlVersionIndex = GetComboBoxIndex("ViewDataViewerMdlVersionComboBox"),

					ToolchainSteamRoot = GetText("ToolchainSteamRootTextBox"),
					ToolchainOverrides = new List<ToolchainPresetOverrides>(toolchainOverrides),
					ToolchainSelectedAppId = toolchainSelectedAppId,
					ToolchainCustomPresets = new List<ToolchainCustomPreset>(toolchainCustomPresets),
					ToolchainSelectedCustomId = toolchainSelectedCustomId,
					ToolchainLibraryRoots = new List<string>(toolchainLibraryRoots),
					ToolchainMacros = toolchainMacros
						.Select(m => new ToolchainMacro { Name = m.Name, Path = m.Path })
						.ToList(),

				WorkshopDownloadAppId = GetText("WorkshopDownloadAppIdTextBox"),
				WorkshopDownloadSteamRoot = GetText("WorkshopDownloadSteamRootTextBox"),
				WorkshopDownloadIdOrLink = GetText("WorkshopDownloadIdTextBox"),
				WorkshopDownloadOutput = GetText("WorkshopDownloadOutputTextBox"),
				WorkshopDownloadFetchDetails = GetChecked("WorkshopDownloadFetchDetailsCheckBox"),
				WorkshopDownloadIncludeTitleInName = GetChecked("WorkshopDownloadIncludeTitleCheckBox"),
				WorkshopDownloadIncludeIdInName = GetChecked("WorkshopDownloadIncludeIdCheckBox"),
				WorkshopDownloadAppendUpdatedTimestamp = GetChecked("WorkshopDownloadAppendUpdatedCheckBox"),
				WorkshopDownloadReplaceSpacesWithUnderscores = GetChecked("WorkshopDownloadUnderscoresCheckBox"),
				WorkshopDownloadConvertToExpectedFileOrFolder = GetChecked("WorkshopDownloadConvertCheckBox"),
				WorkshopDownloadOverwriteOutput = GetChecked("WorkshopDownloadOverwriteCheckBox"),
				WorkshopDownloadOpenOutput = GetChecked("WorkshopDownloadOpenOutputCheckBox"),
				WorkshopDownloadUseSteamworksFallback = GetChecked("WorkshopDownloadSteamworksFallbackCheckBox"),
				WorkshopDownloadUseSteamCmdFallback = GetChecked("WorkshopDownloadSteamCmdFallbackCheckBox"),
				WorkshopDownloadSteamCmdPath = GetText("WorkshopDownloadSteamCmdTextBox"),
				WorkshopDownloadSteamCmdUser = GetText("WorkshopDownloadSteamCmdUserTextBox"),
				WorkshopDownloadSteamCmdInstallDirectory = GetText("WorkshopDownloadSteamCmdInstallTextBox"),

				WorkshopPublishAppId = GetText("WorkshopPublishAppIdTextBox"),
				WorkshopPublishSteamCmdPath = GetText("WorkshopPublishSteamCmdTextBox"),
				WorkshopPublishSteamCmdUser = GetText("WorkshopPublishSteamCmdUserTextBox"),
				WorkshopPublishPublishedFileId = GetText("WorkshopPublishPublishedIdTextBox"),
				WorkshopPublishVisibilityIndex = GetComboBoxIndex("WorkshopPublishVisibilityComboBox"),
				WorkshopPublishContentFolder = GetText("WorkshopPublishContentTextBox"),
				WorkshopPublishStageCleanPayload = GetChecked("WorkshopPublishStageCleanCheckBox"),
				WorkshopPublishPackToVpkBeforeUpload = GetChecked("WorkshopPublishPackVpkCheckBox"),
				WorkshopPublishVpkVersion = GetWorkshopPublishVpkVersionFromForm(),
				WorkshopPublishVpkIncludeMd5Sections = GetWorkshopPublishVpkIncludeMd5SectionsFromForm(),
				WorkshopPublishVpkMultiFile = GetChecked("WorkshopPublishVpkMultiFileCheckBox"),
				WorkshopPublishPreviewFile = GetText("WorkshopPublishPreviewTextBox"),
				WorkshopPublishTitle = GetText("WorkshopPublishTitleTextBox"),
				WorkshopPublishDescription = GetText("WorkshopPublishDescriptionTextBox"),
				WorkshopPublishChangeNote = GetText("WorkshopPublishChangeNoteTextBox"),
				WorkshopPublishTags = GetText("WorkshopPublishTagsTextBox"),
				WorkshopPublishContentType = GetComboBoxText("WorkshopPublishGmodTypeComboBox"),
				WorkshopPublishContentTags = GetWorkshopPublishGmodSelectedTags().ToList(),
					WorkshopPublishVdfPath = GetText("WorkshopPublishVdfTextBox"),
					WorkshopPublishOpenPageWhenDone = GetChecked("WorkshopPublishOpenPageCheckBox"),
					WorkshopPublishSelectedAppId = GetWorkshopPublishSelectedGameAppId(),

					WorkshopPublishDrafts = new List<WorkshopPublishDraft>(workshopPublishDrafts),
					WorkshopPublishSelectedDraftId = workshopPublishSelectedDraftId
				};

			var settingsPath = GetSettingsPath();
			var settingsDir = Path.GetDirectoryName(settingsPath);
			if (!string.IsNullOrWhiteSpace(settingsDir))
			{
				Directory.CreateDirectory(settingsDir);
			}

			var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
			{
				WriteIndented = true,
				TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
			});
			File.WriteAllText(settingsPath, json);
		}
		catch
		{
			// Ignore failures during shutdown.
		}
	}

	private static string GetSettingsPath()
	{
		var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(baseDir, "Stunstick", "desktop-settings.json");
	}

	private static string GetDefaultStunstickDocumentsFolder()
	{
		var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
		if (string.IsNullOrWhiteSpace(documents))
		{
			documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}

		if (string.IsNullOrWhiteSpace(documents))
		{
			documents = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		}

		if (string.IsNullOrWhiteSpace(documents))
		{
			documents = ".";
		}

		return Path.Combine(documents, "Stunstick");
	}

	private static string GetDefaultWorkFolder()
	{
		return Path.Combine(GetDefaultStunstickDocumentsFolder(), "Work");
	}

	private string GetWorkFolder()
	{
		var configured = GetText("OptionsWorkFolderTextBox");
		if (!string.IsNullOrWhiteSpace(configured))
		{
			try
			{
				return Path.GetFullPath(configured);
			}
			catch
			{
				return configured;
			}
		}

		return GetDefaultWorkFolder();
	}

	private void OnOptionsDropRoutingChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		try
		{
			enableGlobalDropRouting = GetChecked("OptionsGlobalDropRoutingCheckBox");
			DragDrop.SetAllowDrop(this, enableGlobalDropRouting);
			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnOptionsDropActionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		try
		{
			dropMdlActionIndex = GetComboBoxIndex("OptionsMdlDropActionComboBox");
			dropPackageActionIndex = GetComboBoxIndex("OptionsPackageDropActionComboBox");
			dropFolderActionIndex = GetComboBoxIndex("OptionsFolderDropActionComboBox");
			dropQcActionIndex = GetComboBoxIndex("OptionsQcDropActionComboBox");

			if (dropMdlActionIndex < 0)
			{
				dropMdlActionIndex = 0;
			}
			if (dropPackageActionIndex < 0)
			{
				dropPackageActionIndex = 0;
			}
			if (dropFolderActionIndex < 0)
			{
				dropFolderActionIndex = 0;
			}
			if (dropQcActionIndex < 0)
			{
				dropQcActionIndex = 0;
			}

			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnOptionsFileAssocToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		try
		{
			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private IReadOnlyList<string> GetSelectedFileAssociations()
	{
		var list = new List<string>();
		if (GetChecked("OptionsAssocVpkCheckBox"))
		{
			list.Add(".vpk");
		}
		if (GetChecked("OptionsAssocGmaCheckBox"))
		{
			list.Add(".gma");
		}
		if (GetChecked("OptionsAssocFpxCheckBox"))
		{
			list.Add(".fpx");
		}
		if (GetChecked("OptionsAssocMdlCheckBox"))
		{
			list.Add(".mdl");
		}
		if (GetChecked("OptionsAssocQcCheckBox"))
		{
			list.Add(".qc");
		}

		return list;
	}

	private void OnHelpOpenSettingsFileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var settingsPath = GetSettingsPath();
			if (!File.Exists(settingsPath))
			{
				SaveSettings();
			}

			TryOpenFile(settingsPath);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnHelpOpenTutorialClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://steamcommunity.com/groups/CrowbarTool/discussions/1/340412122422375600/");
	}

	private void OnHelpOpenContentsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://steamcommunity.com/groups/CrowbarTool/discussions/1");
	}

	private void OnHelpOpenIndexClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://steamcommunity.com/groups/CrowbarTool/discussions/");
	}

	private void OnHelpOpenTipsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://steamcommunity.com/groups/CrowbarTool/discussions/1/340412122422375600/");
	}

	private void OnHelpOpenGuideClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://steamcommunity.com/sharedfiles/filedetails/?id=504080023");
	}

	private void OnHelpOpenFaqClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenHelpResource("FAQ.md", webUrl: "https://github.com/programmer1o1/Stunstick/blob/master/FAQ.md");
	}

	private void OnHelpOpenIssuesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://github.com/programmer1o1/Stunstick/issues");
	}

	private void OnHelpOpenChangelogClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://github.com/programmer1o1/Stunstick/releases");
	}

	private void OnHelpOpenRepoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenUrl("https://github.com/programmer1o1/Stunstick");
	}

	private async void OnHelpCopyLogClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var logText = GetText("LogTextBox");
		await CopyToClipboardAsync(logText, "Copied log to clipboard.");
	}

	private async void OnHelpSaveLogClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var logText = GetText("LogTextBox") ?? string.Empty;
			var path = await BrowseSaveFileAsync(
				"Save log",
				new[]
				{
					new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } },
					new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
				},
				defaultExtension: "txt",
				suggestedFileName: $"stunstick-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

			if (string.IsNullOrWhiteSpace(path))
			{
				return;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, logText);
			AppendLog($"Saved log to: {path}");
			TryOpenFile(path);
		}
		catch (Exception ex)
		{
			AppendLog($"Save log failed: {ex.Message}");
		}
	}

	private async void OnHelpCopyVersionInfoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var info = GetVersionInfoSummary();
		await CopyToClipboardAsync(info, "Copied version info.");
	}

	private void OnHelpOpenWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var folder = GetText("OptionsWorkFolderTextBox");
			if (string.IsNullOrWhiteSpace(folder))
			{
				folder = GetDefaultStunstickDocumentsFolder();
			}

			if (string.IsNullOrWhiteSpace(folder))
			{
				AppendLog("Work folder is not set.");
				return;
			}

			Directory.CreateDirectory(folder);
			TryOpenFolder(folder);
		}
		catch (Exception ex)
		{
			AppendLog($"Open work folder failed: {ex.Message}");
		}
	}

	private async void OnOptionsRegisterFileAssocClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RegisterFileAssociationsAsync(register: true, GetSelectedFileAssociations());
	}

	private async void OnOptionsRemoveFileAssocClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RegisterFileAssociationsAsync(register: false, GetSelectedFileAssociations());
	}

	private void OnHelpOpenReadmeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenHelpResource("README.md", webUrl: "https://github.com/programmer1o1/Stunstick/blob/master/README.md");
	}

	private void OnHelpOpenCrossPlatformClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenHelpResource("CROSS_PLATFORM.md", webUrl: "https://github.com/programmer1o1/Stunstick/blob/master/CROSS_PLATFORM.md");
	}

	private void OnHelpOpenParityClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenHelpResource("parity.md", webUrl: "https://github.com/programmer1o1/Stunstick/blob/master/parity.md");
	}

	private void OnHelpOpenCliDocsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		OpenHelpResource("CROSS_PLATFORM.md", "README.md", "https://github.com/programmer1o1/Stunstick/blob/master/CROSS_PLATFORM.md#cli");
	}

	private void OnHelpOpenSettingsFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var settingsPath = GetSettingsPath();
			var settingsDir = Path.GetDirectoryName(settingsPath);
			if (string.IsNullOrWhiteSpace(settingsDir))
			{
				return;
			}

			Directory.CreateDirectory(settingsDir);
			TryOpenFolder(settingsDir);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
			}
		}

	private async void OnHelpCopySettingsPathClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await CopyToClipboardAsync(GetSettingsPath(), "Copied settings path.");
	}

	private async Task CopyToClipboardAsync(string? text, string successMessage)
	{
		if (string.IsNullOrEmpty(text))
		{
			AppendLog("Nothing to copy.");
			return;
		}

		try
		{
			var topLevel = TopLevel.GetTopLevel(this);
			if (topLevel?.Clipboard is null)
			{
				AppendLog("Clipboard unavailable on this platform.");
				return;
			}

			await topLevel.Clipboard.SetTextAsync(text);
			AppendLog(successMessage);
		}
		catch (Exception ex)
		{
			AppendLog($"Clipboard error: {ex.Message}");
		}
	}

	private string GetVersionInfoSummary()
	{
		var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
		var os = RuntimeInformation.OSDescription;
		var framework = RuntimeInformation.FrameworkDescription;
		var arch = RuntimeInformation.ProcessArchitecture.ToString();
		return $"Stunstick {version} — {framework} — {os} — {arch}";
	}

	private async Task RegisterFileAssociationsAsync(bool register, IReadOnlyList<string>? extensions = null)
	{
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				AppendLog("Shell integration is supported on Windows only.");
				return;
			}

			var assoc = (extensions is null || extensions.Count == 0)
				? new[] { ".vpk", ".gma", ".fpx", ".mdl", ".qc" }
				: extensions;

			if (assoc.Count == 0)
			{
				AppendLog("No extensions selected for shell integration.");
				return;
			}

			var exe = Process.GetCurrentProcess().MainModule?.FileName;
			if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
			{
				throw new FileNotFoundException("Could not find current executable path.");
			}

			var regPath = Path.Combine(Path.GetTempPath(), $"stunstick-assoc-{Guid.NewGuid():N}.reg");
			if (register)
			{
				var progId = "Stunstick.Package";
				var sb = new System.Text.StringBuilder();
				sb.AppendLine("Windows Registry Editor Version 5.00");
				foreach (var ext in assoc)
				{
					sb.AppendLine($@"[HKEY_CURRENT_USER\\Software\\Classes\\{ext}]");
					sb.AppendLine($@"@=""{progId}""");
					sb.AppendLine();
				}

				sb.AppendLine($@"[HKEY_CURRENT_USER\\Software\\Classes\\{progId}]");
				sb.AppendLine($@"@=""Stunstick file""");
					sb.AppendLine($@"[HKEY_CURRENT_USER\\Software\\Classes\\{progId}\\shell]");
					sb.AppendLine($@"@=""open""");
					sb.AppendLine($@"[HKEY_CURRENT_USER\\Software\\Classes\\{progId}\\shell\\open]");
					sb.AppendLine($@"[HKEY_CURRENT_USER\\Software\\Classes\\{progId}\\shell\\open\\command]");
					sb.AppendLine($"@=\"\\\"{exe}\\\" \\\"%1\\\"\"");

				File.WriteAllText(regPath, sb.ToString());
				await RunProcessAsync("reg", $"import \"{regPath}\"");
				AppendLog($"Registered file associations ({string.Join(", ", assoc)}) for current user.");
			}
			else
			{
				foreach (var ext in assoc)
				{
					await RunProcessAsync("reg", $"delete HKCU\\Software\\Classes\\{ext} /f", ignoreErrors: true);
				}
				await RunProcessAsync("reg", "delete HKCU\\Software\\Classes\\Stunstick.Package /f", ignoreErrors: true);
				AppendLog($"Removed Stunstick file associations ({string.Join(", ", assoc)}).");
			}

			try { File.Delete(regPath); } catch { }
		}
		catch (Exception ex)
		{
			AppendLog($"Shell integration failed: {ex.Message}");
		}
	}

	private static async Task RunProcessAsync(string fileName, string arguments, bool ignoreErrors = false)
	{
		var tcs = new TaskCompletionSource<int>();
		var psi = new ProcessStartInfo
		{
			FileName = fileName,
			Arguments = arguments,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
		process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

		process.Start();
		var exit = await tcs.Task.ConfigureAwait(false);
		if (!ignoreErrors && exit != 0)
		{
			throw new InvalidOperationException($"{fileName} exited with code {exit}.");
		}
	}

	private void OnAboutOpenLicenseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			static string? FindFileUpward(string startDirectory, string fileName, int maxLevels)
			{
				if (string.IsNullOrWhiteSpace(startDirectory))
				{
					return null;
				}

				var dir = new DirectoryInfo(startDirectory);
				for (var i = 0; i <= maxLevels && dir is not null; i++)
				{
					var candidate = Path.Combine(dir.FullName, fileName);
					if (File.Exists(candidate))
					{
						return candidate;
					}

					dir = dir.Parent;
				}

				return null;
			}

			var baseDir = AppContext.BaseDirectory;
			var cwd = Environment.CurrentDirectory;
			var licensePath =
				FindFileUpward(baseDir, "LICENSE.txt", maxLevels: 8) ??
				FindFileUpward(cwd, "LICENSE.txt", maxLevels: 8);

			if (string.IsNullOrWhiteSpace(licensePath))
			{
				AppendLog("License file not found.");
				return;
			}

			TryOpenFile(licensePath);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnInspectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Inspect", 0, 0, Message: "Running..."));

			var path = GetText("InspectPathTextBox");
			RequireNonEmpty(path, "Path");

				var fullPath = Path.GetFullPath(path);
				if (Directory.Exists(fullPath))
				{
					var folderOutputLines = new List<string>(capacity: 12)
					{
						$"Path: {fullPath}",
						"Type: Directory"
					};

				long totalBytes = 0;
				var fileCount = 0;
				var dirCount = 0;

				var options = new EnumerationOptions
				{
					RecurseSubdirectories = true,
					IgnoreInaccessible = true,
					AttributesToSkip = FileAttributes.ReparsePoint
				};

				progress.Report(new StunstickProgress("Inspect", 0, 0, Message: "Scanning folder..."));

				foreach (var dir in Directory.EnumerateDirectories(fullPath, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					dirCount++;
					if (dirCount % 200 == 0)
					{
						progress.Report(new StunstickProgress("Inspect", 0, 0, Message: $"Scanning… {dirCount} dirs, {fileCount} files"));
					}
				}

				foreach (var file in Directory.EnumerateFiles(fullPath, "*", options))
				{
					cancellationToken.ThrowIfCancellationRequested();
					fileCount++;
					try
					{
						totalBytes += new FileInfo(file).Length;
					}
					catch
					{
					}

					if (fileCount % 200 == 0)
					{
						progress.Report(new StunstickProgress("Inspect", 0, 0, Message: $"Scanning… {dirCount} dirs, {fileCount} files"));
					}
				}

				progress.Report(new StunstickProgress("Inspect", 0, 0, Message: "Done"));

				folderOutputLines.Add($"Dirs: {dirCount}");
				folderOutputLines.Add($"Files: {fileCount}");
				folderOutputLines.Add($"TotalBytes: {totalBytes} ({FormatBytes(totalBytes)})");

				SetInspectOutput(string.Join(Environment.NewLine, folderOutputLines));
				AppendLog($"Inspect: {fullPath} (Directory)");
				return;
			}

			RequireFileExists(fullPath, "Path");
			var computeSha256 = GetChecked("InspectSha256CheckBox");

			var outputLines = new List<string>(capacity: 24);

			var result = await app.InspectAsync(fullPath, new InspectOptions(ComputeSha256: computeSha256), cancellationToken);
			outputLines.Add($"Path: {result.Path}");
			outputLines.Add($"Type: {result.FileType}");
			outputLines.Add($"SizeBytes: {result.SizeBytes} ({FormatBytes(result.SizeBytes)})");
			if (result.Sha256Hex is not null)
			{
				outputLines.Add($"Sha256: {result.Sha256Hex}");
			}

			if (result.FileType == Stunstick.Core.StunstickFileType.Vpk)
			{
				var vpk = await app.InspectVpkAsync(path, cancellationToken);

				outputLines.Add(string.Empty);
				outputLines.Add("VPK/FPX:");
				if (!string.Equals(Path.GetFullPath(path), vpk.DirectoryFilePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				{
					outputLines.Add($"  Input: {vpk.InputPath}");
				}
				outputLines.Add($"  DirectoryFile: {vpk.DirectoryFilePath}");
				outputLines.Add($"  Signature: 0x{vpk.Signature:x8}");
				outputLines.Add($"  Version: {vpk.Version}");
				outputLines.Add($"  Entries: {vpk.EntryCount}");
				outputLines.Add($"  TotalEntryBytes: {vpk.TotalEntryBytes} ({FormatBytes(vpk.TotalEntryBytes)})");
				outputLines.Add($"  Archives: {vpk.ArchiveCount}");
				outputLines.Add($"  DirectoryTreeSize: {vpk.DirectoryTreeSize} ({FormatBytes(vpk.DirectoryTreeSize)})");
				if (vpk.Version == 2)
				{
					outputLines.Add($"  FileDataSectionSize: {vpk.FileDataSectionSize} ({FormatBytes(vpk.FileDataSectionSize)})");
					outputLines.Add($"  ArchiveMd5SectionSize: {vpk.ArchiveMd5SectionSize} ({FormatBytes(vpk.ArchiveMd5SectionSize)})");
					outputLines.Add($"  OtherMd5SectionSize: {vpk.OtherMd5SectionSize} ({FormatBytes(vpk.OtherMd5SectionSize)})");
					outputLines.Add($"  SignatureSectionSize: {vpk.SignatureSectionSize} ({FormatBytes(vpk.SignatureSectionSize)})");
					outputLines.Add($"  HasSignatureSection: {vpk.HasSignatureSection}");
				}
			}

			if (result.FileType == Stunstick.Core.StunstickFileType.Mdl)
			{
				var versionOverride = GetInspectMdlVersionOverride();
				if (versionOverride is not null)
				{
					outputLines.Add($"OverrideMdlVersion: {versionOverride.Value}");
				}

				var mdl = await app.InspectMdlAsync(fullPath, new MdlInspectOptions(VersionOverride: versionOverride), cancellationToken);
				AppendMdlInspectLines(outputLines, mdl);
			}

			SetInspectOutput(string.Join(Environment.NewLine, outputLines));
			AppendLog($"Inspect: {result.Path} ({result.FileType})");
		});
	}

		private void OnInspectMdlVersionChanged(object? sender, SelectionChangedEventArgs e)
		{
			if (isLoadingSettings)
			{
			return;
		}

		try
		{
			SaveSettings();
		}
		catch
		{
			}
		}

		private void OnDecompileMdlVersionChanged(object? sender, SelectionChangedEventArgs e)
		{
			if (isLoadingSettings)
			{
				return;
			}

			try
			{
				SaveSettings();
			}
			catch
			{
			}
		}

			private void OnExploreFromInspectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				try
				{
			var path = GetText("InspectPathTextBox");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("UnpackInputTextBox").Text = path;
			}

				SelectMainTab("ExploreTabItem");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
		}
	}

	private async Task<string?> PromptSteamCmdAsync(SteamCmdPrompt prompt, CancellationToken cancellationToken)
	{
		return await Dispatcher.UIThread.InvokeAsync(async () =>
		{
			var dialog = new SteamCmdPromptWindow(prompt);

			using var cancellationRegistration = cancellationToken.Register(() =>
			{
				try
				{
					Dispatcher.UIThread.Post(() => dialog.Close(null));
				}
				catch
				{
				}
			});

			return await dialog.ShowDialog<string?>(this);
		});
	}

	private void UpdateWorkshopPublishGmodUiVisibility()
	{
		var show = IsWorkshopPublishGarrysMod();

		GetRequiredControl<TextBlock>("WorkshopPublishGmodTypeLabelTextBlock").IsVisible = show;
		GetRequiredControl<ComboBox>("WorkshopPublishGmodTypeComboBox").IsVisible = show;
		GetRequiredControl<TextBlock>("WorkshopPublishGmodTagsLabelTextBlock").IsVisible = show;
		GetRequiredControl<StackPanel>("WorkshopPublishGmodTagsPanel").IsVisible = show;
		GetRequiredControl<CheckBox>("WorkshopPublishPackVpkCheckBox").IsVisible = !show;
		UpdateWorkshopPublishVpkOptionsUiVisibility();

		if (!show)
		{
			GetRequiredControl<TextBlock>("WorkshopPublishGmodTagsStatusTextBlock").Text = string.Empty;
			return;
		}

		UpdateWorkshopPublishGmodTagsStatusText();
	}

	private void UpdateWorkshopPublishVpkOptionsUiVisibility()
	{
		var isGmod = IsWorkshopPublishGarrysMod();

		var packVpkCheckBox = GetRequiredControl<CheckBox>("WorkshopPublishPackVpkCheckBox");
		var optionsPanel = GetRequiredControl<StackPanel>("WorkshopPublishVpkOptionsPanel");
		optionsPanel.IsVisible = !isGmod && packVpkCheckBox.IsVisible && packVpkCheckBox.IsChecked == true;

		var md5CheckBox = GetRequiredControl<CheckBox>("WorkshopPublishVpkMd5CheckBox");
		var vpkVersion = GetWorkshopPublishVpkVersionFromForm();
		var isV2 = vpkVersion == 2;

		md5CheckBox.IsEnabled = isV2;
		if (!isV2)
		{
			md5CheckBox.IsChecked = false;
		}
	}

	private uint GetWorkshopPublishVpkVersionFromForm()
	{
		return GetComboBoxIndex("WorkshopPublishVpkVersionComboBox") switch
		{
			1 => 2u,
			_ => 1u
		};
	}

	private bool GetWorkshopPublishVpkIncludeMd5SectionsFromForm()
	{
		return GetWorkshopPublishVpkVersionFromForm() == 2 && GetChecked("WorkshopPublishVpkMd5CheckBox");
	}

	private void SetWorkshopPublishVpkVersionInForm(uint vpkVersion)
	{
		SetComboBoxIndex("WorkshopPublishVpkVersionComboBox", vpkVersion == 2 ? 1 : 0);
	}

	private bool IsWorkshopPublishGarrysMod()
	{
		var appIdText = GetText("WorkshopPublishAppIdTextBox");
		if (string.IsNullOrWhiteSpace(appIdText))
		{
			return true; // Default AppID watermark is 4000.
		}

		return uint.TryParse(appIdText.Trim(), out var appId) && appId == 4000;
	}

	private IReadOnlyList<CheckBox> GetWorkshopPublishGmodTagCheckBoxes()
	{
		var panel = GetRequiredControl<WrapPanel>("WorkshopPublishGmodTagsWrapPanel");
		return panel.Children.OfType<CheckBox>().ToArray();
	}

	private IReadOnlyList<string> GetWorkshopPublishGmodSelectedTags()
	{
		return GetWorkshopPublishGmodTagCheckBoxes()
			.Where(cb => cb.IsChecked == true)
			.Select(cb => cb.Tag?.ToString())
			.Where(tag => !string.IsNullOrWhiteSpace(tag))
			.Select(tag => tag!)
			.ToArray();
	}

	private void SetWorkshopPublishGmodSelectedTags(IEnumerable<string>? tags)
	{
		var selected = new HashSet<string>(tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

		isWorkshopPublishGmodTagEnforcingLimit = true;
		try
		{
			foreach (var checkbox in GetWorkshopPublishGmodTagCheckBoxes())
			{
				var tag = checkbox.Tag?.ToString() ?? string.Empty;
				checkbox.IsChecked = selected.Contains(tag);
			}
		}
		finally
		{
			isWorkshopPublishGmodTagEnforcingLimit = false;
		}

		UpdateWorkshopPublishGmodTagsStatusText();
	}

	private void SetComboBoxSelectionByText(string comboBoxName, string? text, int defaultIndex = 0)
	{
		var combo = GetRequiredControl<ComboBox>(comboBoxName);
		var target = text?.Trim();
		if (string.IsNullOrWhiteSpace(target))
		{
			SetComboBoxIndex(comboBoxName, defaultIndex);
			return;
		}

		if (combo.Items is System.Collections.IEnumerable items)
		{
			var index = 0;
			foreach (var item in items)
			{
				var itemText = item switch
				{
					ComboBoxItem comboItem => comboItem.Content?.ToString(),
					_ => item?.ToString()
				};

				if (!string.IsNullOrWhiteSpace(itemText) && string.Equals(itemText.Trim(), target, StringComparison.OrdinalIgnoreCase))
				{
					SetComboBoxIndex(comboBoxName, index);
					return;
				}

				index++;
			}
		}

		SetComboBoxIndex(comboBoxName, defaultIndex);
	}

	private void UpdateWorkshopPublishGmodTagsStatusText()
	{
		var status = GetRequiredControl<TextBlock>("WorkshopPublishGmodTagsStatusTextBlock");
		var tags = GetWorkshopPublishGmodSelectedTags();

		status.Text = tags.Count switch
		{
			0 => "Selected: 0/2",
			_ => $"Selected: {tags.Count}/2 ({string.Join(", ", tags)})"
		};
	}

		private void OnWorkshopPublishGmodTagToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
		if (isWorkshopPublishDraftFormLoading || isWorkshopPublishGmodTagEnforcingLimit)
		{
			return;
		}

		try
		{
			isWorkshopPublishGmodTagEnforcingLimit = true;

			var selectedTags = GetWorkshopPublishGmodSelectedTags();
			if (sender is CheckBox changed && changed.IsChecked == true && selectedTags.Count > 2)
			{
				changed.IsChecked = false;
				GetRequiredControl<TextBlock>("WorkshopPublishGmodTagsStatusTextBlock").Text = "Pick up to 2 tags.";
			}
		}
		finally
		{
			isWorkshopPublishGmodTagEnforcingLimit = false;
		}

			UpdateWorkshopPublishGmodTagsStatusText();
			OnWorkshopPublishDraftFieldChanged();
		}

		private void UpdateWorkshopPublishTagPresetsUiVisibility()
		{
			var panel = GetRequiredControl<StackPanel>("WorkshopPublishTagPresetsPanel");
			var label = GetRequiredControl<TextBlock>("WorkshopPublishTagPresetsLabelTextBlock");
			var wrap = GetRequiredControl<WrapPanel>("WorkshopPublishTagPresetsWrapPanel");

			uint appId = 4000;
			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			if (!string.IsNullOrWhiteSpace(appIdText) && (!uint.TryParse(appIdText.Trim(), out appId) || appId == 0))
			{
				panel.IsVisible = false;
				wrap.Children.Clear();
				workshopPublishTagPresetsForAppId = null;
				return;
			}

			if (appId == 4000 || !WorkshopPublishTagPresets.TryGetValue(appId, out var presets) || presets.Count == 0)
			{
				panel.IsVisible = false;
				wrap.Children.Clear();
				workshopPublishTagPresetsForAppId = null;
				return;
			}

			panel.IsVisible = true;
			label.Text = $"Tag presets (AppID {appId})";

			if (workshopPublishTagPresetsForAppId != appId)
			{
				wrap.Children.Clear();
				foreach (var preset in presets.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					var tag = preset.Trim();
					if (string.IsNullOrWhiteSpace(tag))
					{
						continue;
					}

					var checkBox = new CheckBox
					{
						Content = tag,
						Tag = tag,
						Margin = new Thickness(0, 0, 12, 6)
					};

					checkBox.IsCheckedChanged += OnWorkshopPublishTagPresetToggled;
					wrap.Children.Add(checkBox);
				}

				workshopPublishTagPresetsForAppId = appId;
			}

			SyncWorkshopPublishTagPresetCheckboxesFromText();
		}

		private void OnWorkshopPublishTagPresetToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			if (isWorkshopPublishDraftFormLoading || isWorkshopPublishTagPresetsSyncing)
			{
				return;
			}

			try
			{
				isWorkshopPublishTagPresetsSyncing = true;

				uint appId = 4000;
				var appIdText = GetText("WorkshopPublishAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(appIdText) && (!uint.TryParse(appIdText.Trim(), out appId) || appId == 0))
				{
					return;
				}

				if (appId == 4000 || !WorkshopPublishTagPresets.TryGetValue(appId, out var presets) || presets.Count == 0)
				{
					return;
				}

				var presetSet = new HashSet<string>(presets.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)), StringComparer.OrdinalIgnoreCase);
				var existingTags = ParseCsvTags(GetText("WorkshopPublishTagsTextBox"));
				var merged = existingTags.Where(t => !presetSet.Contains(t)).ToList();

				var wrap = GetRequiredControl<WrapPanel>("WorkshopPublishTagPresetsWrapPanel");
				foreach (var checkbox in wrap.Children.OfType<CheckBox>())
				{
					if (checkbox.IsChecked == true && checkbox.Tag is string tag && !string.IsNullOrWhiteSpace(tag))
					{
						merged.Add(tag.Trim());
					}
				}

				var combined = merged
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray();

				GetRequiredControl<TextBox>("WorkshopPublishTagsTextBox").Text = combined.Length == 0 ? string.Empty : string.Join(",", combined);
			}
			finally
			{
				isWorkshopPublishTagPresetsSyncing = false;
			}
		}

		private void SyncWorkshopPublishTagPresetCheckboxesFromText()
		{
			if (isWorkshopPublishDraftFormLoading || isWorkshopPublishTagPresetsSyncing)
			{
				return;
			}

			var panel = GetRequiredControl<StackPanel>("WorkshopPublishTagPresetsPanel");
			if (!panel.IsVisible)
			{
				return;
			}

			try
			{
				isWorkshopPublishTagPresetsSyncing = true;

				var set = new HashSet<string>(ParseCsvTags(GetText("WorkshopPublishTagsTextBox")), StringComparer.OrdinalIgnoreCase);
				var wrap = GetRequiredControl<WrapPanel>("WorkshopPublishTagPresetsWrapPanel");
				foreach (var checkbox in wrap.Children.OfType<CheckBox>())
				{
					var tag = checkbox.Tag?.ToString() ?? string.Empty;
					if (string.IsNullOrWhiteSpace(tag))
					{
						continue;
					}

					checkbox.IsChecked = set.Contains(tag.Trim());
				}
			}
			finally
			{
				isWorkshopPublishTagPresetsSyncing = false;
			}
		}

			private static IReadOnlyList<string> ParseCsvTags(string? value)
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					return Array.Empty<string>();
				}

				return value
					.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(t => !string.IsNullOrWhiteSpace(t))
					.ToArray();
			}

		private void OnWorkshopPublishGameSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			if (isWorkshopPublishDraftFormLoading || isWorkshopPublishGamePresetSyncing)
			{
				return;
			}

			try
			{
				isWorkshopPublishGamePresetSyncing = true;

				var combo = GetRequiredControl<ComboBox>("WorkshopPublishGameComboBox");
				if (combo.SelectedItem is not ToolchainPresetListItem item)
				{
					workshopPublishSelectedAppId = null;
					return;
				}

				workshopPublishSelectedAppId = item.Preset.AppId;
				GetRequiredControl<TextBox>("WorkshopPublishAppIdTextBox").Text = item.Preset.AppId.ToString();
			}
			finally
			{
				isWorkshopPublishGamePresetSyncing = false;
			}
		}

		private void UpdateWorkshopPublishGamePresetList()
		{
			var combo = GetRequiredControl<ComboBox>("WorkshopPublishGameComboBox");
			combo.ItemsSource = toolchainAllPresets;

			uint? desiredAppId = workshopPublishSelectedAppId;
			if (desiredAppId is null)
			{
				var appIdText = GetText("WorkshopPublishAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(appIdText) && uint.TryParse(appIdText.Trim(), out var parsed) && parsed != 0)
				{
					desiredAppId = parsed;
				}
			}

			if (desiredAppId is null)
			{
				combo.SelectedItem = null;
				return;
			}

			var match = toolchainAllPresets.FirstOrDefault(p => p.Preset.AppId == desiredAppId.Value);
			combo.SelectedItem = match;
		}

		private void SyncWorkshopPublishGameSelectionFromAppIdText()
		{
			if (isWorkshopPublishDraftFormLoading || isWorkshopPublishGamePresetSyncing)
			{
				return;
			}

			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			if (string.IsNullOrWhiteSpace(appIdText))
			{
				workshopPublishSelectedAppId = null;
				return;
			}

			if (!uint.TryParse(appIdText.Trim(), out var parsed) || parsed == 0)
			{
				return;
			}

			workshopPublishSelectedAppId = parsed;

			if (toolchainAllPresets.Count == 0)
			{
				return;
			}

			try
			{
				isWorkshopPublishGamePresetSyncing = true;
				var combo = GetRequiredControl<ComboBox>("WorkshopPublishGameComboBox");
				var match = toolchainAllPresets.FirstOrDefault(p => p.Preset.AppId == parsed);
				combo.SelectedItem = match;
			}
			finally
			{
				isWorkshopPublishGamePresetSyncing = false;
			}
		}

		private uint? GetWorkshopPublishSelectedGameAppId()
		{
			var combo = GetRequiredControl<ComboBox>("WorkshopPublishGameComboBox");
			if (combo.SelectedItem is ToolchainPresetListItem item)
			{
				return item.Preset.AppId;
			}

			if (workshopPublishSelectedAppId is not null)
			{
				return workshopPublishSelectedAppId.Value;
			}

			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			return !string.IsNullOrWhiteSpace(appIdText) && uint.TryParse(appIdText.Trim(), out var parsed) && parsed != 0 ? parsed : null;
		}

		private void OnWorkshopDownloadPreviewTextChanged(object? sender, TextChangedEventArgs e)
		{
			if (isLoadingSettings)
			{
				return;
			}

			UpdateWorkshopDownloadExampleOutput();
		}

			private void OnWorkshopDownloadPreviewCheckChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				if (isLoadingSettings)
				{
					return;
				}

				UpdateWorkshopDownloadUiState();
				UpdateWorkshopDownloadExampleOutput();
			}

			private void UpdateWorkshopDownloadUiState()
			{
				if (isWorkshopDownloadUiSyncing)
				{
					return;
				}

				try
				{
					isWorkshopDownloadUiSyncing = true;

					var includeTitle = GetChecked("WorkshopDownloadIncludeTitleCheckBox");
					var appendUpdated = GetChecked("WorkshopDownloadAppendUpdatedCheckBox");
					var requiresDetails = includeTitle || appendUpdated;

					var fetchDetailsCheckBox = GetRequiredControl<CheckBox>("WorkshopDownloadFetchDetailsCheckBox");
					if (requiresDetails)
					{
						fetchDetailsCheckBox.IsChecked = true;
					}
					fetchDetailsCheckBox.IsEnabled = !requiresDetails;

					var useSteamCmd = GetChecked("WorkshopDownloadSteamCmdFallbackCheckBox");
					GetRequiredControl<TextBox>("WorkshopDownloadSteamCmdTextBox").IsEnabled = useSteamCmd;
					GetRequiredControl<Button>("WorkshopDownloadSteamCmdBrowseButton").IsEnabled = useSteamCmd;
					GetRequiredControl<TextBox>("WorkshopDownloadSteamCmdUserTextBox").IsEnabled = useSteamCmd;
					GetRequiredControl<TextBox>("WorkshopDownloadSteamCmdInstallTextBox").IsEnabled = useSteamCmd;
					GetRequiredControl<Button>("WorkshopDownloadSteamCmdInstallBrowseButton").IsEnabled = useSteamCmd;
				}
				catch
				{
				}
				finally
				{
					isWorkshopDownloadUiSyncing = false;
				}
			}

			private void UpdateWorkshopDownloadExampleOutput()
			{
				try
				{
					var exampleTextBox = GetRequiredControl<TextBox>("WorkshopDownloadExampleTextBox");
				var noteTextBlock = GetRequiredControl<TextBlock>("WorkshopDownloadExampleNoteTextBlock");

				var idOrLink = GetText("WorkshopDownloadIdTextBox");
				if (!WorkshopIdParser.TryParsePublishedFileId(idOrLink, out var publishedFileId))
				{
					exampleTextBox.Text = string.Empty;
					noteTextBlock.Text = string.Empty;
					return;
				}

				var appIdText = GetText("WorkshopDownloadAppIdTextBox");
				var appId = 4000u;
				if (!string.IsNullOrWhiteSpace(appIdText))
				{
					if (!uint.TryParse(appIdText.Trim(), out appId) || appId == 0)
					{
						exampleTextBox.Text = string.Empty;
						noteTextBlock.Text = "Invalid AppID.";
						return;
					}
				}

				var includeTitle = GetChecked("WorkshopDownloadIncludeTitleCheckBox");
				var includeId = GetChecked("WorkshopDownloadIncludeIdCheckBox");
				var appendUpdated = GetChecked("WorkshopDownloadAppendUpdatedCheckBox");
				var replaceSpaces = GetChecked("WorkshopDownloadUnderscoresCheckBox");
				var convert = GetChecked("WorkshopDownloadConvertCheckBox");

				var outputDirectory = GetText("WorkshopDownloadOutputTextBox");
				var outputDirectoryForPreview = string.Empty;
				if (!string.IsNullOrWhiteSpace(outputDirectory))
				{
					outputDirectoryForPreview = Path.GetFullPath(outputDirectory);
				}

				string? contentNameBase = null;
				bool? isDirectory = null;
				string? extension = null;
				string? cacheNote = null;

				if (convert)
				{
					var steamRootText = GetText("WorkshopDownloadSteamRootTextBox");
					var steamRootOverride = string.IsNullOrWhiteSpace(steamRootText) ? null : steamRootText.Trim();
					var steamRoot = Stunstick.Core.Steam.SteamInstallLocator.FindSteamRoot(steamRootOverride);

					if (steamRoot is not null)
					{
						var contentDir = TryFindWorkshopContentDirectoryInCache(steamRoot, appId, publishedFileId);
						if (!string.IsNullOrWhiteSpace(contentDir))
						{
							try
							{
								var topDirectories = Directory.EnumerateDirectories(contentDir, "*", SearchOption.TopDirectoryOnly).ToArray();
								var topFiles = Directory.EnumerateFiles(contentDir, "*", SearchOption.TopDirectoryOnly).ToArray();

								if (topDirectories.Length == 0 && topFiles.Length == 1)
								{
									isDirectory = false;
									extension = Path.GetExtension(topFiles[0]);
									contentNameBase = Path.GetFileNameWithoutExtension(topFiles[0]);
								}
								else
								{
									isDirectory = true;
								}
							}
							catch
							{
								isDirectory = true;
							}
						}
						else
						{
							cacheNote = $"Not found in Steam Workshop cache for AppID {appId}.";
						}
					}
				}

				var baseName = WorkshopNaming.BuildOutputBaseName(
					publishedFileId,
					details: null,
					new WorkshopDownloadNamingOptions(
						IncludeTitle: includeTitle,
						IncludeId: includeId,
						AppendUpdatedTimestamp: appendUpdated,
						ReplaceSpacesWithUnderscores: replaceSpaces),
					contentNameBase);

				var previewPath = baseName;
				if (isDirectory == false)
				{
					previewPath = previewPath + (extension ?? string.Empty);
				}

				if (!string.IsNullOrWhiteSpace(outputDirectoryForPreview))
				{
					previewPath = Path.Combine(outputDirectoryForPreview, previewPath);
				}

				exampleTextBox.Text = previewPath;

				var notes = new List<string>();
				if (includeTitle || appendUpdated)
				{
					notes.Add("Title/date shown after Steam details fetch.");
				}

				if (!string.IsNullOrWhiteSpace(cacheNote))
				{
					notes.Add(cacheNote);
				}

				noteTextBlock.Text = string.Join(" ", notes);
			}
			catch
			{
			}
		}

		private static string? TryFindWorkshopContentDirectoryInCache(string steamRoot, uint appId, ulong publishedFileId)
		{
			foreach (var libraryRoot in Stunstick.Core.Steam.SteamLibraryScanner.GetLibraryRoots(steamRoot))
			{
				var candidate = Path.Combine(
					libraryRoot,
					"steamapps",
					"workshop",
					"content",
					appId.ToString(),
					publishedFileId.ToString());

				if (Directory.Exists(candidate))
				{
					return candidate;
				}
			}

			return null;
		}

			private async void OnWorkshopDownloadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
			progress.Report(new StunstickProgress("Workshop Download", 0, 0, Message: "Starting..."));

			var idOrLink = GetText("WorkshopDownloadIdTextBox");
			RequireNonEmpty(idOrLink, "Item ID/Link");

			var outputDirectory = GetText("WorkshopDownloadOutputTextBox");
			RequireNonEmpty(outputDirectory, "Output folder");

			var appIdText = GetText("WorkshopDownloadAppIdTextBox");
			var appId = 4000u;
			if (!string.IsNullOrWhiteSpace(appIdText))
			{
				if (!uint.TryParse(appIdText, out appId) || appId == 0)
				{
					throw new InvalidDataException("AppID must be a positive integer.");
				}
			}

			var steamRoot = GetText("WorkshopDownloadSteamRootTextBox");
			if (string.IsNullOrWhiteSpace(steamRoot))
			{
				steamRoot = null;
			}

			var includeTitle = GetChecked("WorkshopDownloadIncludeTitleCheckBox");
			var includeId = GetChecked("WorkshopDownloadIncludeIdCheckBox");
			var appendUpdated = GetChecked("WorkshopDownloadAppendUpdatedCheckBox");
			var replaceSpaces = GetChecked("WorkshopDownloadUnderscoresCheckBox");
			var overwrite = GetChecked("WorkshopDownloadOverwriteCheckBox");
			var convert = GetChecked("WorkshopDownloadConvertCheckBox");
			var fetchDetails = GetChecked("WorkshopDownloadFetchDetailsCheckBox") || includeTitle || appendUpdated;

			var steamworksFallback = GetChecked("WorkshopDownloadSteamworksFallbackCheckBox");
			var steamCmdFallback = GetChecked("WorkshopDownloadSteamCmdFallbackCheckBox");
			var steamCmdPath = GetText("WorkshopDownloadSteamCmdTextBox");
			if (string.IsNullOrWhiteSpace(steamCmdPath))
			{
				steamCmdPath = null;
			}

			var steamCmdUser = GetText("WorkshopDownloadSteamCmdUserTextBox");
			if (string.IsNullOrWhiteSpace(steamCmdUser))
			{
				steamCmdUser = null;
			}

			var steamCmdInstall = GetText("WorkshopDownloadSteamCmdInstallTextBox");
			if (string.IsNullOrWhiteSpace(steamCmdInstall))
			{
				steamCmdInstall = null;
			}

			var result = await app.DownloadWorkshopItemAsync(
				new WorkshopDownloadRequest(
					IdOrLink: idOrLink,
					OutputDirectory: outputDirectory,
					AppId: appId,
					SteamRoot: steamRoot,
					ConvertToExpectedFileOrFolder: convert,
					FetchDetails: fetchDetails,
					OverwriteExisting: overwrite,
					NamingOptions: new WorkshopDownloadNamingOptions(
						IncludeTitle: includeTitle,
						IncludeId: includeId,
						AppendUpdatedTimestamp: appendUpdated,
						ReplaceSpacesWithUnderscores: replaceSpaces),
					Progress: progress,
					Output: new Progress<string>(AppendLog),
					UseSteamworksWhenNotCached: steamworksFallback,
					UseSteamCmdWhenNotCached: steamCmdFallback,
					SteamCmdPath: steamCmdPath,
					SteamCmdInstallDirectory: steamCmdInstall,
					SteamCmdUsername: steamCmdUser,
					SteamCmdPromptAsync: PromptSteamCmdAsync),
				cancellationToken);

			lastWorkshopDownloadOutputPath = result.OutputPath;

			if (!string.IsNullOrWhiteSpace(result.Details?.Title))
			{
				AppendLog($"Workshop: {result.Details!.Title}");
			}

			AppendLog($"Workshop: downloaded {result.PublishedFileId} -> {result.OutputPath}");

			if (GetChecked("WorkshopDownloadOpenOutputCheckBox"))
			{
				if (result.OutputType == WorkshopDownloadOutputType.File)
				{
					var folder = Path.GetDirectoryName(result.OutputPath);
					if (!string.IsNullOrWhiteSpace(folder))
					{
						TryOpenFolder(folder);
					}
				}
				else
				{
					TryOpenFolder(result.OutputPath);
				}
			}
		});
	}

	private void OnWorkshopOpenPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var input = GetText("WorkshopDownloadIdTextBox");
			if (!WorkshopIdParser.TryParsePublishedFileId(input, out var publishedFileId))
			{
				AppendLog("Workshop: could not parse item ID.");
				return;
			}

			TryOpenUri($"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}");
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnWorkshopDownloadOutputDocumentsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var folder = Path.Combine(GetDefaultStunstickDocumentsFolder(), "Workshop");
			GetRequiredControl<TextBox>("WorkshopDownloadOutputTextBox").Text = folder;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnWorkshopDownloadOutputWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var folder = Path.Combine(GetWorkFolder(), "Workshop");
			GetRequiredControl<TextBox>("WorkshopDownloadOutputTextBox").Text = folder;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

		private void OnWorkshopUseInOtherTabClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(lastWorkshopDownloadOutputPath))
				{
					AppendLog("Workshop: download something first.");
					return;
				}

				if (!TryRoutePathToTab(lastWorkshopDownloadOutputPath))
				{
					AppendLog("Workshop: output path no longer exists.");
				}
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

	private void OnWorkshopPublishDraftSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (sender is not ListBox listBox)
		{
			return;
		}

		if (listBox.SelectedItem is not WorkshopPublishDraftListItem item)
		{
			workshopPublishSelectedDraftId = null;
			UpdateWorkshopPublishDraftStatus();
			SaveSettings();
			return;
		}

		workshopPublishSelectedDraftId = item.Draft.Id;
		LoadWorkshopPublishDraftIntoForm(item.Draft);
		UpdateWorkshopPublishDraftStatus();
		SaveSettings();
	}

	private void OnWorkshopPublishDraftNewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var draft = CreateWorkshopPublishDraftFromForm();
			draft.Name ??= !string.IsNullOrWhiteSpace(draft.Title) ? draft.Title : $"Draft {workshopPublishDrafts.Count + 1}";

			workshopPublishDrafts.Add(draft);
			workshopPublishSelectedDraftId = draft.Id;

			RefreshWorkshopPublishDraftsList();
			UpdateWorkshopPublishDraftStatus();
			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnWorkshopPublishDraftSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var existing = GetSelectedWorkshopPublishDraft();
			if (existing is null)
			{
				AppendLog("Workshop: select a draft first.");
				return;
			}

			var updated = CreateWorkshopPublishDraftFromForm(existing.Id, existing.CreatedAtUtc);
			updated.Name = existing.Name;

			var index = workshopPublishDrafts.FindIndex(d => d.Id == existing.Id);
			if (index >= 0)
			{
				workshopPublishDrafts[index] = updated;
			}

			workshopPublishSelectedDraftId = updated.Id;
			RefreshWorkshopPublishDraftsList();
			UpdateWorkshopPublishDraftStatus();
			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnWorkshopPublishDraftRevertClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var existing = GetSelectedWorkshopPublishDraft();
			if (existing is null)
			{
				AppendLog("Workshop: select a draft first.");
				return;
			}

			LoadWorkshopPublishDraftIntoForm(existing);
			UpdateWorkshopPublishDraftStatus();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnWorkshopPublishDraftDeleteClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var existing = GetSelectedWorkshopPublishDraft();
			if (existing is null)
			{
				AppendLog("Workshop: select a draft first.");
				return;
			}

			workshopPublishDrafts.RemoveAll(d => d.Id == existing.Id);
			workshopPublishSelectedDraftId = null;
			RefreshWorkshopPublishDraftsList();
			UpdateWorkshopPublishDraftStatus();
			SaveSettings();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private WorkshopPublishDraft CreateWorkshopPublishDraftFromForm(string? draftId = null, DateTimeOffset? createdAtUtc = null)
	{
		var now = DateTimeOffset.UtcNow;
		return new WorkshopPublishDraft
		{
			Id = draftId ?? Guid.NewGuid().ToString("N"),
			CreatedAtUtc = createdAtUtc ?? now,
			UpdatedAtUtc = now,

			AppId = GetText("WorkshopPublishAppIdTextBox"),
			SteamCmdPath = GetText("WorkshopPublishSteamCmdTextBox"),
			SteamCmdUser = GetText("WorkshopPublishSteamCmdUserTextBox"),
			PublishedFileId = GetText("WorkshopPublishPublishedIdTextBox"),
			VisibilityIndex = GetComboBoxIndex("WorkshopPublishVisibilityComboBox"),
			ContentFolder = GetText("WorkshopPublishContentTextBox"),
			StageCleanPayload = GetChecked("WorkshopPublishStageCleanCheckBox"),
			PackToVpkBeforeUpload = GetChecked("WorkshopPublishPackVpkCheckBox"),
			VpkVersion = GetWorkshopPublishVpkVersionFromForm(),
			VpkIncludeMd5Sections = GetWorkshopPublishVpkIncludeMd5SectionsFromForm(),
			VpkMultiFile = GetChecked("WorkshopPublishVpkMultiFileCheckBox"),
			PreviewFile = GetText("WorkshopPublishPreviewTextBox"),
			Title = GetText("WorkshopPublishTitleTextBox"),
			Description = GetRequiredControl<TextBox>("WorkshopPublishDescriptionTextBox").Text ?? string.Empty,
			ChangeNote = GetRequiredControl<TextBox>("WorkshopPublishChangeNoteTextBox").Text ?? string.Empty,
			Tags = GetText("WorkshopPublishTagsTextBox"),
			ContentType = GetComboBoxText("WorkshopPublishGmodTypeComboBox"),
			ContentTags = GetWorkshopPublishGmodSelectedTags().ToList(),
			VdfPath = GetText("WorkshopPublishVdfTextBox")
		};
	}

	private void LoadWorkshopPublishDraftIntoForm(WorkshopPublishDraft draft)
	{
		if (draft is null)
		{
			return;
		}

		try
		{
			isWorkshopPublishDraftFormLoading = true;

			GetRequiredControl<TextBox>("WorkshopPublishAppIdTextBox").Text = draft.AppId ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishSteamCmdTextBox").Text = draft.SteamCmdPath ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishSteamCmdUserTextBox").Text = draft.SteamCmdUser ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishPublishedIdTextBox").Text = draft.PublishedFileId ?? string.Empty;
			SetComboBoxIndex("WorkshopPublishVisibilityComboBox", draft.VisibilityIndex);
			GetRequiredControl<TextBox>("WorkshopPublishContentTextBox").Text = draft.ContentFolder ?? string.Empty;
			SetChecked("WorkshopPublishStageCleanCheckBox", draft.StageCleanPayload);
			SetChecked("WorkshopPublishPackVpkCheckBox", draft.PackToVpkBeforeUpload);
			SetWorkshopPublishVpkVersionInForm(draft.VpkVersion);
			SetChecked("WorkshopPublishVpkMd5CheckBox", draft.VpkIncludeMd5Sections);
			SetChecked("WorkshopPublishVpkMultiFileCheckBox", draft.VpkMultiFile);
			GetRequiredControl<TextBox>("WorkshopPublishPreviewTextBox").Text = draft.PreviewFile ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishTitleTextBox").Text = draft.Title ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishDescriptionTextBox").Text = draft.Description ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishChangeNoteTextBox").Text = draft.ChangeNote ?? string.Empty;
			GetRequiredControl<TextBox>("WorkshopPublishTagsTextBox").Text = draft.Tags ?? string.Empty;
			SetComboBoxSelectionByText("WorkshopPublishGmodTypeComboBox", draft.ContentType, defaultIndex: 0);
			SetWorkshopPublishGmodSelectedTags(draft.ContentTags);
			GetRequiredControl<TextBox>("WorkshopPublishVdfTextBox").Text = draft.VdfPath ?? string.Empty;
		}
			finally
			{
				isWorkshopPublishDraftFormLoading = false;
			}

			UpdateWorkshopPublishTagPresetsUiVisibility();
			UpdateWorkshopPublishVpkOptionsUiVisibility();
		}

	private async void OnWorkshopPublishClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync((progress, cancellationToken) => RunWorkshopPublishAsync(useSteamworks: false, progress, cancellationToken));
	}

	private async void OnWorkshopPublishViaSteamworksClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync((progress, cancellationToken) => RunWorkshopPublishAsync(useSteamworks: true, progress, cancellationToken));
	}

	private async Task RunWorkshopPublishAsync(bool useSteamworks, IProgress<StunstickProgress> progress, CancellationToken cancellationToken)
	{
		progress.Report(new StunstickProgress("Workshop Publish", 0, 0, Message: "Starting..."));

		var appIdText = GetText("WorkshopPublishAppIdTextBox");
		if (string.IsNullOrWhiteSpace(appIdText) || !uint.TryParse(appIdText, out var appId) || appId == 0)
		{
			throw new InvalidDataException("AppID must be a positive integer.");
		}

		var steamCmdUser = GetText("WorkshopPublishSteamCmdUserTextBox");
		if (!useSteamworks)
		{
			RequireNonEmpty(steamCmdUser, "Steam user");
		}
		else if (string.IsNullOrWhiteSpace(steamCmdUser))
		{
			steamCmdUser = null;
		}

		var publishedIdText = GetText("WorkshopPublishPublishedIdTextBox");
		ulong publishedFileId = 0;
		if (!string.IsNullOrWhiteSpace(publishedIdText) && !ulong.TryParse(publishedIdText, out publishedFileId))
		{
			throw new InvalidDataException("Published ID must be a positive integer (or blank for new).");
		}

		var visibility = GetComboBoxIndex("WorkshopPublishVisibilityComboBox") switch
		{
			0 => WorkshopPublishVisibility.Public,
			1 => WorkshopPublishVisibility.FriendsOnly,
			2 => WorkshopPublishVisibility.Private,
			3 => WorkshopPublishVisibility.Unlisted,
			_ => WorkshopPublishVisibility.Public
		};

		var contentFolder = GetText("WorkshopPublishContentTextBox");
		RequireFileOrDirectoryExists(contentFolder, "Content");

		var previewFile = GetText("WorkshopPublishPreviewTextBox");
		RequireFileExists(previewFile, "Preview file");

		var title = GetText("WorkshopPublishTitleTextBox");
		RequireNonEmpty(title, "Title");

		var description = GetText("WorkshopPublishDescriptionTextBox");
		RequireNonEmpty(description, "Description");

		var changeNote = GetText("WorkshopPublishChangeNoteTextBox");
		RequireNonEmpty(changeNote, "Change note");

		IReadOnlyList<string>? tags = null;
		var tagsText = GetText("WorkshopPublishTagsTextBox");
		if (!string.IsNullOrWhiteSpace(tagsText))
		{
			tags = tagsText
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(tag => !string.IsNullOrWhiteSpace(tag))
				.ToArray();
		}

		string? contentType = null;
		IReadOnlyList<string>? contentTags = null;
		if (appId == 4000)
		{
			contentType = GetComboBoxText("WorkshopPublishGmodTypeComboBox");

			var selected = GetWorkshopPublishGmodSelectedTags();
			contentTags = selected.Count == 0 ? null : selected;

			if ((tags is null || tags.Count == 0) && !string.IsNullOrWhiteSpace(contentType))
			{
				var derived = new List<string>(capacity: 3) { contentType! };
				if (contentTags is not null)
				{
					derived.AddRange(contentTags);
				}

				tags = derived
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray();
			}
		}

		var steamCmdPath = GetText("WorkshopPublishSteamCmdTextBox");
		if (string.IsNullOrWhiteSpace(steamCmdPath))
		{
			steamCmdPath = null;
		}

		var vdfPath = GetText("WorkshopPublishVdfTextBox");
		if (string.IsNullOrWhiteSpace(vdfPath))
		{
			vdfPath = null;
		}

		var result = await app.PublishWorkshopItemAsync(
			new WorkshopPublishRequest(
				AppId: appId,
				ContentFolder: contentFolder,
				PreviewFile: previewFile,
				Title: title,
				Description: description,
				ChangeNote: changeNote,
				PublishedFileId: publishedFileId,
				Visibility: visibility,
				VdfPath: vdfPath,
				SteamCmdPath: steamCmdPath,
				SteamCmdUsername: steamCmdUser,
				Tags: tags,
				ContentType: contentType,
				ContentTags: contentTags,
				Progress: progress,
				Output: new Progress<string>(AppendLog),
				SteamCmdPromptAsync: PromptSteamCmdAsync,
				UseSteamworks: useSteamworks,
				PackToVpkBeforeUpload: GetChecked("WorkshopPublishPackVpkCheckBox"),
				StageCleanPayload: GetChecked("WorkshopPublishStageCleanCheckBox"),
				VpkVersion: GetWorkshopPublishVpkVersionFromForm(),
				VpkIncludeMd5Sections: GetWorkshopPublishVpkIncludeMd5SectionsFromForm(),
				VpkMultiFile: GetChecked("WorkshopPublishVpkMultiFileCheckBox")),
			cancellationToken);

		lastWorkshopPublishPublishedFileId = result.PublishedFileId;
		GetRequiredControl<TextBox>("WorkshopPublishPublishedIdTextBox").Text = result.PublishedFileId.ToString();

		var methodText = useSteamworks ? "Steamworks" : "SteamCMD";
		AppendLog($"Workshop: published {result.PublishedFileId} (AppID {result.AppId}) via {methodText}");
		if (!string.IsNullOrWhiteSpace(result.VdfPath))
		{
			AppendLog($"Workshop: VDF {result.VdfPath}");
		}

		if (GetChecked("WorkshopPublishOpenPageCheckBox") && result.PublishedFileId != 0)
		{
			TryOpenUri($"https://steamcommunity.com/sharedfiles/filedetails/?id={result.PublishedFileId}");
		}
	}

	private async void OnWorkshopPublishDeleteViaSteamworksClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			if (string.IsNullOrWhiteSpace(appIdText) || !uint.TryParse(appIdText, out var appId) || appId == 0)
			{
				AppendLog("Workshop: AppID must be a positive integer.");
				return;
			}

			ulong publishedFileId = 0;
			var text = GetText("WorkshopPublishPublishedIdTextBox");
			if (!string.IsNullOrWhiteSpace(text) && ulong.TryParse(text, out var parsed) && parsed != 0)
			{
				publishedFileId = parsed;
			}
			else if (lastWorkshopPublishPublishedFileId is ulong lastId && lastId != 0)
			{
				publishedFileId = lastId;
			}

			if (publishedFileId == 0)
			{
				AppendLog("Workshop: enter/publish an item ID first.");
				return;
			}

			var confirm = new ConfirmWindow($"Delete PublishedFileId {publishedFileId}? This cannot be undone.");
			var ok = await confirm.ShowDialog<bool>(this);
			if (!ok)
			{
				return;
			}

			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				await app.DeleteWorkshopItemAsync(
					new WorkshopDeleteRequest(
						AppId: appId,
						PublishedFileId: publishedFileId,
						Progress: progress,
						Output: new Progress<string>(AppendLog)),
					cancellationToken);

				AppendLog($"Workshop: deleted {publishedFileId} (AppID {appId}) via Steamworks");
			});
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnWorkshopPublishRefreshMyItemsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			uint appId = 4000;
			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			if (!string.IsNullOrWhiteSpace(appIdText) && (!uint.TryParse(appIdText, out appId) || appId == 0))
			{
				throw new InvalidDataException("AppID must be a positive integer.");
			}

			var result = await app.ListWorkshopPublishedItemsAsync(
				new WorkshopListRequest(
					AppId: appId,
					Page: 1,
					Progress: progress,
					Output: new Progress<string>(AppendLog)),
				cancellationToken);

			workshopPublishMyItems.Clear();
			workshopPublishMyItems.AddRange(result.Items.Select(item => new WorkshopPublishedItemListItem(item)));

			RefreshWorkshopPublishMyItemsList();
			AppendLog($"Workshop: listed {result.Items.Count} published item(s) (AppID {appId}) via Steamworks");
		});
	}

	private async void OnWorkshopPublishQuotaClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			uint appId = 4000;
			var appIdText = GetText("WorkshopPublishAppIdTextBox");
			if (!string.IsNullOrWhiteSpace(appIdText) && (!uint.TryParse(appIdText, out appId) || appId == 0))
			{
				throw new InvalidDataException("AppID must be a positive integer.");
			}

			var result = await app.GetWorkshopQuotaAsync(
				new WorkshopQuotaRequest(
					AppId: appId,
					Progress: progress,
					Output: new Progress<string>(AppendLog)),
				cancellationToken);

			var quotaText = GetRequiredControl<TextBlock>("WorkshopPublishQuotaTextBlock");
			quotaText.Text = $"Quota: {FormatBytes(ClampToInt64(result.UsedBytes))} used of {FormatBytes(ClampToInt64(result.TotalBytes))} (available {FormatBytes(ClampToInt64(result.AvailableBytes))})";

			AppendLog($"Workshop: quota read for AppID {appId} via Steamworks");
		});
	}

	private void OnWorkshopPublishMyItemsSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		try
		{
			var list = GetRequiredControl<ListBox>("WorkshopPublishMyItemsListBox");
			if (list.SelectedItem is not WorkshopPublishedItemListItem item)
			{
				return;
			}

			GetRequiredControl<TextBox>("WorkshopPublishPublishedIdTextBox").Text = item.Item.PublishedFileId.ToString();
			lastWorkshopPublishPublishedFileId = item.Item.PublishedFileId;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

		private void RefreshWorkshopPublishMyItemsList()
		{
			var list = GetRequiredControl<ListBox>("WorkshopPublishMyItemsListBox");
			var query = GetRequiredControl<TextBox>("WorkshopPublishMyItemsFilterTextBox").Text?.Trim();
			if (string.IsNullOrWhiteSpace(query))
			{
				list.ItemsSource = workshopPublishMyItems.ToArray();
				return;
			}

			list.ItemsSource = workshopPublishMyItems
				.Where(item => WorkshopPublishedItemMatchesFilter(item.Item, query))
				.ToArray();
		}

		private void OnWorkshopPublishMyItemsFilterTextChanged(object? sender, TextChangedEventArgs e)
		{
			try
			{
				RefreshWorkshopPublishMyItemsList();
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private static bool WorkshopPublishedItemMatchesFilter(WorkshopPublishedItem item, string query)
		{
			if (item.PublishedFileId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!string.IsNullOrWhiteSpace(item.Title) && item.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (!string.IsNullOrWhiteSpace(item.Description) && item.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			foreach (var tag in item.Tags)
			{
				if (tag.Contains(query, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private void OnWorkshopPublishUseInDownloadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var list = GetRequiredControl<ListBox>("WorkshopPublishMyItemsListBox");
				ulong publishedFileId = 0;

				if (list.SelectedItem is WorkshopPublishedItemListItem item)
				{
					publishedFileId = item.Item.PublishedFileId;
				}
				else if (lastWorkshopPublishPublishedFileId is ulong lastId && lastId != 0)
				{
					publishedFileId = lastId;
				}

				if (publishedFileId == 0)
				{
					AppendLog("Workshop: select a published item first.");
					return;
				}

				GetRequiredControl<TextBox>("WorkshopDownloadIdTextBox").Text = publishedFileId.ToString();

				var appIdText = GetText("WorkshopPublishAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(appIdText))
				{
					GetRequiredControl<TextBox>("WorkshopDownloadAppIdTextBox").Text = appIdText;
				}

				SelectMainTab("WorkshopTabItem");
				GetRequiredControl<TabControl>("WorkshopTabControl").SelectedIndex = 0;
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnWorkshopPublishOpenPageClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
			ulong publishedFileId = 0;
			var text = GetText("WorkshopPublishPublishedIdTextBox");
			if (!string.IsNullOrWhiteSpace(text) && ulong.TryParse(text, out var parsed) && parsed != 0)
			{
				publishedFileId = parsed;
			}
			else if (lastWorkshopPublishPublishedFileId is ulong lastId && lastId != 0)
			{
				publishedFileId = lastId;
			}

			if (publishedFileId == 0)
			{
				AppendLog("Workshop: enter/publish an item ID first.");
				return;
			}

			TryOpenUri($"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}");
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnUnpackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Starting..."));

			var input = GetText("UnpackInputTextBox");
			RequireFileExists(input, "Package");
			var outputBase = GetText("UnpackOutputTextBox");
			RequireNonEmpty(outputBase, "Output folder");
			var output = GetUnpackOutputDirectory(input, outputBase);
			var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
			var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
			var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
			var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
			if (writeLogFile)
			{
				AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
			}

			await app.UnpackAsync(
				new UnpackRequest(input, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, WriteLogFile: writeLogFile),
				cancellationToken);
			AppendLog($"Unpacked to: {output}");

			if (GetChecked("UnpackOpenOutputCheckBox"))
			{
				TryOpenFolder(output);
			}
		});
	}

	private async void OnUnpackToTempClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Starting (temp)..."));

			var input = GetText("UnpackInputTextBox");
			RequireFileExists(input, "Package");

			var output = CreateTempOutputDirectoryFor(input);
			GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = output;

			var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
			var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
			var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
			var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
			if (writeLogFile)
			{
				AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
			}

			await app.UnpackAsync(
				new UnpackRequest(input, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, WriteLogFile: writeLogFile),
				cancellationToken);

			AppendLog($"Unpacked to: {output}");
			TryOpenFolder(output);
		});
	}

	private void OnUnpackOutputSameFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var packagePath = GetText("UnpackInputTextBox");
			if (string.IsNullOrWhiteSpace(packagePath))
			{
				AppendLog("Explore: enter a package path first.");
				return;
			}

			var fullPath = Path.GetFullPath(packagePath);
			var packageDirectory = Path.GetDirectoryName(fullPath);
			if (string.IsNullOrWhiteSpace(packageDirectory))
			{
				AppendLog("Explore: could not determine package folder.");
				return;
			}

			GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = packageDirectory;
			SetChecked("UnpackFolderPerPackageCheckBox", false);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

		private void OnUnpackOutputSubfolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
			var packagePath = GetText("UnpackInputTextBox");
			if (string.IsNullOrWhiteSpace(packagePath))
			{
				AppendLog("Explore: enter a package path first.");
				return;
			}

			var fullPath = Path.GetFullPath(packagePath);
			var packageDirectory = Path.GetDirectoryName(fullPath);
			if (string.IsNullOrWhiteSpace(packageDirectory))
			{
				AppendLog("Explore: could not determine package folder.");
				return;
			}

			GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = packageDirectory;
			SetChecked("UnpackFolderPerPackageCheckBox", true);
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnUnpackOutputWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var folder = Path.Combine(GetWorkFolder(), "Unpack");
				GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = folder;
				SetChecked("UnpackFolderPerPackageCheckBox", true);
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnUnpackOutputGameAddonsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var gameDir = GetComboBoxText("ToolchainGameDirComboBox");
				if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
				{
					AppendLog("Explore: select a game and Game Dir first (Games tab).");
					return;
				}

				var folder = GetGameAddonsFolder(gameDir, toolchainSelectedAppId);
				GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = folder;
				SetChecked("UnpackFolderPerPackageCheckBox", true);
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private static string GetGameAddonsFolder(string gameDirectory, uint? appId)
		{
			var fullGameDir = Path.GetFullPath(gameDirectory);

			if (appId == 4000)
			{
				return Path.Combine(fullGameDir, "addons");
			}

			var custom = Path.Combine(fullGameDir, "custom");
			if (Directory.Exists(custom))
			{
				return custom;
			}

			var addons = Path.Combine(fullGameDir, "addons");
			if (Directory.Exists(addons))
			{
				return addons;
			}

			var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullGameDir));
			if (string.Equals(folderName, "garrysmod", StringComparison.OrdinalIgnoreCase))
			{
				return Path.Combine(fullGameDir, "addons");
			}

			return custom;
		}

		private string GetUnpackOutputDirectory(string packagePath, string outputBaseDirectory)
		{
			if (!GetChecked("UnpackFolderPerPackageCheckBox"))
			{
			return outputBaseDirectory;
		}

		var folderName = GetUnpackPackageFolderName(packagePath);
		return Path.Combine(outputBaseDirectory, folderName);
	}

	private static string GetUnpackPackageFolderName(string packagePath)
	{
		var baseName = Path.GetFileNameWithoutExtension(packagePath);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return "package";
		}

		if (baseName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
		{
			baseName = baseName[..^"_dir".Length];
		}
		else if (baseName.EndsWith("_fdr", StringComparison.OrdinalIgnoreCase))
		{
			baseName = baseName[..^"_fdr".Length];
		}
		else
		{
			var lastUnderscore = baseName.LastIndexOf('_');
			if (lastUnderscore >= 0 && lastUnderscore + 4 == baseName.Length)
			{
				var suffix = baseName[(lastUnderscore + 1)..];
				if (suffix.Length == 3 && suffix.All(char.IsDigit))
				{
					baseName = baseName[..lastUnderscore];
				}
			}
		}

		return string.IsNullOrWhiteSpace(baseName) ? "package" : baseName;
	}

	private async void OnUnpackListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("List", 0, 0, Message: "Reading package..."));

			var input = GetText("UnpackInputTextBox");
			RequireFileExists(input, "Package");

			var entries = await app.ListPackageEntriesAsync(input, cancellationToken);
			unpackEntries = entries;
			UpdateUnpackEntriesList();
			AppendLog($"Listed {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}.");
		});
	}

	private void OnUnpackClearListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		unpackEntries = null;
		unpackFilteredEntries = null;
		GetRequiredControl<ListBox>("UnpackEntriesListBox").ItemsSource = Array.Empty<PackageEntryListItem>();
		GetRequiredControl<TextBlock>("UnpackEntriesCountTextBlock").Text = "0 entries";
		GetRequiredControl<TextBlock>("UnpackSelectedEntryTextBlock").Text = string.Empty;
		GetRequiredControl<TreeView>("UnpackTreeView").ItemsSource = Array.Empty<PackageBrowserNode>();
	}

		private void OnUnpackSavedSearchSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			if (isLoadingSettings || isUnpackSavedSearchesSyncing)
			{
				return;
			}

			try
			{
				var combo = GetRequiredControl<ComboBox>("UnpackSavedSearchesComboBox");
				if (combo.SelectedItem is not string query || string.IsNullOrWhiteSpace(query))
				{
					return;
				}

				SetText("UnpackSearchTextBox", query);
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnUnpackSaveSearchClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var query = GetText("UnpackSearchTextBox").Trim();
				if (string.IsNullOrWhiteSpace(query))
				{
					AppendLog("Unpack: enter a search string to save.");
					return;
				}

				if (unpackSavedSearches.Any(s => string.Equals(s, query, StringComparison.OrdinalIgnoreCase)))
				{
					AppendLog("Unpack: saved search already exists.");
					return;
				}

				unpackSavedSearches.Add(query);
				UpdateUnpackSavedSearchesComboBox();
				GetRequiredControl<ComboBox>("UnpackSavedSearchesComboBox").SelectedItem = query;
				SaveSettings();
				AppendLog($"Unpack: saved search: {query}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnUnpackRemoveSearchClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var combo = GetRequiredControl<ComboBox>("UnpackSavedSearchesComboBox");
				if (combo.SelectedItem is not string query || string.IsNullOrWhiteSpace(query))
				{
					AppendLog("Unpack: select a saved search to remove.");
					return;
				}

				unpackSavedSearches.RemoveAll(s => string.Equals(s, query, StringComparison.OrdinalIgnoreCase));
				UpdateUnpackSavedSearchesComboBox();
				SaveSettings();
				AppendLog($"Unpack: removed saved search: {query}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnUnpackSearchTextChanged(object? sender, TextChangedEventArgs e)
		{
			UpdateUnpackEntriesList();
		}

		private void OnUnpackSizeUnitsChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			UpdateUnpackEntriesList();
		}

	private void OnUnpackListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		var list = GetRequiredControl<ListBox>("UnpackEntriesListBox");
		var selectedText = GetRequiredControl<TextBlock>("UnpackSelectedEntryTextBlock");

		var selectedItems = list.SelectedItems?.OfType<PackageEntryListItem>().ToArray() ?? Array.Empty<PackageEntryListItem>();
		if (selectedItems.Length == 0)
		{
			selectedText.Text = string.Empty;
			return;
		}

			if (selectedItems.Length == 1)
			{
				var entry = selectedItems[0].Entry;
				var crcText = entry.Crc32 is null ? string.Empty : $" CRC32=0x{entry.Crc32.Value:x8}";
				selectedText.Text = $"{entry.RelativePath} ({FormatUnpackSizeText(entry.SizeBytes)}){crcText}";
				return;
			}

			var totalBytes = selectedItems.Sum(item => item.Entry.SizeBytes);
			selectedText.Text = $"{selectedItems.Length} items selected ({FormatUnpackSizeText(totalBytes)})";
		}

	private async void OnUnpackExtractSelectedClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Extracting selection..."));

			var packagePath = GetText("UnpackInputTextBox");
			RequireFileExists(packagePath, "Package");

			var outputBase = GetText("UnpackOutputTextBox");
			RequireNonEmpty(outputBase, "Output folder");
			var output = GetUnpackOutputDirectory(packagePath, outputBase);

			var selectedPaths = GetSelectedUnpackRelativePaths();
			if (selectedPaths.Count == 0)
			{
				throw new InvalidDataException("Select a file or folder to extract.");
			}

			var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
			var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
			var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
			var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
			if (writeLogFile)
			{
				AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
			}

			await app.UnpackAsync(
				new UnpackRequest(packagePath, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, OnlyPaths: selectedPaths, WriteLogFile: writeLogFile),
				cancellationToken);

			AppendLog($"Extracted {selectedPaths.Count} item(s) to: {output}");

			if (GetChecked("UnpackOpenOutputCheckBox"))
			{
				TryOpenFolder(output);
			}
		});
	}

		private async void OnUnpackExtractSelectedToTempClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
			progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Extracting selection (temp)..."));

			var packagePath = GetText("UnpackInputTextBox");
			RequireFileExists(packagePath, "Package");

			var selectedPaths = GetSelectedUnpackRelativePaths();
			if (selectedPaths.Count == 0)
			{
				throw new InvalidDataException("Select a file or folder to extract.");
			}

			var output = CreateTempOutputDirectoryFor(packagePath);
			GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = output;

			var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
			var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
			var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
			var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
			if (writeLogFile)
			{
				AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
			}

			await app.UnpackAsync(
				new UnpackRequest(packagePath, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, OnlyPaths: selectedPaths, WriteLogFile: writeLogFile),
				cancellationToken);

			AppendLog($"Extracted {selectedPaths.Count} item(s) to: {output}");

			if (selectedPaths.Count == 1)
			{
				var candidate = MapExtractedPath(output, selectedPaths[0], keepFullPath);
				if (File.Exists(candidate))
				{
					TryOpenFile(candidate);
					return;
				}

				var folderCandidate = Path.GetDirectoryName(candidate);
				if (!string.IsNullOrWhiteSpace(folderCandidate) && Directory.Exists(folderCandidate))
				{
					TryOpenFolder(folderCandidate);
					return;
				}
			}

				TryOpenFolder(output);
			});
		}

		private async void OnUnpackUseSelectedInDecompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Extracting selection (temp)..."));

				var packagePath = GetText("UnpackInputTextBox");
				RequireFileExists(packagePath, "Package");

				var selectedPaths = GetSelectedUnpackRelativePaths();
				if (selectedPaths.Count == 0)
				{
					throw new InvalidDataException("Select a file or folder to use in Decompile.");
				}

				var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				var comparer = comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

				var list = GetRequiredControl<ListBox>("UnpackEntriesListBox");
				var listSelected = list.SelectedItems?.OfType<PackageEntryListItem>().ToArray() ?? Array.Empty<PackageEntryListItem>();

				var tree = GetRequiredControl<TreeView>("UnpackTreeView");
				var treeNode = listSelected.Length > 0 ? null : tree.SelectedItem as PackageBrowserNode;
				var selectionIsDirectory = treeNode?.IsDirectory == true;

				var selectedNormalized = selectedPaths.Select(p => p.Replace('\\', '/')).ToArray();
				var selectedMdls = selectedNormalized
					.Where(p => p.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray();
				if (selectedMdls.Length == 0)
				{
					throw new InvalidDataException("Selection must include at least one MDL file (.mdl).");
				}

				var onlyPaths = new HashSet<string>(comparer);
				for (var i = 0; i < selectedNormalized.Length; i++)
				{
					var path = selectedNormalized[i]?.Trim();
					if (!string.IsNullOrWhiteSpace(path))
					{
						onlyPaths.Add(path);
					}
				}

				if (!selectionIsDirectory)
				{
					var allPaths = (unpackEntries ?? unpackFilteredEntries ?? Array.Empty<PackageEntry>())
						.Select(entry => entry.RelativePath.Replace('\\', '/'))
						.ToArray();

					for (var i = 0; i < selectedMdls.Length; i++)
					{
						var mdl = selectedMdls[i];
						var basePath = mdl[..^4];

						for (var j = 0; j < allPaths.Length; j++)
						{
							var candidate = allPaths[j];
							if (candidate.StartsWith(basePath, comparison) && candidate.Length > basePath.Length && candidate[basePath.Length] == '.')
							{
								onlyPaths.Add(candidate);
							}
						}
					}
				}

				var output = CreateTempOutputDirectoryFor(packagePath);
				GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = output;

				var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
				var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
				var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
				var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
				if (writeLogFile)
				{
					AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
				}

				await app.UnpackAsync(
					new UnpackRequest(packagePath, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, OnlyPaths: onlyPaths.OrderBy(p => p, comparer).ToArray(), WriteLogFile: writeLogFile),
					cancellationToken);

				var decompileInput = output;
				if (selectionIsDirectory)
				{
					SetChecked("DecompileIncludeSubfoldersCheckBox", true);

					if (keepFullPath && treeNode is not null && !string.IsNullOrWhiteSpace(treeNode.RelativePath))
					{
						var dirCandidate = Path.GetFullPath(Path.Combine(output, treeNode.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
						if (Directory.Exists(dirCandidate))
						{
							decompileInput = dirCandidate;
						}
					}
				}
				else if (selectedMdls.Length == 1)
				{
					var extractedMdlPath = MapExtractedPath(output, selectedMdls[0], keepFullPath);
					if (!File.Exists(extractedMdlPath))
					{
						throw new FileNotFoundException("Extracted MDL file not found.", extractedMdlPath);
					}

					decompileInput = extractedMdlPath;
				}
				else
				{
					SetChecked("DecompileIncludeSubfoldersCheckBox", true);
				}

				GetRequiredControl<TextBox>("DecompileMdlTextBox").Text = decompileInput;
					if (string.IsNullOrWhiteSpace(GetText("DecompileOutputTextBox")))
					{
						GetRequiredControl<TextBox>("DecompileOutputTextBox").Text = output;
					}

					SelectMainTab("DecompileTabItem");
					AppendLog($"Unpack: routed to Decompile: {decompileInput}");
			});
		}

		private async void OnUnpackUseSelectedInViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				progress.Report(new StunstickProgress("Unpack", 0, 0, Message: "Extracting model (temp)..."));

				var packagePath = GetText("UnpackInputTextBox");
				RequireFileExists(packagePath, "Package");

				var selectedPaths = GetSelectedUnpackRelativePaths();
				if (selectedPaths.Count != 1)
				{
					throw new InvalidDataException("Select exactly one file to use in View.");
				}

				var selected = selectedPaths[0];
				if (!selected.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("Selected item must be an MDL file (.mdl).");
				}

				var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				var selectedNormalized = selected.Replace('\\', '/');
				var basePath = selectedNormalized[..^4];
				var companionPaths = (unpackEntries ?? unpackFilteredEntries ?? Array.Empty<PackageEntry>())
					.Select(entry => entry.RelativePath)
					.Where(path => path.StartsWith(basePath, comparison) && path.Length > basePath.Length && path[basePath.Length] == '.')
					.Distinct(comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
					.ToArray();

				if (companionPaths.Length == 0)
				{
					companionPaths = new[] { selectedNormalized };
				}

				var output = CreateTempOutputDirectoryFor(packagePath);
				GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = output;

				var verifyCrc = GetChecked("UnpackVerifyCrcCheckBox");
				var verifyMd5 = GetChecked("UnpackVerifyMd5CheckBox");
				var keepFullPath = GetChecked("UnpackKeepFullPathCheckBox");
				var writeLogFile = GetChecked("UnpackWriteLogFileCheckBox");
				if (writeLogFile)
				{
					AppendLog($"Unpack: Writing log: {Path.Combine(Path.GetFullPath(output), "unpack.log")}");
				}

				await app.UnpackAsync(
					new UnpackRequest(packagePath, output, VerifyCrc32: verifyCrc, VerifyMd5: verifyMd5, KeepFullPath: keepFullPath, Progress: progress, OnlyPaths: companionPaths, WriteLogFile: writeLogFile),
					cancellationToken);

				var extractedMdlPath = MapExtractedPath(output, selectedNormalized, keepFullPath);
				if (!File.Exists(extractedMdlPath))
				{
					throw new FileNotFoundException("Extracted MDL file not found.", extractedMdlPath);
				}

				GetRequiredControl<TextBox>("ViewMdlTextBox").Text = extractedMdlPath;
				SelectMainTab("ViewTabItem");

				progress.Report(new StunstickProgress("Preview", 0, 0, CurrentItem: Path.GetFileName(extractedMdlPath), Message: "Loading MDL/VTX/VVD/PHY..."));
				var preview = await MdlPreviewLoader.LoadAsync(extractedMdlPath, lodIndex: 0, includePhysics: true, cancellationToken);

				await Dispatcher.UIThread.InvokeAsync(() =>
				{
					ApplyViewPreview(preview, resetCamera: true);
					ScheduleViewDataViewerUpdate(delayMs: 0);
				});

				var meshTriCount = preview.MeshGeometry?.Triangles.Count ?? 0;
				var phyTriCount = preview.PhysicsGeometry?.Triangles.Count ?? 0;
				AppendLog($"Unpack: routed to View: {extractedMdlPath} (model: {meshTriCount:N0} tris, physics: {phyTriCount:N0} tris)");
			});
		}

	private void OnUnpackTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		var tree = GetRequiredControl<TreeView>("UnpackTreeView");
		var selectedText = GetRequiredControl<TextBlock>("UnpackSelectedEntryTextBlock");

		if (tree.SelectedItem is not PackageBrowserNode node)
		{
			selectedText.Text = string.Empty;
			return;
		}

		if (node.IsDirectory)
		{
			selectedText.Text = string.IsNullOrWhiteSpace(node.RelativePath) ? node.Name : node.RelativePath;
			return;
			}

			var crcText = node.Crc32 is null ? string.Empty : $" CRC32=0x{node.Crc32.Value:x8}";
			selectedText.Text = $"{node.RelativePath} ({FormatUnpackSizeText(node.SizeBytes)}){crcText}";
		}

	private enum PackInputMode
	{
		SingleFolder = 0,
		ParentOfChildFolders = 1
	}

	private enum PackBatchOutputType
	{
		Vpk = 0,
		Fpx = 1,
		Gma = 2
	}

	private PackInputMode GetPackInputMode()
	{
		return GetComboBoxIndex("PackInputModeComboBox") switch
		{
			1 => PackInputMode.ParentOfChildFolders,
			_ => PackInputMode.SingleFolder
		};
	}

	private PackBatchOutputType GetPackBatchOutputType()
	{
		return GetComboBoxIndex("PackBatchOutputTypeComboBox") switch
		{
			1 => PackBatchOutputType.Fpx,
			2 => PackBatchOutputType.Gma,
			_ => PackBatchOutputType.Vpk
		};
	}

	private void OnPackInputModeChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		UpdatePackUiForMode(convertOutput: true);
	}

	private void OnPackBatchOutputTypeChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		UpdatePackUiForMode(convertOutput: false);
	}

	private void OnPackOutputTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		UpdatePackUiForMode(convertOutput: false);
	}

	private void OnPackOutputParentFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var input = GetText("PackInputTextBox");
			if (string.IsNullOrWhiteSpace(input))
			{
				AppendLog("Pack: enter an input folder first.");
				return;
			}

			var inputFullPath = Path.GetFullPath(input);
			var mode = GetPackInputMode();
			var multiFile = GetChecked("PackMultiFileCheckBox");

			if (mode == PackInputMode.ParentOfChildFolders)
			{
				GetRequiredControl<TextBox>("PackOutputTextBox").Text = inputFullPath;
				return;
			}

			var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(inputFullPath));
			if (string.IsNullOrWhiteSpace(parent))
			{
				parent = ".";
			}

			var outputPath = GetPackPresetOutputFilePath(inputFullPath, parent, multiFile);
			GetRequiredControl<TextBox>("PackOutputTextBox").Text = outputPath;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnPackOutputSameFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var input = GetText("PackInputTextBox");
			if (string.IsNullOrWhiteSpace(input))
			{
				AppendLog("Pack: enter an input folder first.");
				return;
			}

			var inputFullPath = Path.GetFullPath(input);
			var mode = GetPackInputMode();
			if (mode == PackInputMode.ParentOfChildFolders)
			{
				AppendLog("Pack: Same folder preset is only for single-folder mode.");
				return;
			}

			var multiFile = GetChecked("PackMultiFileCheckBox");
			var outputPath = GetPackPresetOutputFilePath(inputFullPath, inputFullPath, multiFile);
			GetRequiredControl<TextBox>("PackOutputTextBox").Text = outputPath;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnPackOutputWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var input = GetText("PackInputTextBox");
			if (string.IsNullOrWhiteSpace(input))
			{
				AppendLog("Pack: enter an input folder first.");
				return;
			}

			var inputFullPath = Path.GetFullPath(input);
			var mode = GetPackInputMode();
			var multiFile = GetChecked("PackMultiFileCheckBox");

			var workFolder = Path.Combine(GetWorkFolder(), "Pack");
			if (mode == PackInputMode.ParentOfChildFolders)
			{
				GetRequiredControl<TextBox>("PackOutputTextBox").Text = workFolder;
				return;
			}

			var outputPath = GetPackPresetOutputFilePath(inputFullPath, workFolder, multiFile);
			GetRequiredControl<TextBox>("PackOutputTextBox").Text = outputPath;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private string GetPackPresetOutputFilePath(string inputDirectory, string outputDirectory, bool multiFile)
	{
		var outputText = GetText("PackOutputTextBox");
		var extension = ".vpk";
		try
		{
			var ext = Path.GetExtension(outputText).Trim();
			if (string.Equals(ext, ".vpk", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(ext, ".fpx", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(ext, ".gma", StringComparison.OrdinalIgnoreCase))
			{
				extension = ext.ToLowerInvariant();
			}
		}
		catch
		{
		}

		var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory));
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "package";
		}

		if (extension == ".gma")
		{
			return Path.Combine(outputDirectory, $"{name}.gma");
		}

		if (multiFile)
		{
			var dirSuffix = extension == ".fpx" ? "_fdr" : "_dir";
			return Path.Combine(outputDirectory, $"{name}{dirSuffix}{extension}");
		}

		return Path.Combine(outputDirectory, $"{name}{extension}");
	}

	private void OnPackSkipCurrentFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			packSkipCurrentFolderCancellation?.Cancel();
		}
		catch
		{
		}
	}

	private void UpdatePackUiForMode(bool convertOutput)
	{
		var mode = GetPackInputMode();
		var isBatch = mode == PackInputMode.ParentOfChildFolders;

		var inputBox = GetRequiredControl<TextBox>("PackInputTextBox");
		var outputLabel = GetRequiredControl<TextBlock>("PackOutputLabelTextBlock");
		var outputBox = GetRequiredControl<TextBox>("PackOutputTextBox");
		var outputBrowseButton = GetRequiredControl<Button>("PackOutputBrowseButton");
		var batchTypeLabel = GetRequiredControl<TextBlock>("PackBatchOutputTypeLabelTextBlock");
		var batchTypeComboBox = GetRequiredControl<ComboBox>("PackBatchOutputTypeComboBox");
		var skipButton = GetRequiredControl<Button>("PackSkipCurrentFolderButton");

		batchTypeLabel.IsVisible = isBatch;
		batchTypeComboBox.IsVisible = isBatch;
		skipButton.IsVisible = isBatch;
		skipButton.IsEnabled = false;

		if (isBatch)
		{
			inputBox.Watermark = "parent folder";
			outputLabel.Text = "Output folder";
			outputBox.Watermark = "output folder";
			outputBrowseButton.Content = "Browse folder";
			outputBox.Tag = "folder";

			if (convertOutput)
			{
				try
				{
					var output = outputBox.Text?.Trim();
					if (!string.IsNullOrWhiteSpace(output) && Path.HasExtension(output))
					{
						var outputDir = Path.GetDirectoryName(output);
						if (!string.IsNullOrWhiteSpace(outputDir))
						{
							outputBox.Text = outputDir;
						}
					}
				}
				catch
				{
				}
			}
		}
		else
		{
			inputBox.Watermark = "folder";
			outputLabel.Text = "Output file";
			outputBox.Watermark = "path/to/pak01_dir.vpk";
			outputBrowseButton.Content = "Browse file";
			outputBox.Tag = "file";

			if (convertOutput)
			{
				try
				{
					var output = outputBox.Text?.Trim();
					if (!string.IsNullOrWhiteSpace(output) && !Path.HasExtension(output) && (Directory.Exists(output) || output.EndsWith(Path.DirectorySeparatorChar)))
					{
						outputBox.Text = Path.Combine(output, "pak01_dir.vpk");
					}
				}
				catch
				{
				}
			}
		}

		var isGmaOutput = isBatch && GetPackBatchOutputType() == PackBatchOutputType.Gma;
		if (!isBatch)
		{
			try
			{
				isGmaOutput = string.Equals(
					Path.GetExtension(GetText("PackOutputTextBox")).Trim(),
					".gma",
					StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				isGmaOutput = false;
			}
		}

		GetRequiredControl<StackPanel>("PackVpkOptionsPanel").IsEnabled = !isGmaOutput;
		GetRequiredControl<TextBox>("PackSplitMbTextBox").IsEnabled = !isGmaOutput;
		GetRequiredControl<TextBox>("PackPreloadBytesTextBox").IsEnabled = !isGmaOutput;
		GetRequiredControl<ComboBox>("PackVpkVersionComboBox").IsEnabled = !isGmaOutput;
		GetRequiredControl<TextBlock>("PackGmaAddonJsonLabelTextBlock").IsVisible = isGmaOutput;
		GetRequiredControl<StackPanel>("PackGmaAddonJsonPanel").IsVisible = isGmaOutput;
	}

	private static string GetPackBatchExtension(PackBatchOutputType type)
	{
		return type switch
		{
			PackBatchOutputType.Fpx => ".fpx",
			PackBatchOutputType.Gma => ".gma",
			_ => ".vpk"
		};
	}

	private static string GetPackBatchOutputPath(string inputDirectory, string outputDirectory, PackBatchOutputType type, bool multiFile)
	{
		var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory));
		if (string.IsNullOrWhiteSpace(name))
		{
			name = "package";
		}

		if (type == PackBatchOutputType.Gma)
		{
			return Path.Combine(outputDirectory, $"{name}.gma");
		}

		var extension = GetPackBatchExtension(type);
		if (!multiFile)
		{
			return Path.Combine(outputDirectory, $"{name}{extension}");
		}

		var dirSuffix = type == PackBatchOutputType.Fpx ? "_fdr" : "_dir";
		return Path.Combine(outputDirectory, $"{name}{dirSuffix}{extension}");
	}

	private static HashSet<string> GetExistingPackOutputPaths(string outputPath, bool multiFile)
	{
		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var outputs = new HashSet<string>(comparer);

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			return outputs;
		}

		var fullOutputPath = Path.GetFullPath(outputPath);
		if (File.Exists(fullOutputPath))
		{
			outputs.Add(fullOutputPath);
		}

		if (!multiFile)
		{
			return outputs;
		}

		var directory = Path.GetDirectoryName(fullOutputPath);
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return outputs;
		}

		var extension = Path.GetExtension(fullOutputPath);
		var baseName = Path.GetFileNameWithoutExtension(fullOutputPath);
		var prefix = baseName;
		if (baseName.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
		{
			prefix = baseName[..^"_dir".Length];
		}
		else if (baseName.EndsWith("_fdr", StringComparison.OrdinalIgnoreCase))
		{
			prefix = baseName[..^"_fdr".Length];
		}

		foreach (var file in Directory.EnumerateFiles(directory, $"{prefix}_???{extension}", SearchOption.TopDirectoryOnly))
		{
			outputs.Add(Path.GetFullPath(file));
		}

		return outputs;
	}

		private static void TryCleanupPackOutputs(string outputPath, bool multiFile, HashSet<string> preExistingOutputs)
		{
		if (string.IsNullOrWhiteSpace(outputPath))
		{
			return;
		}

		var currentOutputs = GetExistingPackOutputPaths(outputPath, multiFile);
		foreach (var file in currentOutputs)
		{
			if (preExistingOutputs.Contains(file))
			{
				continue;
			}

			try
			{
				if (File.Exists(file))
				{
					File.Delete(file);
				}
			}
			catch
			{
			}
			}
		}

		private static readonly string[] RecommendedGmaIgnorePatterns =
		{
			".git/",
			".github/",
			".vs/",
			".vscode/",
			".idea/",
			".svn/",
			".hg/",
			"bin/",
			"obj/",
			"node_modules/",
			"__pycache__/",
			".venv/",
			".pytest_cache/",
			".DS_Store",
			"Thumbs.db",
			"desktop.ini",
			".gitignore",
			".gitattributes",
			".gitmodules"
		};

		private static IReadOnlyList<string> ParseGmaIgnorePatterns(string? input)
		{
			if (string.IsNullOrWhiteSpace(input))
			{
			return Array.Empty<string>();
		}

		var parts = input
			.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(p => !string.IsNullOrWhiteSpace(p))
			.Select(p => p.Trim())
			.Where(p => p.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		return parts.Length == 0 ? Array.Empty<string>() : parts;
	}

	private static IReadOnlyList<string> ParseGmaTags(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return Array.Empty<string>();
		}

		var parts = input
			.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(p => !string.IsNullOrWhiteSpace(p))
			.Select(p => p.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return parts.Length == 0 ? Array.Empty<string>() : parts;
	}

	private void EnsureGmaAddonJsonExists(string inputDirectory, bool isBatchChildFolder)
	{
		var addonJsonPath = Path.Combine(inputDirectory, "addon.json");
		if (File.Exists(addonJsonPath))
		{
			return;
		}

		if (!GetChecked("PackGmaCreateAddonJsonCheckBox"))
		{
			throw new InvalidDataException("GMA packing requires an addon.json in the input folder. Enable \"Create addon.json if missing\" in Pack → GMA addon.json, or add addon.json manually.");
		}

		var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory));
		if (string.IsNullOrWhiteSpace(folderName))
		{
			folderName = "addon";
		}

		var title = GetText("PackGmaTitleTextBox");
		if (isBatchChildFolder || string.IsNullOrWhiteSpace(title))
		{
			title = folderName;
		}

		var description = GetText("PackGmaDescriptionTextBox") ?? string.Empty;
		var author = GetText("PackGmaAuthorTextBox") ?? string.Empty;

		var versionText = GetText("PackGmaVersionTextBox");
		var version = 1u;
		if (!string.IsNullOrWhiteSpace(versionText))
		{
			if (!uint.TryParse(versionText.Trim(), out version) || version == 0)
			{
				throw new InvalidDataException("GMA addon.json Version must be a positive integer.");
			}
		}

			var ignore = ParseGmaIgnorePatterns(GetText("PackGmaIgnoreTextBox"));
			var mergedIgnore = new List<string>(ignore.Count + RecommendedGmaIgnorePatterns.Length);
			var ignoreSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var pattern in ignore)
			{
				if (!string.IsNullOrWhiteSpace(pattern) && ignoreSet.Add(pattern.Trim()))
				{
					mergedIgnore.Add(pattern.Trim());
				}
			}

			foreach (var pattern in RecommendedGmaIgnorePatterns)
			{
				if (!string.IsNullOrWhiteSpace(pattern) && ignoreSet.Add(pattern.Trim()))
				{
					mergedIgnore.Add(pattern.Trim());
				}
			}

			ignore = mergedIgnore;
			var tags = ParseGmaTags(GetText("PackGmaTagsTextBox"));

			var json = System.Text.Json.JsonSerializer.Serialize(
				new
				{
				title,
				description,
				author,
				version,
				tags,
				ignore
			},
			new System.Text.Json.JsonSerializerOptions
			{
				WriteIndented = true,
				TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
			});

		File.WriteAllText(addonJsonPath, json);
		AppendLog($"Pack: wrote addon.json: {addonJsonPath}");
	}

	private async void OnPackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Pack", 0, 0, Message: "Starting..."));

				var input = GetText("PackInputTextBox");
				RequireDirectoryExists(input, GetPackInputMode() == PackInputMode.ParentOfChildFolders ? "Input parent folder" : "Input folder");
				var multiFile = GetChecked("PackMultiFileCheckBox");
				var withMd5 = GetChecked("PackWithMd5CheckBox");
				var writeLogFile = GetChecked("PackWriteLogFileCheckBox");
				var vpkToolPath = GetText("PackVpkToolTextBox");
				var directOptions = GetText("PackDirectOptionsTextBox");
				var splitMbText = GetText("PackSplitMbTextBox");
				long? maxArchiveSizeBytes = null;
				if (!string.IsNullOrWhiteSpace(splitMbText))
				{
					if (!long.TryParse(splitMbText, out var mb) || mb <= 0)
				{
					throw new InvalidDataException("Invalid split size (MB).");
				}

				maxArchiveSizeBytes = mb * 1024L * 1024L;
			}

			var preloadBytesText = GetText("PackPreloadBytesTextBox");
			var preloadBytes = 0;
			if (!string.IsNullOrWhiteSpace(preloadBytesText))
			{
				if (!int.TryParse(preloadBytesText, out preloadBytes) || preloadBytes < 0 || preloadBytes > ushort.MaxValue)
				{
					throw new InvalidDataException($"Invalid preload bytes value (expected 0 to {ushort.MaxValue}).");
				}
			}

			var versionText = GetComboBoxText("PackVpkVersionComboBox") ?? "1";
			var version = uint.TryParse(versionText, out var parsed) ? parsed : 1;

			var mode = GetPackInputMode();
			if (mode == PackInputMode.SingleFolder)
			{
				var output = GetText("PackOutputTextBox");
				RequireNonEmpty(output, "Output file");

				var outputExtension = Path.GetExtension(output).Trim().ToLowerInvariant();
				if (outputExtension == ".gma")
				{
					EnsureGmaAddonJsonExists(input, isBatchChildFolder: false);
				}

				ValidatePackOptions(input, output, multiFile, withMd5, version);

				if (outputExtension == ".gma" && (multiFile || withMd5 || maxArchiveSizeBytes is not null || version != 1 || preloadBytes != 0))
				{
					AppendLog("Note: GMA output ignores VPK options (multi-file/split/version/MD5/preload).");
				}

					await app.PackAsync(
						new PackRequest(
							InputDirectory: input,
							OutputPackagePath: output,
							MultiFile: multiFile,
							MaxArchiveSizeBytes: maxArchiveSizeBytes,
							PreloadBytes: preloadBytes,
							VpkVersion: version,
							IncludeMd5Sections: withMd5,
							VpkToolPath: vpkToolPath,
							WriteLogFile: writeLogFile,
							DirectOptions: directOptions,
							IgnoreWhitelistWarnings: GetChecked("PackGmaIgnoreWhitelistCheckBox"),
							Progress: progress),
						cancellationToken);

				AppendLog($"Packed to: {output}");

				if (GetChecked("PackOpenOutputCheckBox"))
				{
					var outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
					if (!string.IsNullOrWhiteSpace(outputDir))
					{
						TryOpenFolder(outputDir);
					}
				}

				return;
			}

			var outputDirectory = GetText("PackOutputTextBox");
			RequireNonEmpty(outputDirectory, "Output folder");
			Directory.CreateDirectory(outputDirectory);

			var outputType = GetPackBatchOutputType();
			var multiFileForBatch = outputType == PackBatchOutputType.Gma ? false : multiFile;
			var outputExtensionForMode = GetPackBatchExtension(outputType);
			if (outputType == PackBatchOutputType.Gma && (multiFile || withMd5 || maxArchiveSizeBytes is not null || version != 1 || preloadBytes != 0))
			{
				AppendLog("Note: GMA output ignores VPK options (multi-file/split/version/MD5/preload).");
			}

			var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
			var childFolders = Directory.EnumerateDirectories(input, "*", SearchOption.TopDirectoryOnly)
				.OrderBy(p => p, comparer)
				.ToArray();

			if (childFolders.Length == 0)
			{
				throw new InvalidDataException("No child folders found in the selected parent folder.");
			}

			var skipButton = GetRequiredControl<Button>("PackSkipCurrentFolderButton");
			skipButton.IsEnabled = false;

			var successes = 0;
			var failures = 0;
			var skipped = 0;

			try
			{
				for (var i = 0; i < childFolders.Length; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var childFolder = childFolders[i];
					var childName = Path.GetFileName(Path.TrimEndingDirectorySeparator(childFolder));
					progress.Report(new StunstickProgress("Pack", i, childFolders.Length, CurrentItem: childName));

					var outputPath = GetPackBatchOutputPath(childFolder, outputDirectory, outputType, multiFileForBatch);

					HashSet<string> preExistingOutputs = GetExistingPackOutputPaths(outputPath, multiFileForBatch);
					CancellationTokenSource? itemCancellation = null;

					try
					{
						if (outputType == PackBatchOutputType.Gma)
						{
							EnsureGmaAddonJsonExists(childFolder, isBatchChildFolder: true);
						}

						ValidatePackOptions(childFolder, outputPath, multiFileForBatch, withMd5, version);

						itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
						packSkipCurrentFolderCancellation = itemCancellation;
						skipButton.IsEnabled = true;

							await app.PackAsync(
								new PackRequest(
									InputDirectory: childFolder,
									OutputPackagePath: outputPath,
									MultiFile: multiFileForBatch,
									MaxArchiveSizeBytes: maxArchiveSizeBytes,
									PreloadBytes: preloadBytes,
									VpkVersion: version,
									IncludeMd5Sections: withMd5,
									VpkToolPath: vpkToolPath,
									WriteLogFile: writeLogFile,
									DirectOptions: directOptions,
									IgnoreWhitelistWarnings: GetChecked("PackGmaIgnoreWhitelistCheckBox"),
									Progress: progress),
								itemCancellation.Token);

						successes++;
						AppendLog($"Packed: {childName} → {outputPath}");
					}
					catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
					{
						skipped++;
						TryCleanupPackOutputs(outputPath, multiFileForBatch, preExistingOutputs);
						AppendLog($"Skipped: {childName}");
					}
					catch (Exception ex)
					{
						failures++;
						AppendLog($"Pack failed: {childName}: {ex.Message}");
					}
					finally
					{
						skipButton.IsEnabled = false;

						if (itemCancellation is not null)
						{
							if (ReferenceEquals(packSkipCurrentFolderCancellation, itemCancellation))
							{
								packSkipCurrentFolderCancellation = null;
							}

							itemCancellation.Dispose();
						}
					}
				}
			}
			finally
			{
				packSkipCurrentFolderCancellation = null;
				skipButton.IsEnabled = false;
				progress.Report(new StunstickProgress("Pack", childFolders.Length, childFolders.Length, Message: "Done"));
			}

			AppendLog($"Pack batch finished ({outputExtensionForMode}): {successes} succeeded, {skipped} skipped, {failures} failed.");

			if (GetChecked("PackOpenOutputCheckBox"))
			{
				TryOpenFolder(outputDirectory);
			}
			});
		}

		private void OnPackUseInPublishClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var mode = GetPackInputMode();
				if (mode != PackInputMode.SingleFolder)
				{
					AppendLog("Pack: switch to \"Single folder\" mode to use in Publish.");
					return;
				}

				var outputPath = GetText("PackOutputTextBox");
				RequireNonEmpty(outputPath, "Output file");

				var contentPath = Path.GetFullPath(outputPath);

				SetText("WorkshopPublishContentTextBox", contentPath);
				SelectMainTab("WorkshopTabItem");
				GetRequiredControl<TabControl>("WorkshopTabControl").SelectedIndex = 1;

				AppendLog($"Pack: routed to Workshop Publish: {contentPath}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnDecompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				progress.Report(new StunstickProgress("Decompile", 0, 0, Message: "Running..."));

				var mdlPath = GetText("DecompileMdlTextBox");
				var output = GetText("DecompileOutputTextBox");
					RequireNonEmpty(mdlPath, "MDL file/folder");
					RequireNonEmpty(output, "Output folder");
					var includeSubfolders = GetChecked("DecompileIncludeSubfoldersCheckBox");
					var writeLogFile = GetChecked("DecompileWriteLogFileCheckBox");
					var versionOverride = GetDecompileMdlVersionOverride();

				StreamWriter? logWriter = null;
				string? logPathFileName = null;
				try
				{
					if (writeLogFile)
					{
						Directory.CreateDirectory(output);
						logPathFileName = Path.Combine(output, "decompile.log");
						var logStream = new FileStream(logPathFileName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
						logWriter = new StreamWriter(logStream) { AutoFlush = true };

						logWriter.WriteLine("Stunstick decompile log");
						logWriter.WriteLine($"Started: {DateTimeOffset.Now:O}");
						logWriter.WriteLine($"Input: {Path.GetFullPath(mdlPath)}");
						logWriter.WriteLine($"Output: {Path.GetFullPath(output)}");
						logWriter.WriteLine();

						AppendLog($"Decompile: Writing log: {logPathFileName}");
					}

				var options = new DecompileOptions(
					WriteQcFile: GetChecked("DecompileWriteQcCheckBox"),
				QcGroupIntoQciFiles: GetChecked("DecompileQcGroupIntoQciFilesCheckBox"),
				QcSkinFamilyOnSingleLine: GetChecked("DecompileQcSkinFamilyOnSingleLineCheckBox"),
				QcOnlyChangedMaterialsInTextureGroupLines: GetChecked("DecompileQcOnlyChangedMaterialsInTextureGroupLinesCheckBox"),
				QcIncludeDefineBoneLines: GetChecked("DecompileQcIncludeDefineBoneLinesCheckBox"),
				WriteReferenceMeshSmdFiles: GetChecked("DecompileWriteRefSmdCheckBox"),
					WriteBoneAnimationSmdFiles: GetChecked("DecompileWriteAnimsCheckBox"),
					BoneAnimationPlaceInSubfolder: !GetChecked("DecompileAnimsSameFolderCheckBox"),
					WriteVertexAnimationVtaFile: GetChecked("DecompileWriteVtaCheckBox"),
					WritePhysicsMeshSmdFile: GetChecked("DecompileWritePhysicsSmdCheckBox"),
						WriteTextureBmpFiles: GetChecked("DecompileWriteTextureBmpsCheckBox"),
						WriteProceduralBonesVrdFile: GetChecked("DecompileWriteProceduralBonesVrdCheckBox"),
						WriteDeclareSequenceQciFile: GetChecked("DecompileWriteDeclareSequenceQciCheckBox"),
						WriteDebugInfoFiles: GetChecked("DecompileWriteDebugInfoFilesCheckBox"),
						WriteLodMeshSmdFiles: GetChecked("DecompileWriteLodSmdCheckBox"),
							RemovePathFromSmdMaterialFileNames: GetChecked("DecompileStripMaterialPathsCheckBox"),
							UseNonValveUvConversion: GetChecked("DecompileNonValveUvCheckBox"),
							FolderForEachModel: !GetChecked("DecompileFlatOutputCheckBox"),
						PrefixFileNamesWithModelName: GetChecked("DecompilePrefixFileNamesWithModelNameCheckBox"),
							StricterFormat: GetChecked("DecompileStricterFormatCheckBox"),
							QcUseMixedCaseForKeywords: GetChecked("DecompileMixedCaseQcCheckBox"),
							VersionOverride: versionOverride);

				if (File.Exists(mdlPath))
				{
					try
					{
						await app.DecompileAsync(new DecompileRequest(mdlPath, output, options), cancellationToken);
					}
					catch (Exception ex)
					{
						logWriter?.WriteLine($"ERROR: {ex.Message}");
						throw;
					}
					lastDecompileQcPath = options.WriteQcFile
						? Path.GetFullPath(GetDecompileOutputQcPath(mdlPath, output, options.FolderForEachModel))
						: null;
					if (!string.IsNullOrWhiteSpace(lastDecompileQcPath) && !File.Exists(lastDecompileQcPath))
					{
						lastDecompileQcPath = null;
					}
					AppendLog($"Decompiled to: {output}");

					if (GetChecked("DecompileOpenOutputCheckBox"))
					{
						var openPath = output;
						if (!GetChecked("DecompileFlatOutputCheckBox"))
						{
							var modelName = Path.GetFileNameWithoutExtension(mdlPath);
							if (!string.IsNullOrWhiteSpace(modelName))
							{
								openPath = Path.Combine(output, modelName);
							}
						}

						TryOpenFolder(openPath);
					}
				}
				else if (Directory.Exists(mdlPath))
				{
					lastDecompileQcPath = null;

					var search = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					var mdlFiles = Directory.EnumerateFiles(mdlPath, "*.mdl", search)
						.OrderBy(p => p, comparer)
						.ToArray();

					if (mdlFiles.Length == 0)
					{
						throw new InvalidDataException("No .mdl files found in the selected folder.");
					}

					var failures = 0;
					for (var i = 0; i < mdlFiles.Length; i++)
					{
						cancellationToken.ThrowIfCancellationRequested();
						var file = mdlFiles[i];
						progress.Report(new StunstickProgress("Decompile", i, mdlFiles.Length, CurrentItem: Path.GetFileName(file)));

						try
						{
							await app.DecompileAsync(new DecompileRequest(file, output, options), cancellationToken);
						}
						catch (OperationCanceledException)
						{
							throw;
						}
						catch (Exception ex)
						{
							failures++;
							var msg = $"Decompile failed: {file}: {ex.Message}";
							AppendLog(msg);
							logWriter?.WriteLine(msg);
						}
					}

					progress.Report(new StunstickProgress("Decompile", mdlFiles.Length, mdlFiles.Length, Message: "Done"));
					var succeeded = mdlFiles.Length - failures;
					var summary = failures == 0
						? $"Decompiled {succeeded} model(s) to: {output}"
						: $"Decompile finished: {succeeded} succeeded, {failures} failed. Output: {output}";
					AppendLog(summary);
					logWriter?.WriteLine(summary);

					if (GetChecked("DecompileOpenOutputCheckBox"))
					{
						TryOpenFolder(output);
					}
				}
				else
				{
					throw new FileNotFoundException("MDL file/folder not found.", mdlPath);
				}

					if (logWriter is not null)
					{
						logWriter.WriteLine();
						logWriter.WriteLine($"Ended: {DateTimeOffset.Now:O}");
					}
				}
				finally
				{
					if (logWriter is not null)
					{
						logWriter.Flush();
						logWriter.Dispose();
					}
				}
					});
				}

		private void OnDecompileUseInCompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var qcPath = string.IsNullOrWhiteSpace(lastDecompileQcPath) ? null : lastDecompileQcPath;
					if (string.IsNullOrWhiteSpace(qcPath) || !File.Exists(qcPath))
					{
						var mdlPath = GetText("DecompileMdlTextBox");
						if (Directory.Exists(mdlPath))
						{
							AppendLog("Decompile: folder input used. Pick a specific model.qc to compile.");
							return;
						}

						var output = GetText("DecompileOutputTextBox");
						var folderForEachModel = !GetChecked("DecompileFlatOutputCheckBox");
						if (!string.IsNullOrWhiteSpace(mdlPath) && !string.IsNullOrWhiteSpace(output))
						{
						var candidate = GetDecompileOutputQcPath(mdlPath, output, folderForEachModel);
						if (File.Exists(candidate))
						{
							qcPath = Path.GetFullPath(candidate);
						}
					}
				}

				if (string.IsNullOrWhiteSpace(qcPath))
				{
					AppendLog("Decompile: no QC found. Run Decompile with \"Write QC\" enabled first.");
					return;
				}

				GetRequiredControl<TextBox>("CompileQcTextBox").Text = qcPath;
				SelectMainTab("CompileTabItem");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnCompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				progress.Report(new StunstickProgress("Compile", 0, 0, Message: "Launching..."));

				var qcPath = GetText("CompileQcTextBox");
				RequireNonEmpty(qcPath, "QC file/folder");
				var gameDir = GetText("CompileGameDirTextBox");
				var studiomdl = GetText("CompileStudioMdlTextBox");
				var steamAppId = ParseUInt(GetText("CompileSteamAppIdTextBox"), "Steam AppID");
				var steamRoot = GetText("CompileSteamRootTextBox");
				var winePrefix = GetText("CompileWinePrefixTextBox");
				var wineCmd = GetText("CompileWineCommandTextBox");
				var noP4 = GetChecked("CompileNoP4CheckBox");
				var verbose = GetChecked("CompileVerboseCheckBox");
				var includeSubfolders = GetChecked("CompileIncludeSubfoldersCheckBox");
				var defineBones = GetChecked("CompileDefineBonesCheckBox");
				var defineBonesWriteQci = GetChecked("CompileDefineBonesWriteQciFileCheckBox");
					var defineBonesFileName = GetText("CompileDefineBonesQciFileNameTextBox");
					var defineBonesOverwrite = GetChecked("CompileDefineBonesOverwriteQciFileCheckBox");
					var defineBonesModifyQc = GetChecked("CompileDefineBonesModifyQcFileCheckBox");
					var directOptions = GetText("CompileDirectOptionsTextBox");
						var writeLogFile = GetChecked("CompileWriteLogFileCheckBox");
						var copyOutput = GetChecked("CompileCopyOutputCheckBox");
						var outputCopyFolder = GetText("CompileOutputCopyFolderTextBox");

				var resolvedGameDir = ResolveGameDirectory(gameDir, steamAppId, steamRoot);

				ValidateToolLaunchInputs(studiomdl, gameDir, steamAppId, steamRoot, "StudioMDL");

				if (copyOutput)
				{
					RequireNonEmpty(outputCopyFolder, "Output folder");
					Directory.CreateDirectory(outputCopyFolder);
				}

					var output = new Progress<string>(AppendLog);

					if (File.Exists(qcPath))
					{
					var exitCode = await app.CompileWithStudioMdlAsync(
						new StudioMdlCompileRequest(
							StudioMdlPath: string.IsNullOrWhiteSpace(studiomdl) ? null : studiomdl,
							QcPath: qcPath,
							GameDirectory: string.IsNullOrWhiteSpace(gameDir) ? null : gameDir,
							SteamAppId: steamAppId,
							SteamRoot: string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot,
							WineOptions: new WineOptions(
								Prefix: string.IsNullOrWhiteSpace(winePrefix) ? null : winePrefix,
								WineCommand: string.IsNullOrWhiteSpace(wineCmd) ? "wine" : wineCmd),
							NoP4: noP4,
							Verbose: verbose,
							DefineBones: defineBones,
							DefineBonesCreateQciFile: defineBonesWriteQci,
							DefineBonesQciFileName: string.IsNullOrWhiteSpace(defineBonesFileName) ? "DefineBones" : defineBonesFileName,
									DefineBonesOverwriteQciFile: defineBonesOverwrite,
									DefineBonesModifyQcFile: defineBonesModifyQc,
									DirectOptions: string.IsNullOrWhiteSpace(directOptions) ? null : directOptions,
									WriteLogFile: writeLogFile,
									Output: output),
								cancellationToken);

						lastCompileOutputMdlPath = null;
						if (exitCode == 0)
						{
							var lookupGameDir = resolvedGameDir;
							if (string.IsNullOrWhiteSpace(lookupGameDir))
							{
								try
								{
									lookupGameDir = Path.GetDirectoryName(Path.GetFullPath(qcPath));
								}
								catch
								{
									lookupGameDir = null;
								}

								if (!string.IsNullOrWhiteSpace(lookupGameDir))
								{
									AppendLog($"Compile: Game Dir not set; using QC folder for output discovery: {lookupGameDir}");
								}
							}

							var compiledModelPath = TryGetCompiledModelPath(qcPath, lookupGameDir, copyOutput ? outputCopyFolder : null);
							if (!string.IsNullOrWhiteSpace(compiledModelPath) && File.Exists(compiledModelPath))
							{
								lastCompileOutputMdlPath = Path.GetFullPath(compiledModelPath);
								AppendLog($"Compile output model: {lastCompileOutputMdlPath}");
							}

							if (copyOutput)
							{
								var copied = TryCopyCompileOutputs(qcPath, lookupGameDir, outputCopyFolder, cancellationToken);
								if (!string.IsNullOrWhiteSpace(copied) && File.Exists(copied))
								{
									lastCompileOutputMdlPath = copied;
									AppendLog($"Compile copied model: {copied}");
								}
							}
						}

						AppendLog($"Compile exit code: {exitCode}");
					}
				else if (Directory.Exists(qcPath))
				{
					lastCompileOutputMdlPath = null;

					var search = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
					var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
					var qcFiles = Directory.EnumerateFiles(qcPath, "*.qc", search)
						.OrderBy(p => p, comparer)
						.ToArray();

					if (qcFiles.Length == 0)
					{
						throw new InvalidDataException("No .qc files found in the selected folder.");
					}

					var failures = 0;
					for (var i = 0; i < qcFiles.Length; i++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						var qcFile = qcFiles[i];
						progress.Report(new StunstickProgress("Compile", i, qcFiles.Length, CurrentItem: Path.GetFileName(qcFile)));

						var qciName = string.IsNullOrWhiteSpace(defineBonesFileName) ? "DefineBones" : defineBonesFileName;
						if (defineBonesWriteQci)
						{
							qciName = $"{Path.GetFileNameWithoutExtension(qcFile)}_{qciName}";
						}

						try
						{
							var exitCode = await app.CompileWithStudioMdlAsync(
								new StudioMdlCompileRequest(
									StudioMdlPath: string.IsNullOrWhiteSpace(studiomdl) ? null : studiomdl,
									QcPath: qcFile,
									GameDirectory: string.IsNullOrWhiteSpace(gameDir) ? null : gameDir,
									SteamAppId: steamAppId,
									SteamRoot: string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot,
									WineOptions: new WineOptions(
										Prefix: string.IsNullOrWhiteSpace(winePrefix) ? null : winePrefix,
										WineCommand: string.IsNullOrWhiteSpace(wineCmd) ? "wine" : wineCmd),
									NoP4: noP4,
									Verbose: verbose,
									DefineBones: defineBones,
									DefineBonesCreateQciFile: defineBonesWriteQci,
									DefineBonesQciFileName: qciName,
											DefineBonesOverwriteQciFile: defineBonesOverwrite,
											DefineBonesModifyQcFile: defineBonesModifyQc,
											DirectOptions: string.IsNullOrWhiteSpace(directOptions) ? null : directOptions,
											WriteLogFile: writeLogFile,
											Output: output),
										cancellationToken);

						if (exitCode != 0)
						{
							failures++;
							AppendLog($"Compile failed (exit {exitCode}): {qcFile}");
						}
						else if (copyOutput)
						{
							var lookupGameDir = resolvedGameDir;
							if (string.IsNullOrWhiteSpace(lookupGameDir))
							{
								try
								{
									lookupGameDir = Path.GetDirectoryName(Path.GetFullPath(qcFile));
								}
								catch
								{
									lookupGameDir = null;
								}
							}

							var copied = TryCopyCompileOutputs(qcFile, lookupGameDir, outputCopyFolder, cancellationToken);
							if (!string.IsNullOrWhiteSpace(copied) && File.Exists(copied))
							{
								lastCompileOutputMdlPath = copied;
								AppendLog($"Compile copied model: {copied}");
							}
						}
							}
							catch (Exception ex)
							{
								failures++;
								AppendLog($"Compile failed: {qcFile}: {ex.Message}");
						}
					}

					progress.Report(new StunstickProgress("Compile", qcFiles.Length, qcFiles.Length, Message: "Done"));
					AppendLog($"Compile finished: {qcFiles.Length - failures} succeeded, {failures} failed.");
				}
				else
				{
					throw new FileNotFoundException("QC file/folder not found.", qcPath);
				}
				});
			}

		private void OnCompileUseInViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var qcPath = GetText("CompileQcTextBox");
				if (Directory.Exists(qcPath))
				{
					AppendLog("Compile: folder input selected. Select a single QC file to use in View.");
					return;
				}

			var gameDir = GetText("CompileGameDirTextBox");
			var steamAppId = ParseUInt(GetText("CompileSteamAppIdTextBox"), "Steam AppID");
			var steamRoot = GetText("CompileSteamRootTextBox");
			var copyOutput = GetChecked("CompileCopyOutputCheckBox");
			var copyOutputFolder = GetText("CompileOutputCopyFolderTextBox");

			var mdlPath = string.IsNullOrWhiteSpace(lastCompileOutputMdlPath) ? null : lastCompileOutputMdlPath;
			if (string.IsNullOrWhiteSpace(mdlPath) || !File.Exists(mdlPath))
			{
				var resolvedGameDir = ResolveGameDirectory(gameDir, steamAppId, steamRoot);
				var lookupGameDir = resolvedGameDir;
				if (string.IsNullOrWhiteSpace(lookupGameDir))
				{
					try
					{
						lookupGameDir = Path.GetDirectoryName(Path.GetFullPath(qcPath));
					}
					catch
					{
						lookupGameDir = null;
					}
				}

				var candidate = TryGetCompiledModelPath(qcPath, lookupGameDir, copyOutput ? copyOutputFolder : null);
				if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
				{
					mdlPath = Path.GetFullPath(candidate);
					lastCompileOutputMdlPath = mdlPath;
				}
				}

				if (string.IsNullOrWhiteSpace(mdlPath))
				{
					AppendLog("Compile: could not locate compiled MDL. Ensure QC has $modelname and a valid Game Dir (or Steam AppID).");
					return;
				}

				var view = GetRequiredControl<TextBox>("ViewMdlTextBox");
				view.Text = mdlPath;

				var compileGameDir = GetText("CompileGameDirTextBox");
				if (!string.IsNullOrWhiteSpace(compileGameDir))
				{
					SetText("ViewGameDirTextBox", compileGameDir);
				}

				SetText("ViewSteamAppIdTextBox", GetText("CompileSteamAppIdTextBox"));
				SetText("ViewSteamRootTextBox", GetText("CompileSteamRootTextBox"));
				SetText("ViewWinePrefixTextBox", GetText("CompileWinePrefixTextBox"));
				SetText("ViewWineCommandTextBox", GetText("CompileWineCommandTextBox"));

				SelectMainTab("ViewTabItem");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
			progress.Report(new StunstickProgress("View", 0, 0, Message: "Launching..."));

			var mdlPath = GetText("ViewMdlTextBox");
			RequireFileExists(mdlPath, "MDL");
			var gameDir = GetText("ViewGameDirTextBox");
			var hlmv = GetText("ViewHlmvTextBox");
			var steamAppId = ParseUInt(GetText("ViewSteamAppIdTextBox"), "Steam AppID");
			var steamRoot = GetText("ViewSteamRootTextBox");
			var winePrefix = GetText("ViewWinePrefixTextBox");
			var wineCmd = GetText("ViewWineCommandTextBox");

			ValidateToolLaunchInputs(hlmv, gameDir, steamAppId, steamRoot, "HLMV");

			var exitCode = await app.ViewWithHlmvAsync(
				new HlmvViewRequest(
					HlmvPath: string.IsNullOrWhiteSpace(hlmv) ? null : hlmv,
					MdlPath: mdlPath,
					GameDirectory: string.IsNullOrWhiteSpace(gameDir) ? null : gameDir,
					SteamAppId: steamAppId,
					SteamRoot: string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot,
					WineOptions: new WineOptions(
						Prefix: string.IsNullOrWhiteSpace(winePrefix) ? null : winePrefix,
						WineCommand: string.IsNullOrWhiteSpace(wineCmd) ? "wine" : wineCmd)),
				cancellationToken);

				AppendLog($"View exit code: {exitCode}");
			});
		}

		private async void OnViewAsReplacementClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			await RunOperationAsync(async (progress, cancellationToken) =>
			{
				progress.Report(new StunstickProgress("View", 0, 0, Message: "Launching (replacement)..."));

				var mdlPath = GetText("ViewMdlTextBox");
				RequireFileExists(mdlPath, "MDL");
				var gameDir = GetText("ViewGameDirTextBox");
				var hlmv = GetText("ViewHlmvTextBox");
				var steamAppId = ParseUInt(GetText("ViewSteamAppIdTextBox"), "Steam AppID");
				var steamRoot = GetText("ViewSteamRootTextBox");
				var winePrefix = GetText("ViewWinePrefixTextBox");
				var wineCmd = GetText("ViewWineCommandTextBox");

				ValidateToolLaunchInputs(hlmv, gameDir, steamAppId, steamRoot, "HLMV");

				var exitCode = await app.ViewWithHlmvAsync(
					new HlmvViewRequest(
						HlmvPath: string.IsNullOrWhiteSpace(hlmv) ? null : hlmv,
						MdlPath: mdlPath,
						GameDirectory: string.IsNullOrWhiteSpace(gameDir) ? null : gameDir,
						SteamAppId: steamAppId,
						SteamRoot: string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot,
						WineOptions: new WineOptions(
							Prefix: string.IsNullOrWhiteSpace(winePrefix) ? null : winePrefix,
							WineCommand: string.IsNullOrWhiteSpace(wineCmd) ? "wine" : wineCmd),
						ViewAsReplacement: true),
					cancellationToken);

				AppendLog($"View (replacement) exit code: {exitCode}");
			});
		}

	private void OnViewMdlTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		ScheduleViewDataViewerUpdate(delayMs: 350);
	}

	private void OnViewDataViewerAutoRunChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		try
		{
			SaveSettings();
			if (GetChecked("ViewDataViewerAutoRunCheckBox"))
			{
				ScheduleViewDataViewerUpdate(delayMs: 0);
			}
			else
			{
				CancelViewDataViewerUpdate();
			}
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnViewDataViewerMdlVersionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isLoadingSettings)
		{
			return;
		}

		try
		{
			SaveSettings();
			ScheduleViewDataViewerUpdate(delayMs: 0);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnViewDataViewerRunClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			await RunViewDataViewerNowAsync();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnViewPreviewLoadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			var mdlPath = GetText("ViewMdlTextBox");
			RequireFileExists(mdlPath, "MDL");

			var lodIndex = Math.Max(0, GetComboBoxIndex("ViewPreviewLodComboBox"));
			progress.Report(new StunstickProgress("Preview", 0, 0, CurrentItem: Path.GetFileName(mdlPath), Message: "Loading MDL/VTX/VVD/PHY..."));
			var preview = await MdlPreviewLoader.LoadAsync(mdlPath, lodIndex: lodIndex, includePhysics: true, cancellationToken);

			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				ApplyViewPreview(preview, resetCamera: true);
			});

			var meshTriCount = preview.MeshGeometry?.Triangles.Count ?? 0;
			var phyTriCount = preview.PhysicsGeometry?.Triangles.Count ?? 0;
			AppendLog($"Preview loaded: {Path.GetFileName(mdlPath)} (model: {meshTriCount:N0} tris, physics: {phyTriCount:N0} tris)");
		});

		ScheduleViewDataViewerUpdate(delayMs: 0);
	}

	private void CancelViewDataViewerUpdate()
	{
		try
		{
			viewDataViewerCancellation?.Cancel();
			viewDataViewerCancellation?.Dispose();
		}
		catch
		{
		}
		finally
		{
			viewDataViewerCancellation = null;
		}
	}

	private void ScheduleViewDataViewerUpdate(int delayMs)
	{
		try
		{
			if (!Dispatcher.UIThread.CheckAccess())
			{
				Dispatcher.UIThread.Post(() => ScheduleViewDataViewerUpdate(delayMs));
				return;
			}

			if (!GetChecked("ViewDataViewerAutoRunCheckBox"))
			{
				return;
			}

			CancelViewDataViewerUpdate();
			viewDataViewerCancellation = new CancellationTokenSource();
			var token = viewDataViewerCancellation.Token;

			_ = Task.Run(async () =>
			{
				try
				{
					if (delayMs > 0)
					{
						await Task.Delay(delayMs, token).ConfigureAwait(false);
					}

					await RunViewDataViewerAsync(token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
				}
				catch
				{
				}
			}, token);
		}
		catch
		{
		}
	}

	private async Task RunViewDataViewerNowAsync()
	{
		CancelViewDataViewerUpdate();
		viewDataViewerCancellation = new CancellationTokenSource();

		try
		{
			await RunViewDataViewerAsync(viewDataViewerCancellation.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

		private int? GetViewDataViewerMdlVersionOverride()
		{
			var text = GetComboBoxText("ViewDataViewerMdlVersionComboBox")?.Trim();
			if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

			return int.TryParse(text, out var version) ? version : null;
		}

		private int? GetDecompileMdlVersionOverride()
		{
			var text = GetComboBoxText("DecompileMdlVersionComboBox")?.Trim();
			if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			return int.TryParse(text, out var version) ? version : null;
		}

		private int? GetInspectMdlVersionOverride()
		{
			var text = GetComboBoxText("InspectMdlVersionComboBox")?.Trim();
			if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return int.TryParse(text, out var version) ? version : null;
	}

	private async Task RunViewDataViewerAsync(CancellationToken cancellationToken)
	{
		try
		{
			string? mdlPath = null;
			int? versionOverride = null;

			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				mdlPath = GetText("ViewMdlTextBox");
				versionOverride = GetViewDataViewerMdlVersionOverride();
			});

			if (string.IsNullOrWhiteSpace(mdlPath) || !File.Exists(mdlPath))
			{
				await Dispatcher.UIThread.InvokeAsync(() =>
				{
					GetRequiredControl<TextBox>("ViewDataViewerOutputTextBox").Text = string.Empty;
				});
				return;
			}

			var fullPath = Path.GetFullPath(mdlPath);

			var outputLines = new List<string>(capacity: 64)
			{
				$"Path: {fullPath}"
			};

			if (versionOverride is not null)
			{
				outputLines.Add($"OverrideMdlVersion: {versionOverride.Value}");
			}

			var fileInfo = new FileInfo(fullPath);
			outputLines.Add($"SizeBytes: {fileInfo.Length} ({FormatBytes(fileInfo.Length)})");

			var mdl = await app.InspectMdlAsync(fullPath, new MdlInspectOptions(VersionOverride: versionOverride), cancellationToken);
			AppendMdlInspectLines(outputLines, mdl);

			var output = string.Join(Environment.NewLine, outputLines);
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				GetRequiredControl<TextBox>("ViewDataViewerOutputTextBox").Text = output;
			});
		}
		catch (Exception ex)
		{
			await Dispatcher.UIThread.InvokeAsync(() =>
			{
				GetRequiredControl<TextBox>("ViewDataViewerOutputTextBox").Text = $"ERROR: {ex.Message}";
			});
		}
	}

	private async void OnViewPreviewLodChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isViewPreviewUiUpdating)
		{
			return;
		}

		var mdlPath = GetText("ViewMdlTextBox");
		if (string.IsNullOrWhiteSpace(mdlPath) || !File.Exists(mdlPath))
		{
			return;
		}

		var mode = GetComboBoxIndex("ViewPreviewModeComboBox");
		if (mode != 0)
		{
			return;
		}

		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			var lodIndex = Math.Max(0, GetComboBoxIndex("ViewPreviewLodComboBox"));
			progress.Report(new StunstickProgress("Preview", 0, 0, CurrentItem: Path.GetFileName(mdlPath), Message: $"Loading LOD {lodIndex}..."));
			var preview = await MdlPreviewLoader.LoadAsync(mdlPath, lodIndex: lodIndex, includePhysics: true, cancellationToken);
			await Dispatcher.UIThread.InvokeAsync(() => ApplyViewPreview(preview, resetCamera: true));
		});
	}

	private void OnViewPreviewModeChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isViewPreviewUiUpdating)
		{
			return;
		}

		try
		{
			UpdateViewPreviewViewer(resetCamera: true);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnViewPreviewMaterialChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (isViewPreviewUiUpdating)
		{
			return;
		}

		try
		{
			UpdateViewPreviewMaterialFilter();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnViewPreviewResetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			GetRequiredControl<ModelViewerControl>("ViewPreviewControl").ResetCamera();
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void ApplyViewPreview(MdlPreviewResult preview, bool resetCamera)
	{
		viewPreview = preview;

		var desiredMaterialIndex = GetViewPreviewSelectedMaterialIndex();

		isViewPreviewUiUpdating = true;
		try
		{
			PopulateViewPreviewLodComboBox(preview);
			PopulateViewPreviewMaterialComboBox(preview);
			SetViewPreviewSelectedMaterialIndex(desiredMaterialIndex);
			SetViewPreviewText(preview);
			UpdateViewPreviewViewer(resetCamera);
		}
		finally
		{
			isViewPreviewUiUpdating = false;
		}
	}

	private void PopulateViewPreviewLodComboBox(MdlPreviewResult preview)
	{
		var lodCombo = GetRequiredControl<ComboBox>("ViewPreviewLodComboBox");
		var items = new List<int>();
		var count = Math.Max(1, preview.MaxLodCount);
		for (var i = 0; i < count; i++)
		{
			items.Add(i);
		}

		lodCombo.ItemsSource = items;

			var desired = Math.Clamp(preview.LodIndex, 0, count - 1);
			lodCombo.SelectedIndex = desired;
		}

	private void PopulateViewPreviewMaterialComboBox(MdlPreviewResult preview)
	{
		var materialCombo = GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox");
		var items = new List<ViewPreviewMaterialListItem>
		{
			new(index: -1, name: "(All)")
		};

		for (var i = 0; i < preview.Model.Textures.Count; i++)
		{
			var texture = preview.Model.Textures[i];
			var name = NormalizeMaterialName(texture.PathFileName);
			if (string.IsNullOrWhiteSpace(name))
			{
				name = $"material_{i}";
			}

			items.Add(new ViewPreviewMaterialListItem(index: i, name: name));
		}

		materialCombo.ItemsSource = items;
		materialCombo.SelectedIndex = 0;
	}

	private int? GetViewPreviewSelectedMaterialIndex()
	{
		try
		{
			var combo = GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox");
			if (combo.SelectedItem is ViewPreviewMaterialListItem item && item.Index >= 0)
			{
				return item.Index;
			}
		}
		catch
		{
		}

		return null;
	}

	private void SetViewPreviewSelectedMaterialIndex(int? materialIndex)
	{
		var combo = GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox");
		if (materialIndex is not int index || index < 0)
		{
			combo.SelectedIndex = 0;
			return;
		}

		var items = combo.ItemsSource as IEnumerable<ViewPreviewMaterialListItem> ?? Array.Empty<ViewPreviewMaterialListItem>();
		var match = items.FirstOrDefault(item => item.Index == index);
		combo.SelectedItem = match ?? items.FirstOrDefault(item => item.Index < 0) ?? null;
	}

		private void SetViewPreviewText(MdlPreviewResult preview)
		{
			var header = preview.Model.Header;
			var meshTriCount = preview.MeshGeometry?.Triangles.Count ?? 0;
			var phyTriCount = preview.PhysicsGeometry?.Triangles.Count ?? 0;

			var summaryLines = new List<string>
			{
				$"Path: {preview.MdlPath}",
				$"Name: {header.Name}",
				$"Version: {header.Version}  Flags: 0x{header.Flags:X}  Checksum: {header.Checksum}  Length: {header.Length}",
				$"Bones: {preview.Model.Bones.Count}  BodyParts: {preview.Model.BodyParts.Count}  Sequences: {preview.Model.SequenceDescs.Count}",
				$"Animations: {preview.Model.AnimationDescs.Count}  FlexDescs: {preview.Model.FlexDescs.Count}  FlexControllers: {preview.Model.FlexControllers.Count}  FlexRules: {preview.Model.FlexRules.Count}",
				$"Textures: {preview.Model.Textures.Count}  TexturePaths: {preview.Model.TexturePaths.Count}  SkinFamilies: {preview.Model.SkinFamilies.Count}",
				$"Model mesh: {(preview.MeshGeometry is null ? "not loaded" : $"{meshTriCount:N0} triangles")} {(string.IsNullOrWhiteSpace(preview.MeshError) ? string.Empty : $"({preview.MeshError})")}".Trim(),
				$"Physics mesh: {(preview.PhysicsGeometry is null ? "not loaded" : $"{phyTriCount:N0} triangles")} {(string.IsNullOrWhiteSpace(preview.PhysicsError) ? string.Empty : $"({preview.PhysicsError})")}".Trim()
			};

			summaryLines.Add(string.Empty);
			summaryLines.Add($"Body parts ({preview.Model.BodyParts.Count}):");
			for (var bodyPartIndex = 0; bodyPartIndex < preview.Model.BodyParts.Count; bodyPartIndex++)
			{
				var bodyPart = preview.Model.BodyParts[bodyPartIndex];
				var bodyPartName = string.IsNullOrWhiteSpace(bodyPart.Name) ? $"bodypart_{bodyPartIndex}" : bodyPart.Name.Trim();
				summaryLines.Add($"  [{bodyPartIndex}] {bodyPartName}  Models: {bodyPart.Models.Count}");

				for (var modelIndex = 0; modelIndex < bodyPart.Models.Count; modelIndex++)
				{
					var subModel = bodyPart.Models[modelIndex];
					var subModelName = string.IsNullOrWhiteSpace(subModel.Name) ? $"model_{modelIndex}" : subModel.Name.Trim();
					summaryLines.Add($"    [{modelIndex}] {subModelName}  Meshes: {subModel.Meshes.Count}  Vertices: {subModel.VertexCount}");
				}
			}

			summaryLines.Add(string.Empty);
			summaryLines.Add($"Bones ({preview.Model.Bones.Count}):");
			for (var boneIndex = 0; boneIndex < preview.Model.Bones.Count; boneIndex++)
			{
				var bone = preview.Model.Bones[boneIndex];
				var boneName = string.IsNullOrWhiteSpace(bone.Name) ? $"bone_{boneIndex}" : bone.Name.Trim();
				summaryLines.Add($"  [{boneIndex}] {boneName}  Parent: {bone.ParentIndex}");
			}

			GetRequiredControl<TextBox>("ViewPreviewSummaryTextBox").Text = string.Join(Environment.NewLine, summaryLines.Where(s => s is not null));

		var materialsLines = new List<string>();
		materialsLines.Add($"Texture paths ({preview.Model.TexturePaths.Count}):");
		for (var i = 0; i < preview.Model.TexturePaths.Count; i++)
		{
			var path = (preview.Model.TexturePaths[i] ?? string.Empty).Replace('\\', '/').Trim();
			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}

			materialsLines.Add($"  [{i}] {path}");
		}

		materialsLines.Add(string.Empty);
		materialsLines.Add($"Materials / textures ({preview.Model.Textures.Count}):");
		for (var i = 0; i < preview.Model.Textures.Count; i++)
		{
			var texture = preview.Model.Textures[i];
			var name = NormalizeMaterialName(texture.PathFileName);
			if (string.IsNullOrWhiteSpace(name))
			{
				name = $"material_{i}";
			}

			materialsLines.Add($"  [{i}] {name}");
		}

		materialsLines.Add(string.Empty);
		materialsLines.Add($"Skin families ({preview.Model.SkinFamilies.Count}):");
		for (var i = 0; i < preview.Model.SkinFamilies.Count; i++)
		{
			var family = preview.Model.SkinFamilies[i];
			var indexes = family.TextureIndexes.Select(index => index.ToString()).ToArray();
			var indexText = indexes.Length == 0 ? "(none)" : string.Join(", ", indexes);
			materialsLines.Add($"  [{i}] {indexText}");
		}

		GetRequiredControl<TextBox>("ViewPreviewMaterialsTextBox").Text = string.Join(Environment.NewLine, materialsLines);

		var sequenceLines = new List<string>();
		sequenceLines.Add($"Sequences ({preview.Model.SequenceDescs.Count}):");
		for (var i = 0; i < preview.Model.SequenceDescs.Count; i++)
		{
			var seq = preview.Model.SequenceDescs[i];
			var name = string.IsNullOrWhiteSpace(seq.Name) ? $"sequence_{i}" : seq.Name.Trim();
			sequenceLines.Add($"  [{i}] {name}  Flags: 0x{seq.Flags:X}  Blends: {seq.BlendCount}  Group: {seq.GroupSize0}x{seq.GroupSize1}  AnimDescs: {seq.AnimDescIndexes.Count}");
		}

		GetRequiredControl<TextBox>("ViewPreviewSequencesTextBox").Text = string.Join(Environment.NewLine, sequenceLines);

			var physicsLines = new List<string>();
			physicsLines.Add("Physics:");
			if (preview.PhysicsHeader is not null)
			{
				var phyHeader = preview.PhysicsHeader;
				physicsLines.Add($"  Header: Solids={phyHeader.SolidCount}  Checksum={phyHeader.Checksum}  Id={phyHeader.Id}  Size={phyHeader.Size}");
				physicsLines.Add($"  Read: Solids={preview.PhysicsSolidsRead}  ConvexMeshes={preview.PhysicsConvexMeshesRead}");
			}

			physicsLines.Add(preview.PhysicsGeometry is null ? $"  {preview.PhysicsError ?? "not loaded"}" : $"  Triangles: {phyTriCount:N0}");
			GetRequiredControl<TextBox>("ViewPreviewPhysicsTextBox").Text = string.Join(Environment.NewLine, physicsLines);
		}

	private void UpdateViewPreviewViewer(bool resetCamera)
	{
		var preview = viewPreview;
		var modeCombo = GetRequiredControl<ComboBox>("ViewPreviewModeComboBox");
		var lodCombo = GetRequiredControl<ComboBox>("ViewPreviewLodComboBox");
		var materialCombo = GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox");

		var modeIndex = modeCombo.SelectedIndex;

		ModelGeometry? geometry = null;
		var statusText = string.Empty;

		if (preview is null)
		{
			geometry = null;
			statusText = string.Empty;
		}
		else if (modeIndex == 1)
		{
			geometry = preview.PhysicsGeometry;
			statusText = geometry is null
				? $"Physics: {preview.PhysicsError ?? "not loaded"}"
				: $"Physics triangles: {geometry.Triangles.Count:N0}";
		}
		else
		{
			geometry = preview.MeshGeometry;
			statusText = geometry is null
				? $"Model: {preview.MeshError ?? "not loaded"}"
				: $"Model triangles: {geometry.Triangles.Count:N0} (LOD {preview.LodIndex})";
		}

		lodCombo.IsEnabled = modeIndex == 0;
		materialCombo.IsEnabled = modeIndex == 0;

		var viewer = GetRequiredControl<ModelViewerControl>("ViewPreviewControl");
		viewer.SetGeometry(geometry, resetCamera);

		GetRequiredControl<TextBlock>("ViewPreviewInfoTextBlock").Text = statusText;

		UpdateViewPreviewMaterialFilter();
	}

	private void UpdateViewPreviewMaterialFilter()
	{
		var modeIndex = GetComboBoxIndex("ViewPreviewModeComboBox");
		var viewer = GetRequiredControl<ModelViewerControl>("ViewPreviewControl");
		if (modeIndex != 0)
		{
			viewer.SetMaterialFilter(null);
			return;
		}

		var combo = GetRequiredControl<ComboBox>("ViewPreviewMaterialComboBox");
		if (combo.SelectedItem is ViewPreviewMaterialListItem item && item.Index >= 0)
		{
			viewer.SetMaterialFilter(item.Index);
		}
		else
		{
			viewer.SetMaterialFilter(null);
		}
	}

	private static string NormalizeMaterialName(string path)
	{
		path = (path ?? string.Empty).Replace('\\', '/').Trim();
		if (path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
		{
			path = path[..^4];
		}

		return path;
	}

		private void OnViewUseInDecompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var mdlPath = GetText("ViewMdlTextBox");
				if (string.IsNullOrWhiteSpace(mdlPath))
				{
					AppendLog("View: select an MDL first.");
					return;
				}

				GetRequiredControl<TextBox>("DecompileMdlTextBox").Text = mdlPath;
				SelectMainTab("DecompileTabItem");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnViewUseInInspectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var mdlPath = GetText("ViewMdlTextBox");
				if (string.IsNullOrWhiteSpace(mdlPath))
				{
					AppendLog("View: select an MDL first.");
					return;
				}

				GetRequiredControl<TextBox>("InspectPathTextBox").Text = mdlPath;
				SelectMainTab("InspectTabItem");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnViewRunGameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var viewAppIdText = GetText("ViewSteamAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(viewAppIdText) && uint.TryParse(viewAppIdText, out var viewAppId) && viewAppId != 0)
				{
					TryOpenUri($"steam://rungameid/{viewAppId}");
					AppendLog($"Requested Steam launch: {viewAppId}");
					return;
				}

				var toolchainAppIdText = GetText("ToolchainSelectedAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(toolchainAppIdText) && uint.TryParse(toolchainAppIdText, out var toolchainAppId) && toolchainAppId != 0)
				{
					TryOpenUri($"steam://rungameid/{toolchainAppId}");
					AppendLog($"Requested Steam launch: {toolchainAppId}");
					return;
				}

				AppendLog("View: set a Steam AppID (or select a game in Games tab) to run.");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnViewOpenMappingToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			OnToolchainOpenMappingToolClicked(sender, e);
		}

		private void OnSteamListClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
			var steamRoot = GetText("ToolchainSteamRootTextBox");
			if (!string.IsNullOrWhiteSpace(steamRoot) && !Directory.Exists(steamRoot))
			{
				throw new InvalidDataException("Steam Root folder not found.");
			}

			var steamRootToUse = string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot;
			var resolvedSteamRoot = Stunstick.Core.Steam.SteamInstallLocator.FindSteamRoot(steamRootToUse);
			if (resolvedSteamRoot is null && toolchainLibraryRoots.Count == 0)
			{
				throw new InvalidDataException("Steam install not found.");
			}

			if (steamRootToUse is null && resolvedSteamRoot is not null)
			{
				GetRequiredControl<TextBox>("ToolchainSteamRootTextBox").Text = resolvedSteamRoot;
			}

			var presets = ToolchainDiscovery.DiscoverSteamPresets(resolvedSteamRoot, toolchainLibraryRoots);

				toolchainAllPresets = presets
					.Select(preset => new ToolchainPresetListItem(preset, resolvedSteamRoot ?? steamRootToUse))
					.ToArray();

				UpdateToolchainPresetList();
				UpdateWorkshopPublishGamePresetList();

			if (resolvedSteamRoot is not null)
			{
				var libraries = Stunstick.Core.Steam.SteamLibraryScanner.GetLibraryRoots(resolvedSteamRoot);
				AppendLog($"Steam library roots: {libraries.Count}");
				foreach (var root in libraries)
				{
					AppendLog($"  {root}");
				}
			}
			if (toolchainLibraryRoots.Count > 0)
			{
				AppendLog($"Extra library roots: {toolchainLibraryRoots.Count}");
				foreach (var root in toolchainLibraryRoots)
				{
					AppendLog($"  {root}");
				}
			}

			AppendLog($"Found {presets.Count} Steam app(s).");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnWorkshopPublishRefreshGamesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var steamRoot = GetText("ToolchainSteamRootTextBox");
				if (!string.IsNullOrWhiteSpace(steamRoot) && !Directory.Exists(steamRoot))
				{
					throw new InvalidDataException("Steam Root folder not found.");
				}

				var steamRootToUse = string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot;
				var resolvedSteamRoot = Stunstick.Core.Steam.SteamInstallLocator.FindSteamRoot(steamRootToUse);
				if (resolvedSteamRoot is null && toolchainLibraryRoots.Count == 0)
				{
					throw new InvalidDataException("Steam install not found.");
				}

				if (steamRootToUse is null && resolvedSteamRoot is not null)
				{
					GetRequiredControl<TextBox>("ToolchainSteamRootTextBox").Text = resolvedSteamRoot;
				}

				var presets = ToolchainDiscovery.DiscoverSteamPresets(resolvedSteamRoot, toolchainLibraryRoots);
				toolchainAllPresets = presets
					.Select(preset => new ToolchainPresetListItem(preset, resolvedSteamRoot ?? steamRootToUse))
					.ToArray();

				UpdateToolchainPresetList();
				UpdateWorkshopPublishGamePresetList();

				AppendLog($"Found {presets.Count} Steam app(s).");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

	private void OnToolchainSearchTextChanged(object? sender, TextChangedEventArgs e)
	{
		UpdateToolchainPresetList();
	}

		private void UpdateToolchainPresetList()
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
			var search = GetText("ToolchainSearchTextBox");

			var items = new List<object>(capacity: toolchainCustomPresets.Count + toolchainAllPresets.Count);
			items.AddRange(toolchainCustomPresets
				.OrderBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.Select(preset => new ToolchainCustomPresetListItem(preset)));
			items.AddRange(toolchainAllPresets);

			var filtered = string.IsNullOrWhiteSpace(search)
				? items
				: items.Where(item => ToolchainItemMatchesSearch(item, search!)).ToList();

			listBox.ItemsSource = filtered;

			if (!string.IsNullOrWhiteSpace(toolchainSelectedCustomId))
			{
				var toSelect = filtered
					.OfType<ToolchainCustomPresetListItem>()
					.FirstOrDefault(i => string.Equals(i.Preset.Id, toolchainSelectedCustomId, StringComparison.Ordinal));
				if (toSelect is not null)
				{
					listBox.SelectedItem = toSelect;
					return;
				}
			}

			if (toolchainSelectedAppId is not null)
			{
				var toSelect = filtered
					.OfType<ToolchainPresetListItem>()
					.FirstOrDefault(i => i.Preset.AppId == toolchainSelectedAppId.Value);
				if (toSelect is not null)
				{
					listBox.SelectedItem = toSelect;
				}
			}
		}

		private static bool ToolchainItemMatchesSearch(object item, string search)
		{
			if (item is ToolchainPresetListItem steam)
			{
				return steam.Preset.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
					steam.Preset.AppId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
			}

			if (item is ToolchainCustomPresetListItem custom)
			{
				var name = custom.Preset.Name ?? string.Empty;
				return name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
					"custom".Contains(search, StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}

		private void UpdateToolchainLibraryUi()
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainLibraryRootsListBox");
			listBox.ItemsSource = toolchainLibraryRoots
				.Select(path => path.Trim())
				.Where(p => !string.IsNullOrWhiteSpace(p))
				.OrderBy(p => p, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
				.ToList();

			var removeButton = GetRequiredControl<Button>("ToolchainLibraryRootRemoveButton");
			removeButton.IsEnabled = listBox.SelectedItem is string;
		}

		private void UpdateToolchainMacroUi()
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainMacrosListBox");
			var selectedName = (listBox.SelectedItem as ToolchainMacro)?.Name;

			listBox.ItemsSource = toolchainMacros
				.Where(m => !string.IsNullOrWhiteSpace(m.Name))
				.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (!string.IsNullOrWhiteSpace(selectedName))
			{
				var match = toolchainMacros.FirstOrDefault(m => string.Equals(m.Name, selectedName, StringComparison.OrdinalIgnoreCase));
				if (match is not null)
				{
					listBox.SelectedItem = match;
				}
			}

			var deleteButton = GetRequiredControl<Button>("ToolchainMacroDeleteButton");
			deleteButton.IsEnabled = listBox.SelectedItem is ToolchainMacro;
		}

		private void ReloadToolchainSelection()
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
			if (listBox.SelectedItem is ToolchainPresetListItem item)
			{
				PopulateToolchainDetails(item);
				return;
			}

			if (listBox.SelectedItem is ToolchainCustomPresetListItem customItem)
			{
				PopulateToolchainDetails(customItem);
				return;
			}
		}

		private void OnToolchainSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
			if (listBox.SelectedItem is ToolchainPresetListItem item)
			{
				toolchainSelectedAppId = item.Preset.AppId;
				toolchainSelectedCustomId = null;
				PopulateToolchainDetails(item);
				return;
			}

			if (listBox.SelectedItem is ToolchainCustomPresetListItem customItem)
			{
				toolchainSelectedCustomId = customItem.Preset.Id;
				toolchainSelectedAppId = null;
				PopulateToolchainDetails(customItem);
				return;
			}

			toolchainSelectedAppId = null;
			toolchainSelectedCustomId = null;
			ClearToolchainDetails();
		}

		private void OnToolchainLibraryRootSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainLibraryRootsListBox");
			var removeButton = GetRequiredControl<Button>("ToolchainLibraryRootRemoveButton");
			removeButton.IsEnabled = listBox.SelectedItem is string;
		}

		private async void OnToolchainLibraryRootAddClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var path = await BrowseFolderAsync("Select Steam library folder").ConfigureAwait(true);
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}

				var fullPath = Path.GetFullPath(path);
				if (!Directory.Exists(fullPath))
				{
					throw new DirectoryNotFoundException($"Folder not found: \"{fullPath}\".");
				}

				var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
				if (toolchainLibraryRoots.Contains(fullPath, comparer))
				{
					AppendLog("Library root already added.");
					return;
				}

				toolchainLibraryRoots.Add(fullPath);
				UpdateToolchainLibraryUi();
				SaveSettings();
				AppendLog($"Added library root: {fullPath}. Click List to rescan Steam games.");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnToolchainLibraryRootRemoveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainLibraryRootsListBox");
				if (listBox.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
				{
					return;
				}

				var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
				toolchainLibraryRoots.RemoveAll(p => comparer.Equals(p, selected));
				UpdateToolchainLibraryUi();
				SaveSettings();
				AppendLog($"Removed library root: {selected}. Click List to rescan Steam games.");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnToolchainMacroSelectionChanged(object? sender, SelectionChangedEventArgs e)
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainMacrosListBox");
			var macro = listBox.SelectedItem as ToolchainMacro;

			GetRequiredControl<TextBox>("ToolchainMacroNameTextBox").Text = macro?.Name ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainMacroPathTextBox").Text = macro?.Path ?? string.Empty;

			var deleteButton = GetRequiredControl<Button>("ToolchainMacroDeleteButton");
			deleteButton.IsEnabled = macro is not null;
		}

		private async void OnToolchainMacroBrowseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select folder for macro").ConfigureAwait(true);
			if (string.IsNullOrWhiteSpace(path))
			{
				return;
			}

			GetRequiredControl<TextBox>("ToolchainMacroPathTextBox").Text = path;
		}

		private void OnToolchainMacroNewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var listBox = GetRequiredControl<ListBox>("ToolchainMacrosListBox");
			listBox.SelectedItem = null;
			GetRequiredControl<TextBox>("ToolchainMacroNameTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ToolchainMacroPathTextBox").Text = string.Empty;
			GetRequiredControl<Button>("ToolchainMacroDeleteButton").IsEnabled = false;
		}

		private void OnToolchainMacroDeleteClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainMacrosListBox");
				if (listBox.SelectedItem is not ToolchainMacro macro)
				{
					return;
				}

				toolchainMacros.Remove(macro);
				UpdateToolchainMacroUi();
				OnToolchainMacroNewClicked(sender, e);
				SaveSettings();
				ReloadToolchainSelection();
				AppendLog($"Deleted macro: {macro.Name}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnToolchainMacroSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var name = GetText("ToolchainMacroNameTextBox").Trim();
				if (string.IsNullOrWhiteSpace(name))
				{
					throw new InvalidDataException("Enter a macro name.");
				}

				if (!Regex.IsMatch(name, "^[A-Za-z0-9_]+$"))
				{
					throw new InvalidDataException("Macro name can only contain letters, numbers, and underscores.");
				}

				var path = GetText("ToolchainMacroPathTextBox").Trim();
				if (string.IsNullOrWhiteSpace(path))
				{
					throw new InvalidDataException("Enter a macro path.");
				}

				var fullPath = Path.GetFullPath(path);
				if (!Directory.Exists(fullPath))
				{
					throw new DirectoryNotFoundException($"Folder not found: \"{fullPath}\".");
				}

				var existing = toolchainMacros.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
				if (existing is null)
				{
					toolchainMacros.Add(new ToolchainMacro { Name = name, Path = fullPath });
				}
				else
				{
					existing.Name = name;
					existing.Path = fullPath;
				}

				UpdateToolchainMacroUi();
				SaveSettings();
				ReloadToolchainSelection();
				AppendLog($"Saved macro: $({name}) -> {fullPath}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}
		private void PopulateToolchainDetails(ToolchainPresetListItem item)
		{
			SetToolchainDetailsEditMode(isCustom: false);

			var preset = item.Preset;
			var steamRoot = item.SteamRoot;
			var installDir = preset.GameDirectory;

		GetRequiredControl<TextBox>("ToolchainSelectedNameTextBox").Text = preset.Name;
		GetRequiredControl<TextBox>("ToolchainSelectedAppIdTextBox").Text = preset.AppId.ToString();
		GetRequiredControl<TextBox>("ToolchainSelectedInstallDirTextBox").Text = installDir;

		var overrides = toolchainOverrides.FirstOrDefault(o => o.AppId == preset.AppId);
		var overrideGameDir = ExpandToolchainPathTemplate(overrides?.GameDirectory, steamRoot, installDir, toolchainMacros);
		var engine = overrides?.GameEngine ?? preset.GameEngine;

		var candidates = ToolchainDiscovery.FindGameDirectoryCandidates(installDir);
		var selectedGameDir = !string.IsNullOrWhiteSpace(overrideGameDir)
			? overrideGameDir
			: candidates.FirstOrDefault();

		SetToolchainEngine(engine);
		SetToolchainGameDirCandidates(candidates, selectedGameDir);

		var studioMdl = ExpandToolchainPathTemplate(overrides?.StudioMdlPath, steamRoot, installDir, toolchainMacros) ?? preset.StudioMdlPath;
		var goldSrcStudioMdl = ExpandToolchainPathTemplate(overrides?.GoldSrcStudioMdlPath, steamRoot, installDir, toolchainMacros) ?? preset.GoldSrcStudioMdlPath;
		var source2StudioMdl = ExpandToolchainPathTemplate(overrides?.Source2StudioMdlPath, steamRoot, installDir, toolchainMacros) ?? preset.Source2StudioMdlPath;
		var hlmv = ExpandToolchainPathTemplate(overrides?.HlmvPath, steamRoot, installDir, toolchainMacros) ?? preset.HlmvPath;
		var hammer = ExpandToolchainPathTemplate(overrides?.HammerPath, steamRoot, installDir, toolchainMacros) ?? preset.HammerPath ?? ToolchainDiscovery.FindHammerPath(installDir);
		var vpkTool = ExpandToolchainPathTemplate(overrides?.VpkToolPath, steamRoot, installDir, toolchainMacros) ?? preset.VpkToolPath ?? ToolchainDiscovery.FindVpkPath(installDir);
		var gmad = ExpandToolchainPathTemplate(overrides?.GmadPath, steamRoot, installDir, toolchainMacros) ?? preset.GmadPath ?? ToolchainDiscovery.FindGmadPath(installDir);
		var packerTool = ExpandToolchainPathTemplate(overrides?.PackerToolPath, steamRoot, installDir, toolchainMacros)
			?? preset.PackerToolPath
			?? vpkTool
			?? gmad;

		if (string.IsNullOrWhiteSpace(studioMdl) && engine == ToolchainGameEngine.Source)
		{
			studioMdl = ToolchainDiscovery.FindBundledStudioMdlPath();
		}

		GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = studioMdl ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainGoldSrcStudioMdlTextBox").Text = goldSrcStudioMdl ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainSource2StudioMdlTextBox").Text = source2StudioMdl ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainHlmvTextBox").Text = hlmv ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainHammerTextBox").Text = hammer ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainPackerToolTextBox").Text = packerTool ?? string.Empty;
		GetRequiredControl<TextBox>("ToolchainVpkToolTextBox").Text = vpkTool ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainGmadTextBox").Text = gmad ?? string.Empty;

			AppendLog($"Selected game: {preset.AppId} — {preset.Name}");
			if (candidates.Count == 0)
			{
				if (engine == ToolchainGameEngine.Unknown)
				{
					AppendLog("Note: No Source game folders found (no gameinfo.txt/gameinfo.gi/liblist.gam). This may not be a Source engine game.");
				}
				else
				{
					AppendLog("Note: No game folders found (no gameinfo.txt). Pick a Game Dir manually if you want to use -game.");
				}
			}
			else
			{
				var anyGameInfoTxt = candidates.Any(p => File.Exists(Path.Combine(p, "gameinfo.txt")));
				var anyGameInfoGi = candidates.Any(p => File.Exists(Path.Combine(p, "gameinfo.gi")));
				if (!anyGameInfoTxt && anyGameInfoGi)
			{
				AppendLog("Note: This looks like a Source 2 install (gameinfo.gi). Source 2 workflows are not supported yet.");
			}
		}
		if (string.IsNullOrWhiteSpace(studioMdl))
		{
			AppendLog("Note: StudioMDL not found for this install; set it manually or verify the install.");
		}
		if (string.IsNullOrWhiteSpace(hlmv))
		{
			AppendLog("Note: HLMV not found for this install; set it manually or verify the install.");
		}
		if (string.IsNullOrWhiteSpace(hammer))
		{
			AppendLog("Note: Hammer not found for this install; set it manually or verify the install.");
			}
		}

		private void PopulateToolchainDetails(ToolchainCustomPresetListItem item)
		{
			SetToolchainDetailsEditMode(isCustom: true);

			var preset = item.Preset;
			var steamRoot = GetText("ToolchainSteamRootTextBox");

			var name = string.IsNullOrWhiteSpace(preset.Name) ? "Custom preset" : preset.Name!.Trim();

			var installDir = ExpandToolchainPathTemplate(preset.InstallDirectory, steamRoot, installDir: null, toolchainMacros) ?? string.Empty;
			string? installDirFull = null;
			if (!string.IsNullOrWhiteSpace(installDir))
			{
				try
				{
					installDirFull = Path.GetFullPath(installDir);
				}
				catch
				{
					installDirFull = installDir;
				}
			}

			GetRequiredControl<TextBox>("ToolchainSelectedNameTextBox").Text = name;
			GetRequiredControl<TextBox>("ToolchainSelectedAppIdTextBox").Text = string.Empty;
			GetRequiredControl<TextBox>("ToolchainSelectedInstallDirTextBox").Text = installDir;

			var engine = preset.GameEngine;

			var candidates = !string.IsNullOrWhiteSpace(installDirFull) && Directory.Exists(installDirFull)
				? ToolchainDiscovery.FindGameDirectoryCandidates(installDirFull)
				: Array.Empty<string>();

			var selectedGameDir = ExpandToolchainPathTemplate(preset.GameDirectory, steamRoot, installDirFull, toolchainMacros);
			if (string.IsNullOrWhiteSpace(selectedGameDir))
			{
				selectedGameDir = candidates.FirstOrDefault();
			}

			SetToolchainEngine(engine);
			SetToolchainGameDirCandidates(candidates, selectedGameDir);

			var studioMdl = ExpandToolchainPathTemplate(preset.StudioMdlPath, steamRoot, installDirFull, toolchainMacros);
			var hlmv = ExpandToolchainPathTemplate(preset.HlmvPath, steamRoot, installDirFull, toolchainMacros);
			var hammer = ExpandToolchainPathTemplate(preset.HammerPath, steamRoot, installDirFull, toolchainMacros);
			var packerTool = ExpandToolchainPathTemplate(preset.PackerToolPath, steamRoot, installDirFull, toolchainMacros);
			var vpkTool = ExpandToolchainPathTemplate(preset.VpkToolPath, steamRoot, installDirFull, toolchainMacros);
			var gmad = ExpandToolchainPathTemplate(preset.GmadPath, steamRoot, installDirFull, toolchainMacros);

			if (string.IsNullOrWhiteSpace(studioMdl) && !string.IsNullOrWhiteSpace(installDirFull))
			{
				studioMdl = ToolchainDiscovery.FindStudioMdlPath(installDirFull);
			}
			if (string.IsNullOrWhiteSpace(studioMdl) && engine == ToolchainGameEngine.Source)
			{
				studioMdl = ToolchainDiscovery.FindBundledStudioMdlPath();
			}
			if (string.IsNullOrWhiteSpace(hlmv) && !string.IsNullOrWhiteSpace(installDirFull))
			{
				hlmv = ToolchainDiscovery.FindHlmvPath(installDirFull);
			}
			if (string.IsNullOrWhiteSpace(hammer) && !string.IsNullOrWhiteSpace(installDirFull))
			{
				hammer = ToolchainDiscovery.FindHammerPath(installDirFull);
			}
			if (string.IsNullOrWhiteSpace(packerTool))
			{
				packerTool = vpkTool ?? gmad;
			}
			if (string.IsNullOrWhiteSpace(vpkTool) && !string.IsNullOrWhiteSpace(installDirFull))
			{
				vpkTool = ToolchainDiscovery.FindVpkPath(installDirFull);
			}
			if (string.IsNullOrWhiteSpace(gmad) && !string.IsNullOrWhiteSpace(installDirFull))
			{
				gmad = ToolchainDiscovery.FindGmadPath(installDirFull);
			}

			GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = studioMdl ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainHlmvTextBox").Text = hlmv ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainHammerTextBox").Text = hammer ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainPackerToolTextBox").Text = packerTool ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainVpkToolTextBox").Text = vpkTool ?? string.Empty;
			GetRequiredControl<TextBox>("ToolchainGmadTextBox").Text = gmad ?? string.Empty;

			AppendLog($"Selected custom preset: {name}");
			if (string.IsNullOrWhiteSpace(installDir))
			{
				AppendLog("Note: Set an Install Dir to enable game folder discovery.");
			}
		}

		private void ClearToolchainDetails()
		{
		GetRequiredControl<TextBox>("ToolchainSelectedNameTextBox").Text = string.Empty;
		GetRequiredControl<TextBox>("ToolchainSelectedAppIdTextBox").Text = string.Empty;
		GetRequiredControl<TextBox>("ToolchainSelectedInstallDirTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainGoldSrcStudioMdlTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainSource2StudioMdlTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainHlmvTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainHammerTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainPackerToolTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainVpkToolTextBox").Text = string.Empty;
	GetRequiredControl<TextBox>("ToolchainGmadTextBox").Text = string.Empty;
		var engineCombo = GetRequiredControl<ComboBox>("ToolchainEngineComboBox");
		engineCombo.SelectedItem = null;
		var combo = GetRequiredControl<ComboBox>("ToolchainGameDirComboBox");
			combo.ItemsSource = Array.Empty<string>();
			combo.SelectedItem = null;

			SetToolchainDetailsEditMode(isCustom: false);
		}

		private void SetToolchainDetailsEditMode(bool isCustom)
		{
			GetRequiredControl<TextBox>("ToolchainSelectedNameTextBox").IsReadOnly = !isCustom;
			GetRequiredControl<TextBox>("ToolchainSelectedInstallDirTextBox").IsReadOnly = !isCustom;

			var installBrowse = GetRequiredControl<Button>("ToolchainInstallDirBrowseButton");
			installBrowse.IsVisible = isCustom;

			var deleteCustom = GetRequiredControl<Button>("ToolchainDeleteCustomPresetButton");
			deleteCustom.IsEnabled = isCustom;
		}

		private async void OnBrowseToolchainInstallDirClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is not ToolchainCustomPresetListItem)
				{
					throw new InvalidDataException("Select a custom preset first.");
				}

				var path = await BrowseFolderAsync("Select install folder").ConfigureAwait(true);
				if (string.IsNullOrWhiteSpace(path))
				{
					return;
				}

				GetRequiredControl<TextBox>("ToolchainSelectedInstallDirTextBox").Text = path;

				string? installDirFull = null;
				try
				{
					installDirFull = Path.GetFullPath(path);
				}
				catch
				{
					installDirFull = path;
				}

				if (!string.IsNullOrWhiteSpace(installDirFull) && Directory.Exists(installDirFull))
				{
					var detectedEngine = ToolchainDiscovery.DetectEngine(installDirFull);
					SetToolchainEngine(detectedEngine);
				}

				var candidates = !string.IsNullOrWhiteSpace(installDirFull) && Directory.Exists(installDirFull)
					? ToolchainDiscovery.FindGameDirectoryCandidates(installDirFull)
					: Array.Empty<string>();

				var selectedGameDir = GetComboBoxText("ToolchainGameDirComboBox");
				if (string.IsNullOrWhiteSpace(selectedGameDir))
				{
					selectedGameDir = candidates.FirstOrDefault();
				}

				SetToolchainGameDirCandidates(candidates, selectedGameDir);

				var studiomdl = GetText("ToolchainStudioMdlTextBox");
				if (!string.IsNullOrWhiteSpace(studiomdl) && !studiomdl.Contains("$(", StringComparison.Ordinal) && !File.Exists(studiomdl))
				{
					studiomdl = string.Empty;
				}

				var hlmv = GetText("ToolchainHlmvTextBox");
				if (!string.IsNullOrWhiteSpace(hlmv) && !hlmv.Contains("$(", StringComparison.Ordinal) && !File.Exists(hlmv))
				{
					hlmv = string.Empty;
				}

				var hammer = GetText("ToolchainHammerTextBox");
				if (!string.IsNullOrWhiteSpace(hammer) && !hammer.Contains("$(", StringComparison.Ordinal) && !File.Exists(hammer))
				{
					hammer = string.Empty;
				}
				var vpkTool = GetText("ToolchainVpkToolTextBox");
				if (!string.IsNullOrWhiteSpace(vpkTool) && !vpkTool.Contains("$(", StringComparison.Ordinal) && !File.Exists(vpkTool))
				{
					vpkTool = string.Empty;
				}
				var gmad = GetText("ToolchainGmadTextBox");
				if (!string.IsNullOrWhiteSpace(gmad) && !gmad.Contains("$(", StringComparison.Ordinal) && !File.Exists(gmad))
				{
					gmad = string.Empty;
				}
				var packerTool = GetText("ToolchainPackerToolTextBox");
				if (!string.IsNullOrWhiteSpace(packerTool) && !packerTool.Contains("$(", StringComparison.Ordinal) && !File.Exists(packerTool))
				{
					packerTool = string.Empty;
				}

				if (!string.IsNullOrWhiteSpace(installDirFull) && Directory.Exists(installDirFull))
				{
					if (string.IsNullOrWhiteSpace(studiomdl))
					{
						studiomdl = ToolchainDiscovery.FindStudioMdlPath(installDirFull) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = studiomdl;
					}
					if (string.IsNullOrWhiteSpace(hlmv))
					{
						hlmv = ToolchainDiscovery.FindHlmvPath(installDirFull) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainHlmvTextBox").Text = hlmv;
					}
					if (string.IsNullOrWhiteSpace(hammer))
					{
						hammer = ToolchainDiscovery.FindHammerPath(installDirFull) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainHammerTextBox").Text = hammer;
					}
					if (string.IsNullOrWhiteSpace(vpkTool))
					{
						vpkTool = ToolchainDiscovery.FindVpkPath(installDirFull) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainVpkToolTextBox").Text = vpkTool;
					}
					if (string.IsNullOrWhiteSpace(gmad))
					{
						gmad = ToolchainDiscovery.FindGmadPath(installDirFull) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainGmadTextBox").Text = gmad;
					}
					if (string.IsNullOrWhiteSpace(packerTool))
					{
						packerTool = FirstNonEmpty(vpkTool, gmad) ?? string.Empty;
						GetRequiredControl<TextBox>("ToolchainPackerToolTextBox").Text = packerTool;
					}
				}
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnToolchainNewCustomPresetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var preset = new ToolchainCustomPreset
				{
					Name = GetUniqueToolchainCustomPresetName($"Custom preset {toolchainCustomPresets.Count + 1}")
				};

				toolchainCustomPresets.Add(preset);
				toolchainSelectedCustomId = preset.Id;
				toolchainSelectedAppId = null;

				SaveSettings();
				UpdateToolchainPresetList();
				AppendLog($"Created custom preset: {preset.Name}");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnToolchainCloneAsCustomPresetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is null)
				{
					throw new InvalidDataException("Select a game first.");
				}

				var baseName = GetText("ToolchainSelectedNameTextBox");
				baseName = string.IsNullOrWhiteSpace(baseName) ? "Custom preset" : baseName.Trim();

				var cloneName = GetUniqueToolchainCustomPresetName($"{baseName} (copy)");
				GetRequiredControl<TextBox>("ToolchainSelectedNameTextBox").Text = cloneName;

				var preset = new ToolchainCustomPreset();
				toolchainCustomPresets.Add(preset);
				toolchainSelectedCustomId = preset.Id;
				toolchainSelectedAppId = null;

				try
				{
					SaveToolchainCustomPreset(preset);
				}
				catch
				{
					toolchainCustomPresets.RemoveAll(p => p.Id == preset.Id);
					toolchainSelectedCustomId = null;
					throw;
				}
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnToolchainDeleteCustomPresetClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is not ToolchainCustomPresetListItem item)
				{
					AppendLog("Select a custom preset first.");
					return;
				}

				var name = string.IsNullOrWhiteSpace(item.Preset.Name) ? "Custom preset" : item.Preset.Name!.Trim();
				var confirm = new ConfirmWindow($"Delete custom preset \"{name}\"?");
				var ok = await confirm.ShowDialog<bool>(this);
				if (!ok)
				{
					return;
				}

				var removed = toolchainCustomPresets.RemoveAll(p => p.Id == item.Preset.Id);
				toolchainSelectedCustomId = null;
				toolchainSelectedAppId = null;

				SaveSettings();
				UpdateToolchainPresetList();
				ClearToolchainDetails();

				AppendLog(removed > 0 ? $"Deleted custom preset: {name}" : "Custom preset not found.");
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private string GetUniqueToolchainCustomPresetName(string desiredName)
		{
			var baseName = string.IsNullOrWhiteSpace(desiredName) ? "Custom preset" : desiredName.Trim();

			var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			var existingNames = toolchainCustomPresets
				.Select(p => p.Name)
				.Where(name => !string.IsNullOrWhiteSpace(name))
				.Select(name => name!.Trim())
				.ToHashSet(comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			if (!existingNames.Contains(baseName))
			{
				return baseName;
			}

			var suffix = 2;
			while (true)
			{
				var candidate = $"{baseName} {suffix}";
				if (!existingNames.Contains(candidate))
				{
					return candidate;
				}

				suffix++;
			}
		}

	private void SetToolchainGameDirCandidates(IReadOnlyList<string> candidates, string? selected)
	{
		var combo = GetRequiredControl<ComboBox>("ToolchainGameDirComboBox");

	var items = new List<string>();
	if (candidates.Count > 0)
	{
		items.AddRange(candidates);
	}

	if (!string.IsNullOrWhiteSpace(selected) &&
		!items.Contains(selected, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
	{
		items.Insert(0, selected);
	}

	combo.ItemsSource = items;
	combo.SelectedItem = string.IsNullOrWhiteSpace(selected) ? null : selected;
}

	private void SetToolchainEngine(ToolchainGameEngine? engine)
	{
		var combo = GetRequiredControl<ComboBox>("ToolchainEngineComboBox");
		if (engine is null)
		{
			combo.SelectedItem = null;
			return;
		}

		var value = engine.Value.ToString();
		var match = combo.Items?.Cast<object?>()
			.FirstOrDefault(i => string.Equals(i?.ToString(), value, StringComparison.OrdinalIgnoreCase));

		combo.SelectedItem = match ?? value;
	}

	private ToolchainGameEngine GetSelectedToolchainEngineOrDefault()
	{
		var combo = GetRequiredControl<ComboBox>("ToolchainEngineComboBox");
		var selected = combo.SelectedItem?.ToString();
		if (Enum.TryParse<ToolchainGameEngine>(selected, out var engine))
		{
			return engine;
		}

		return ToolchainGameEngine.Source;
	}

	private static string? ExpandToolchainPathTemplate(
		string? value,
		string? steamRoot,
		string? installDir,
		IReadOnlyList<ToolchainMacro>? macros = null)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var result = value.Trim();
		if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
		{
			result = result.Replace("$(AppDir)", AppContext.BaseDirectory, StringComparison.Ordinal);
		}
		if (!string.IsNullOrWhiteSpace(steamRoot))
		{
			result = result.Replace("$(SteamRoot)", steamRoot, StringComparison.Ordinal);
		}
		if (!string.IsNullOrWhiteSpace(installDir))
		{
			result = result.Replace("$(InstallDir)", installDir, StringComparison.Ordinal);
		}

		if (macros is not null)
		{
			foreach (var macro in macros)
			{
				if (string.IsNullOrWhiteSpace(macro?.Name) || string.IsNullOrWhiteSpace(macro.Path))
				{
					continue;
				}

				result = result.Replace($"$({macro.Name})", macro.Path, StringComparison.Ordinal);
			}
		}

		return result;
	}

	private static string? CollapseToolchainPathTemplate(
		string? value,
		string? steamRoot,
		string? installDir,
		IReadOnlyList<ToolchainMacro>? macros = null)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		string fullPath;
		try
		{
			fullPath = Path.GetFullPath(value.Trim());
		}
		catch
		{
			return value.Trim();
		}

		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		if (macros is not null)
		{
			foreach (var macro in macros)
			{
				if (string.IsNullOrWhiteSpace(macro?.Name) || string.IsNullOrWhiteSpace(macro.Path))
				{
					continue;
				}

				try
				{
					var macroFull = Path.GetFullPath(macro.Path);
					if (IsSubPathOf(fullPath, macroFull, comparison))
					{
						return "$(" + macro.Name + ")" + fullPath[macroFull.Length..];
					}
				}
				catch
				{
				}
			}
		}

		try
		{
			var appDirFull = Path.GetFullPath(AppContext.BaseDirectory);
			if (IsSubPathOf(fullPath, appDirFull, comparison))
			{
				return "$(AppDir)" + fullPath[appDirFull.Length..];
			}
		}
		catch
		{
		}

		if (!string.IsNullOrWhiteSpace(installDir))
		{
			try
			{
				var installFull = Path.GetFullPath(installDir);
				if (IsSubPathOf(fullPath, installFull, comparison))
				{
					return "$(InstallDir)" + fullPath[installFull.Length..];
				}
			}
			catch
			{
			}
		}

		if (!string.IsNullOrWhiteSpace(steamRoot))
		{
			try
			{
				var steamFull = Path.GetFullPath(steamRoot);
				if (IsSubPathOf(fullPath, steamFull, comparison))
				{
					return "$(SteamRoot)" + fullPath[steamFull.Length..];
				}
			}
			catch
			{
			}
		}

		return fullPath;
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		foreach (var value in values)
		{
			if (!string.IsNullOrWhiteSpace(value))
			{
				return value;
			}
		}

		return null;
	}

	private static bool IsSubPathOf(string path, string root, StringComparison comparison)
	{
		var normalizedRoot = root;
		if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
		{
			normalizedRoot += Path.DirectorySeparatorChar;
		}

		return path.StartsWith(normalizedRoot, comparison);
	}

	private void OnToolchainGameDirSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		// Intentionally no-op for now. User applies via the buttons below.
	}

	private async void OnBrowseToolchainGameDirClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select game folder (contains gameinfo.txt)").ConfigureAwait(true);
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		var combo = GetRequiredControl<ComboBox>("ToolchainGameDirComboBox");
		var existing = combo.ItemsSource as IEnumerable<string> ?? Array.Empty<string>();
		var items = existing.ToList();
		if (!items.Contains(path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
		{
			items.Insert(0, path);
			combo.ItemsSource = items;
		}

		combo.SelectedItem = path;
	}

	private async void OnBrowseToolchainStudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select StudioMDL",
			fileTypes: new[]
			{
				new FilePickerFileType("StudioMDL") { Patterns = new[] { "studiomdl*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = path;
		}
	}

	private void OnToolchainUseBundledStudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var engine = GetSelectedToolchainEngineOrDefault();
			if (engine != ToolchainGameEngine.Source)
			{
				AppendLog("Bundled StudioMDL (MDLForge) targets Source 1. Set Engine=Source to use it.");
				return;
			}

			var bundled = ToolchainDiscovery.FindBundledStudioMdlPath();
			if (string.IsNullOrWhiteSpace(bundled))
			{
				AppendLog("Bundled StudioMDL not found. Build it via scripts/toolchain/build_studiomdl.sh (or build_studiomdl.ps1 on Windows).");
				return;
			}

			GetRequiredControl<TextBox>("ToolchainStudioMdlTextBox").Text = bundled;
			AppendLog($"Toolchain: using bundled StudioMDL: {bundled}");
		}
		catch (Exception ex)
		{
			AppendLog($"Toolchain: failed to set bundled StudioMDL: {ex.Message}");
		}
	}

	private async void OnBrowseToolchainHlmvClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select HLMV",
			fileTypes: new[]
			{
				new FilePickerFileType("HLMV") { Patterns = new[] { "hlmv*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainHlmvTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainHammerClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select Hammer",
			fileTypes: new[]
			{
				new FilePickerFileType("Hammer") { Patterns = new[] { "hammer*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainHammerTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainGoldSrcStudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select GoldSrc StudioMDL",
			fileTypes: new[]
			{
				new FilePickerFileType("StudioMDL") { Patterns = new[] { "studiomdl*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainGoldSrcStudioMdlTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainSource2StudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select Source 2 resourcecompiler",
			fileTypes: new[]
			{
				new FilePickerFileType("resourcecompiler") { Patterns = new[] { "resourcecompiler*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainSource2StudioMdlTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainPackerToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select packer tool (VPK/GMAD)",
			fileTypes: new[]
			{
				new FilePickerFileType("VPK/GMAD") { Patterns = new[] { "vpk*", "gmad*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainPackerToolTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainVpkToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select VPK tool",
			fileTypes: new[]
			{
				new FilePickerFileType("VPK tool") { Patterns = new[] { "vpk*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainVpkToolTextBox").Text = path;
		}
	}

	private async void OnBrowseToolchainGmadClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select GMAD",
			fileTypes: new[]
			{
				new FilePickerFileType("GMAD") { Patterns = new[] { "gmad*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			}).ConfigureAwait(true);

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainGmadTextBox").Text = path;
		}
	}

	private async void OnToolchainOpenMappingToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		await RunOperationAsync(async (progress, cancellationToken) =>
		{
			progress.Report(new StunstickProgress("Mapping", 0, 0, Message: "Launching..."));

			var hammerPath = GetText("ToolchainHammerTextBox");
			RequireFileExists(hammerPath, "Hammer");

			var gameDir = GetComboBoxText("ToolchainGameDirComboBox") ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(gameDir) && !Directory.Exists(gameDir))
			{
				throw new DirectoryNotFoundException($"Game Dir not found: \"{gameDir}\".");
			}

			var args = new List<string>();
			if (!string.IsNullOrWhiteSpace(gameDir))
			{
				args.Add("-game");
				args.Add(gameDir);
			}

			var winePrefix = GetText("CompileWinePrefixTextBox");
			var wineCmd = GetText("CompileWineCommandTextBox");

			var launcher = new ToolchainLauncher(new SystemProcessLauncher());
			_ = await launcher.LaunchExternalToolAsync(
				toolPath: hammerPath,
				toolArguments: args,
				wineOptions: new WineOptions(
					Prefix: string.IsNullOrWhiteSpace(winePrefix) ? null : winePrefix,
					WineCommand: string.IsNullOrWhiteSpace(wineCmd) ? "wine" : wineCmd),
				steamRootOverride: null,
				waitForExit: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			AppendLog("Mapping tool launched.");
		});
	}

			private void OnToolchainUseForCompileClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				try
				{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is not ToolchainPresetListItem && listBox.SelectedItem is not ToolchainCustomPresetListItem)
				{
					throw new InvalidDataException("Select a game first.");
				}

				var appIdText = GetText("ToolchainSelectedAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(appIdText))
				{
					SetText("CompileSteamAppIdTextBox", appIdText);
				}
				else
				{
					SetText("CompileSteamAppIdTextBox", string.Empty);
				}

				var steamRoot = GetText("ToolchainSteamRootTextBox");
				if (!string.IsNullOrWhiteSpace(steamRoot))
				{
					SetText("CompileSteamRootTextBox", steamRoot);
				}

				var engine = GetSelectedToolchainEngineOrDefault();
				var gameDir = GetComboBoxText("ToolchainGameDirComboBox");
				if (!string.IsNullOrWhiteSpace(gameDir))
				{
				SetText("CompileGameDirTextBox", gameDir);
			}

			var studiomdl = GetText("ToolchainStudioMdlTextBox");
			var goldSrc = GetText("ToolchainGoldSrcStudioMdlTextBox");
			var source2 = GetText("ToolchainSource2StudioMdlTextBox");

			var compileTool = engine switch
			{
				ToolchainGameEngine.GoldSrc when !string.IsNullOrWhiteSpace(goldSrc) => goldSrc,
				ToolchainGameEngine.Source2 when !string.IsNullOrWhiteSpace(source2) => source2,
				_ => studiomdl
			};

				if (string.IsNullOrWhiteSpace(compileTool))
				{
					compileTool = !string.IsNullOrWhiteSpace(studiomdl) ? studiomdl : (!string.IsNullOrWhiteSpace(goldSrc) ? goldSrc : source2);
				}

				if (!string.IsNullOrWhiteSpace(compileTool))
				{
					SetText("CompileStudioMdlTextBox", compileTool);
				}

				SelectMainTab("CompileTabItem");
				AppendLog($"Applied selected game to Compile tab (Engine: {engine}).");
			}
			catch (Exception ex)
			{
			AppendLog(ex.Message);
		}
	}

			private void OnToolchainUseForViewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				try
				{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is not ToolchainPresetListItem && listBox.SelectedItem is not ToolchainCustomPresetListItem)
				{
					throw new InvalidDataException("Select a game first.");
				}

				var appIdText = GetText("ToolchainSelectedAppIdTextBox");
				if (!string.IsNullOrWhiteSpace(appIdText))
				{
					SetText("ViewSteamAppIdTextBox", appIdText);
				}
				else
				{
					SetText("ViewSteamAppIdTextBox", string.Empty);
				}

				var steamRoot = GetText("ToolchainSteamRootTextBox");
				if (!string.IsNullOrWhiteSpace(steamRoot))
				{
					SetText("ViewSteamRootTextBox", steamRoot);
				}

				var gameDir = GetComboBoxText("ToolchainGameDirComboBox");
				if (!string.IsNullOrWhiteSpace(gameDir))
				{
				SetText("ViewGameDirTextBox", gameDir);
			}

			var hlmv = GetText("ToolchainHlmvTextBox");
				if (!string.IsNullOrWhiteSpace(hlmv))
				{
					SetText("ViewHlmvTextBox", hlmv);
				}

				SelectMainTab("ViewTabItem");
				AppendLog("Applied selected game to View tab.");
			}
			catch (Exception ex)
			{
			AppendLog(ex.Message);
		}
	}

			private void OnToolchainUseForPackClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				try
				{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is not ToolchainPresetListItem && listBox.SelectedItem is not ToolchainCustomPresetListItem)
				{
					throw new InvalidDataException("Select a game first.");
				}

				var engine = GetSelectedToolchainEngineOrDefault();
				var packer = GetText("ToolchainPackerToolTextBox");
				var vpk = GetText("ToolchainVpkToolTextBox");
				var gmad = GetText("ToolchainGmadTextBox");

				string? chosen = engine switch
				{
					ToolchainGameEngine.GoldSrc => FirstNonEmpty(gmad, packer, vpk),
					ToolchainGameEngine.Source2 => FirstNonEmpty(vpk, packer, gmad),
					_ => FirstNonEmpty(vpk, packer, gmad)
				};

				if (!string.IsNullOrWhiteSpace(chosen))
				{
					SetText("PackVpkToolTextBox", chosen);
					AppendLog($"Applied packer tool to Pack tab (Engine: {engine}).");
				}
				else
				{
					AppendLog("No packer tool path set; add one in Games tab first.");
				}
			}
			catch (Exception ex)
			{
			AppendLog(ex.Message);
		}
	}

	private void OnToolchainRunGameClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var appIdText = GetText("ToolchainSelectedAppIdTextBox");
			if (string.IsNullOrWhiteSpace(appIdText) || !uint.TryParse(appIdText, out var appId))
			{
				throw new InvalidDataException("Select a Steam game first.");
			}

			TryOpenUri($"steam://rungameid/{appId}");
			AppendLog($"Requested Steam launch: {appId}");
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

		private void OnToolchainSaveOverridesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is ToolchainCustomPresetListItem customItem)
				{
					SaveToolchainCustomPreset(customItem.Preset);
					return;
				}

				if (listBox.SelectedItem is not ToolchainPresetListItem item)
				{
					throw new InvalidDataException("Select a game first.");
				}

			var preset = item.Preset;
			var steamRoot = item.SteamRoot;
			var installDir = preset.GameDirectory;

			var gameDir = GetComboBoxText("ToolchainGameDirComboBox");
			var studiomdl = GetText("ToolchainStudioMdlTextBox");
			var goldSrcStudioMdl = GetText("ToolchainGoldSrcStudioMdlTextBox");
			var source2StudioMdl = GetText("ToolchainSource2StudioMdlTextBox");
			var hlmv = GetText("ToolchainHlmvTextBox");
			var hammer = GetText("ToolchainHammerTextBox");
			var packerTool = GetText("ToolchainPackerToolTextBox");
			var vpkTool = GetText("ToolchainVpkToolTextBox");
			var gmad = GetText("ToolchainGmadTextBox");
			var engine = GetSelectedToolchainEngineOrDefault();

			if (!string.IsNullOrWhiteSpace(gameDir) && !Directory.Exists(gameDir))
			{
				throw new DirectoryNotFoundException($"Game Dir not found: \"{gameDir}\".");
			}

			if (!string.IsNullOrWhiteSpace(studiomdl) && !File.Exists(studiomdl))
			{
				throw new FileNotFoundException("StudioMDL not found.", studiomdl);
			}
			if (!string.IsNullOrWhiteSpace(goldSrcStudioMdl) && !File.Exists(goldSrcStudioMdl))
			{
				throw new FileNotFoundException("GoldSrc StudioMDL not found.", goldSrcStudioMdl);
			}
			if (!string.IsNullOrWhiteSpace(source2StudioMdl) && !File.Exists(source2StudioMdl))
			{
				throw new FileNotFoundException("Source2 resourcecompiler not found.", source2StudioMdl);
			}

			if (!string.IsNullOrWhiteSpace(hlmv) && !File.Exists(hlmv))
			{
				throw new FileNotFoundException("HLMV not found.", hlmv);
			}

			if (!string.IsNullOrWhiteSpace(hammer) && !File.Exists(hammer))
			{
				throw new FileNotFoundException("Hammer not found.", hammer);
			}
			if (!string.IsNullOrWhiteSpace(packerTool) && !File.Exists(packerTool))
			{
				throw new FileNotFoundException("Packer tool not found.", packerTool);
			}
			if (!string.IsNullOrWhiteSpace(vpkTool) && !File.Exists(vpkTool))
			{
				throw new FileNotFoundException("VPK tool not found.", vpkTool);
			}
			if (!string.IsNullOrWhiteSpace(gmad) && !File.Exists(gmad))
			{
				throw new FileNotFoundException("GMAD not found.", gmad);
			}

			var overrideEntry = toolchainOverrides.FirstOrDefault(o => o.AppId == preset.AppId);
			if (overrideEntry is null)
			{
				overrideEntry = new ToolchainPresetOverrides { AppId = preset.AppId };
				toolchainOverrides.Add(overrideEntry);
			}

			overrideEntry.GameDirectory = CollapseToolchainPathTemplate(gameDir, steamRoot, installDir, toolchainMacros);
			overrideEntry.StudioMdlPath = CollapseToolchainPathTemplate(studiomdl, steamRoot, installDir, toolchainMacros);
			overrideEntry.GoldSrcStudioMdlPath = CollapseToolchainPathTemplate(goldSrcStudioMdl, steamRoot, installDir, toolchainMacros);
			overrideEntry.Source2StudioMdlPath = CollapseToolchainPathTemplate(source2StudioMdl, steamRoot, installDir, toolchainMacros);
			overrideEntry.HlmvPath = CollapseToolchainPathTemplate(hlmv, steamRoot, installDir, toolchainMacros);
			overrideEntry.HammerPath = CollapseToolchainPathTemplate(hammer, steamRoot, installDir, toolchainMacros);
			overrideEntry.PackerToolPath = CollapseToolchainPathTemplate(packerTool, steamRoot, installDir, toolchainMacros);
			overrideEntry.VpkToolPath = CollapseToolchainPathTemplate(vpkTool, steamRoot, installDir, toolchainMacros);
			overrideEntry.GmadPath = CollapseToolchainPathTemplate(gmad, steamRoot, installDir, toolchainMacros);
			overrideEntry.GameEngine = engine;

				SaveSettings();
				AppendLog($"Saved overrides for: {preset.AppId} — {preset.Name}");
			}
			catch (Exception ex)
			{
			AppendLog(ex.Message);
			}
		}

		private void SaveToolchainCustomPreset(ToolchainCustomPreset preset)
		{
			if (preset is null)
			{
				throw new ArgumentNullException(nameof(preset));
			}

			var steamRoot = GetText("ToolchainSteamRootTextBox");

			var name = GetText("ToolchainSelectedNameTextBox");
			name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				throw new InvalidDataException("Enter a preset name.");
			}

			var installDirText = GetText("ToolchainSelectedInstallDirTextBox");
			installDirText = string.IsNullOrWhiteSpace(installDirText) ? null : installDirText.Trim();

			string? installDirFull = null;
			if (!string.IsNullOrWhiteSpace(installDirText))
			{
				if (!installDirText.Contains("$(", StringComparison.Ordinal))
				{
					installDirFull = Path.GetFullPath(installDirText);
					if (!Directory.Exists(installDirFull))
					{
						throw new DirectoryNotFoundException($"Install Dir not found: \"{installDirFull}\".");
					}
				}
			}

			var gameDir = GetComboBoxText("ToolchainGameDirComboBox");
			if (!string.IsNullOrWhiteSpace(gameDir))
			{
				gameDir = gameDir.Trim();
				if (!gameDir.Contains("$(", StringComparison.Ordinal) && !Directory.Exists(gameDir))
				{
					throw new DirectoryNotFoundException($"Game Dir not found: \"{gameDir}\".");
				}
			}

			var studiomdl = GetText("ToolchainStudioMdlTextBox");
			var goldSrcStudioMdl = GetText("ToolchainGoldSrcStudioMdlTextBox");
			var source2StudioMdl = GetText("ToolchainSource2StudioMdlTextBox");
			var hlmv = GetText("ToolchainHlmvTextBox");
			var hammer = GetText("ToolchainHammerTextBox");
			var packerTool = GetText("ToolchainPackerToolTextBox");
			var vpkTool = GetText("ToolchainVpkToolTextBox");
			var gmad = GetText("ToolchainGmadTextBox");
			var engine = GetSelectedToolchainEngineOrDefault();

			if (!string.IsNullOrWhiteSpace(studiomdl) && !studiomdl.Contains("$(", StringComparison.Ordinal) && !File.Exists(studiomdl))
			{
				throw new FileNotFoundException("StudioMDL not found.", studiomdl);
			}
			if (!string.IsNullOrWhiteSpace(goldSrcStudioMdl) && !goldSrcStudioMdl.Contains("$(", StringComparison.Ordinal) && !File.Exists(goldSrcStudioMdl))
			{
				throw new FileNotFoundException("GoldSrc StudioMDL not found.", goldSrcStudioMdl);
			}
			if (!string.IsNullOrWhiteSpace(source2StudioMdl) && !source2StudioMdl.Contains("$(", StringComparison.Ordinal) && !File.Exists(source2StudioMdl))
			{
				throw new FileNotFoundException("Source2 resourcecompiler not found.", source2StudioMdl);
			}

			if (!string.IsNullOrWhiteSpace(hlmv) && !hlmv.Contains("$(", StringComparison.Ordinal) && !File.Exists(hlmv))
			{
				throw new FileNotFoundException("HLMV not found.", hlmv);
			}

			if (!string.IsNullOrWhiteSpace(hammer) && !hammer.Contains("$(", StringComparison.Ordinal) && !File.Exists(hammer))
			{
				throw new FileNotFoundException("Hammer not found.", hammer);
			}
			if (!string.IsNullOrWhiteSpace(packerTool) && !packerTool.Contains("$(", StringComparison.Ordinal) && !File.Exists(packerTool))
			{
				throw new FileNotFoundException("Packer tool not found.", packerTool);
			}
			if (!string.IsNullOrWhiteSpace(vpkTool) && !vpkTool.Contains("$(", StringComparison.Ordinal) && !File.Exists(vpkTool))
			{
				throw new FileNotFoundException("VPK tool not found.", vpkTool);
			}
			if (!string.IsNullOrWhiteSpace(gmad) && !gmad.Contains("$(", StringComparison.Ordinal) && !File.Exists(gmad))
			{
				throw new FileNotFoundException("GMAD not found.", gmad);
			}

			preset.Name = name;
			preset.InstallDirectory = CollapseToolchainPathTemplate(installDirText, steamRoot, installDir: null, toolchainMacros);
			preset.GameDirectory = CollapseToolchainPathTemplate(gameDir, steamRoot, installDirFull, toolchainMacros);
			preset.StudioMdlPath = CollapseToolchainPathTemplate(studiomdl, steamRoot, installDirFull, toolchainMacros);
			preset.GoldSrcStudioMdlPath = CollapseToolchainPathTemplate(goldSrcStudioMdl, steamRoot, installDirFull, toolchainMacros);
			preset.Source2StudioMdlPath = CollapseToolchainPathTemplate(source2StudioMdl, steamRoot, installDirFull, toolchainMacros);
			preset.HlmvPath = CollapseToolchainPathTemplate(hlmv, steamRoot, installDirFull, toolchainMacros);
			preset.HammerPath = CollapseToolchainPathTemplate(hammer, steamRoot, installDirFull, toolchainMacros);
			preset.PackerToolPath = CollapseToolchainPathTemplate(packerTool, steamRoot, installDirFull, toolchainMacros);
			preset.VpkToolPath = CollapseToolchainPathTemplate(vpkTool, steamRoot, installDirFull, toolchainMacros);
			preset.GmadPath = CollapseToolchainPathTemplate(gmad, steamRoot, installDirFull, toolchainMacros);
			preset.GameEngine = engine;

			SaveSettings();
			UpdateToolchainPresetList();
			AppendLog($"Saved custom preset: {preset.Name}");
		}

		private void OnToolchainClearOverridesClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var listBox = GetRequiredControl<ListBox>("ToolchainListBox");
				if (listBox.SelectedItem is ToolchainCustomPresetListItem customItem)
				{
					PopulateToolchainDetails(customItem);
					AppendLog("Reverted custom preset changes (reloaded from saved settings).");
					return;
				}

				if (listBox.SelectedItem is not ToolchainPresetListItem item)
				{
					throw new InvalidDataException("Select a game first.");
				}

			var removed = toolchainOverrides.RemoveAll(o => o.AppId == item.Preset.AppId);
			SaveSettings();
			PopulateToolchainDetails(item);

			AppendLog(removed > 0
				? $"Cleared overrides for: {item.Preset.AppId} — {item.Preset.Name}"
				: "No overrides were set for this game.");
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnBrowseToolchainSteamRootClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select Steam folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ToolchainSteamRootTextBox").Text = path;
		}
	}

	private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		operationCancellation?.Cancel();
	}

			private async void OnBrowseInspectPathClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				var path = await BrowseOpenFileAsync("Select file");
				if (!string.IsNullOrWhiteSpace(path))
				{
					GetRequiredControl<TextBox>("InspectPathTextBox").Text = path;
				}
			}

			private async void OnBrowseInspectPathFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				var path = await BrowseFolderAsync("Select folder");
				if (!string.IsNullOrWhiteSpace(path))
				{
					GetRequiredControl<TextBox>("InspectPathTextBox").Text = path;
				}
			}

			private async void OnBrowseWorkshopDownloadSteamRootClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
			{
				var path = await BrowseFolderAsync("Select Steam folder");
				if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopDownloadSteamRootTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopDownloadOutputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select output folder");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopDownloadOutputTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopDownloadSteamCmdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync("Select SteamCMD");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopDownloadSteamCmdTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopDownloadSteamCmdInstallClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select SteamCMD install folder");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopDownloadSteamCmdInstallTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopPublishSteamCmdClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync("Select SteamCMD");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopPublishSteamCmdTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopPublishContentClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select content folder");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopPublishContentTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopPublishPreviewClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync(
				"Select preview image",
				fileTypes: new[]
				{
					new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif" } },
					new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
				});
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopPublishPreviewTextBox").Text = path;
			}
		}

		private async void OnBrowseWorkshopPublishVdfClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseSaveFileAsync(
				"Select output VDF",
				fileTypes: new[]
				{
					new FilePickerFileType("VDF") { Patterns = new[] { "*.vdf" } },
					new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
				},
				defaultExtension: "vdf",
				suggestedFileName: "workshopitem.vdf");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("WorkshopPublishVdfTextBox").Text = path;
			}
		}

		private async void OnBrowseOptionsWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var path = await BrowseFolderAsync("Select work folder");
				if (!string.IsNullOrWhiteSpace(path))
				{
					GetRequiredControl<TextBox>("OptionsWorkFolderTextBox").Text = path;
				}
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnOpenOptionsWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var folder = GetWorkFolder();
				Directory.CreateDirectory(folder);
				TryOpenFolder(folder);
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnBrowseUnpackInputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync(
				"Select package",
			fileTypes: new[]
			{
				new FilePickerFileType("Supported packages") { Patterns = new[] { "*.vpk", "*.fpx", "*.gma", "*.apk", "*.hfs" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("UnpackInputTextBox").Text = path;
		}
	}

	private async void OnBrowseUnpackOutputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select output folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("UnpackOutputTextBox").Text = path;
		}
	}

	private async void OnBrowsePackInputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var title = GetPackInputMode() == PackInputMode.ParentOfChildFolders
			? "Select parent folder"
			: "Select input folder";
		var path = await BrowseFolderAsync(title);
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("PackInputTextBox").Text = path;
		}
	}

	private async void OnBrowsePackOutputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (GetPackInputMode() == PackInputMode.ParentOfChildFolders)
		{
			var folder = await BrowseFolderAsync("Select output folder");
			if (!string.IsNullOrWhiteSpace(folder))
			{
				GetRequiredControl<TextBox>("PackOutputTextBox").Text = folder;
			}

			return;
		}

		var path = await BrowseSaveFileAsync(
			"Select output package",
			fileTypes: new[]
			{
				new FilePickerFileType("VPK") { Patterns = new[] { "*.vpk" } },
				new FilePickerFileType("FPX") { Patterns = new[] { "*.fpx" } },
				new FilePickerFileType("GMA") { Patterns = new[] { "*.gma" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			},
			defaultExtension: "vpk",
			suggestedFileName: "pak01_dir.vpk");

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("PackOutputTextBox").Text = path;
		}
	}

	private async void OnBrowsePackVpkToolClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select VPK tool",
			fileTypes: new[]
			{
				new FilePickerFileType("VPK tool") { Patterns = new[] { "vpk*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("PackVpkToolTextBox").Text = path;
		}
	}

		private async void OnBrowseDecompileMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync(
				"Select MDL",
				fileTypes: new[]
			{
				new FilePickerFileType("MDL") { Patterns = new[] { "*.mdl" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("DecompileMdlTextBox").Text = path;
			}
		}

		private async void OnBrowseDecompileMdlFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select folder containing .mdl files");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("DecompileMdlTextBox").Text = path;
			}
		}

		private async void OnBrowseDecompileOutputClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select output folder");
			if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("DecompileOutputTextBox").Text = path;
		}
	}

		private void OnDecompileOutputWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var folder = Path.Combine(GetWorkFolder(), "Decompile");
				GetRequiredControl<TextBox>("DecompileOutputTextBox").Text = folder;
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private void OnDecompileOutputSubfolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			try
			{
				var input = GetText("DecompileMdlTextBox");
				if (string.IsNullOrWhiteSpace(input))
				{
					AppendLog("Decompile: enter an MDL path first.");
					return;
				}

				var fullPath = Path.GetFullPath(input);
				string output;
				if (Directory.Exists(fullPath))
				{
					output = Path.Combine(fullPath, "decompiled");
				}
				else
				{
					var folder = Path.GetDirectoryName(fullPath);
					if (string.IsNullOrWhiteSpace(folder))
					{
						folder = ".";
					}

					var baseName = Path.GetFileNameWithoutExtension(fullPath);
					baseName = string.IsNullOrWhiteSpace(baseName) ? "model" : baseName;
					output = Path.Combine(folder, $"{baseName}_decompiled");
				}

				GetRequiredControl<TextBox>("DecompileOutputTextBox").Text = output;
			}
			catch (Exception ex)
			{
				AppendLog(ex.Message);
			}
		}

		private async void OnBrowseCompileQcClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseOpenFileAsync(
				"Select QC",
				fileTypes: new[]
			{
				new FilePickerFileType("QC") { Patterns = new[] { "*.qc" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("CompileQcTextBox").Text = path;
			}
		}

		private async void OnBrowseCompileQcFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select folder containing .qc files");
			if (!string.IsNullOrWhiteSpace(path))
			{
				GetRequiredControl<TextBox>("CompileQcTextBox").Text = path;
			}
		}

		private async void OnBrowseCompileGameDirClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
		{
			var path = await BrowseFolderAsync("Select game folder");
			if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("CompileGameDirTextBox").Text = path;
		}
	}

	private async void OnBrowseCompileStudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select StudioMDL",
			fileTypes: new[]
			{
				new FilePickerFileType("StudioMDL") { Patterns = new[] { "studiomdl*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("CompileStudioMdlTextBox").Text = path;
		}
	}

	private void OnCompileUseBundledStudioMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var bundled = ToolchainDiscovery.FindBundledStudioMdlPath();
			if (string.IsNullOrWhiteSpace(bundled))
			{
				AppendLog("Bundled StudioMDL not found. Build it via scripts/toolchain/build_studiomdl.sh (or build_studiomdl.ps1 on Windows).");
				return;
			}

			GetRequiredControl<TextBox>("CompileStudioMdlTextBox").Text = bundled;
			AppendLog($"Compile: using bundled StudioMDL: {bundled}");
			if (!OperatingSystem.IsWindows() && !bundled.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				AppendLog("Compile: NOTE: On Linux/macOS, the bundled (internal) StudioMDL does not generate .phy files. Use studiomdl.exe via Wine/Proton if you need collision output.");
			}
		}
		catch (Exception ex)
		{
			AppendLog($"Compile: failed to set bundled StudioMDL: {ex.Message}");
		}
	}

	private async void OnCompileStudioMdlPhyInfoClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var message = OperatingSystem.IsWindows()
				? "On Windows, StudioMDL generates .phy when your QC uses $collisionmodel/$collisionjoints."
				: "On Linux/macOS, the bundled (internal) StudioMDL does not generate .phy files. If your QC uses $collisionmodel/$collisionjoints and you need .phy output, use a Windows studiomdl.exe via Wine/Proton (e.g. Source SDK Base 2013).";

			var dialog = new InfoWindow("About .phy generation", message);
			await dialog.ShowDialog(this);
		}
		catch (Exception ex)
		{
			AppendLog($"Compile: failed to show .phy info: {ex.Message}");
		}
	}

	private async void OnBrowseCompileSteamRootClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select Steam folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("CompileSteamRootTextBox").Text = path;
		}
	}

	private async void OnBrowseCompileWinePrefixClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select WINEPREFIX folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("CompileWinePrefixTextBox").Text = path;
		}
	}

	private async void OnBrowseCompileOutputCopyFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select output folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("CompileOutputCopyFolderTextBox").Text = path;
		}
	}

	private void OnCompileOutputCopyGameModelsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var gameDir = GetText("CompileGameDirTextBox");
			var steamAppId = ParseUInt(GetText("CompileSteamAppIdTextBox"), "Steam AppID");
			var steamRoot = GetText("CompileSteamRootTextBox");

			var resolvedGameDir = ResolveGameDirectory(gameDir, steamAppId, steamRoot);
			if (string.IsNullOrWhiteSpace(resolvedGameDir))
			{
				AppendLog("Compile: set Game Dir or Steam AppID first.");
				return;
			}

			var modelsFolder = Path.Combine(resolvedGameDir, "models");
			GetRequiredControl<TextBox>("CompileOutputCopyFolderTextBox").Text = modelsFolder;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnCompileOutputCopyWorkFolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var outputFolder = Path.Combine(GetWorkFolder(), "Compile");
			GetRequiredControl<TextBox>("CompileOutputCopyFolderTextBox").Text = outputFolder;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private void OnCompileOutputCopySubfolderClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		try
		{
			var qcPath = GetText("CompileQcTextBox");
			if (string.IsNullOrWhiteSpace(qcPath))
			{
				AppendLog("Compile: enter a QC path first.");
				return;
			}

			var fullPath = Path.GetFullPath(qcPath);
			string output;
			if (Directory.Exists(fullPath))
			{
				output = Path.Combine(fullPath, "compiled");
			}
			else
			{
				var folder = Path.GetDirectoryName(fullPath);
				if (string.IsNullOrWhiteSpace(folder))
				{
					folder = ".";
				}

				var baseName = Path.GetFileNameWithoutExtension(fullPath);
				baseName = string.IsNullOrWhiteSpace(baseName) ? "model" : baseName;
				output = Path.Combine(folder, $"{baseName}_compiled");
			}

			GetRequiredControl<TextBox>("CompileOutputCopyFolderTextBox").Text = output;
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
		}
	}

	private async void OnBrowseViewMdlClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select MDL",
			fileTypes: new[]
			{
				new FilePickerFileType("MDL") { Patterns = new[] { "*.mdl" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ViewMdlTextBox").Text = path;
		}
	}

	private async void OnBrowseViewGameDirClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select game folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ViewGameDirTextBox").Text = path;
		}
	}

	private async void OnBrowseViewHlmvClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseOpenFileAsync(
			"Select HLMV",
			fileTypes: new[]
			{
				new FilePickerFileType("HLMV") { Patterns = new[] { "hlmv*", "*.exe" } },
				new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
			});

		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ViewHlmvTextBox").Text = path;
		}
	}

	private async void OnBrowseViewSteamRootClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select Steam folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ViewSteamRootTextBox").Text = path;
		}
	}

	private async void OnBrowseViewWinePrefixClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var path = await BrowseFolderAsync("Select WINEPREFIX folder");
		if (!string.IsNullOrWhiteSpace(path))
		{
			GetRequiredControl<TextBox>("ViewWinePrefixTextBox").Text = path;
		}
	}

	private async Task RunOperationAsync(Func<IProgress<StunstickProgress>, CancellationToken, Task> action)
	{
		if (operationCancellation is not null)
		{
			AppendLog("An operation is already running.");
			return;
		}

		var version = Interlocked.Increment(ref operationProgressVersion);
		var cancelButton = GetRequiredControl<Button>("CancelButton");
		cancelButton.IsEnabled = true;

		operationCancellation = new CancellationTokenSource();
		var progress = new Progress<StunstickProgress>(p => UpdateOperationProgress(p, version));

		try
		{
			await action(progress, operationCancellation.Token);
		}
		catch (OperationCanceledException)
		{
			AppendLog("Canceled.");
			ResetOperationProgress(version);
		}
		catch (Exception ex)
		{
			AppendLog(ex.Message);
			ResetOperationProgress(version);
		}
		finally
		{
			operationCancellation.Dispose();
			operationCancellation = null;
			cancelButton.IsEnabled = false;
			ResetOperationProgress(version);
			Interlocked.Increment(ref operationProgressVersion); // invalidate any queued progress updates from this run
		}
	}

	private async Task<string?> BrowseOpenFileAsync(string title, IReadOnlyList<FilePickerFileType>? fileTypes = null)
	{
		if (!StorageProvider.CanOpen)
		{
			AppendLog("File picker is not supported on this platform.");
			return null;
		}

		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = title,
			AllowMultiple = false,
			FileTypeFilter = fileTypes
		});

		return files.Count == 0 ? null : files[0].TryGetLocalPath();
	}

	private async Task<string?> BrowseSaveFileAsync(string title, IReadOnlyList<FilePickerFileType>? fileTypes, string defaultExtension, string suggestedFileName)
	{
		if (!StorageProvider.CanSave)
		{
			AppendLog("Save file picker is not supported on this platform.");
			return null;
		}

		var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = title,
			DefaultExtension = defaultExtension,
			SuggestedFileName = suggestedFileName,
			FileTypeChoices = fileTypes,
			ShowOverwritePrompt = true
		});

		return file?.TryGetLocalPath();
	}

	private async Task<string?> BrowseFolderAsync(string title)
	{
		if (!StorageProvider.CanPickFolder)
		{
			AppendLog("Folder picker is not supported on this platform.");
			return null;
		}

		var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = title,
			AllowMultiple = false
		});

		return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
	}

	private void UpdateOperationProgress(StunstickProgress progress, int version)
	{
		if (version != Volatile.Read(ref operationProgressVersion))
		{
			return;
		}

		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => UpdateOperationProgress(progress, version));
			return;
		}

		var statusText = GetRequiredControl<TextBlock>("OperationStatusTextBlock");
		var bar = GetRequiredControl<ProgressBar>("OperationProgressBar");

		var parts = new List<string>(capacity: 4) { progress.Operation };
		if (!string.IsNullOrWhiteSpace(progress.CurrentItem))
		{
			parts.Add(progress.CurrentItem!);
		}

		if (!string.IsNullOrWhiteSpace(progress.Message))
		{
			parts.Add(progress.Message!);
		}

		if (progress.TotalBytes > 0)
		{
			parts.Add($"{FormatBytes(progress.CompletedBytes)} / {FormatBytes(progress.TotalBytes)}");
		}

		statusText.Text = string.Join(" — ", parts);

		if (progress.TotalBytes <= 0)
		{
			bar.IsIndeterminate = true;
			bar.Value = 0;
			return;
		}

		bar.IsIndeterminate = false;
		bar.Value = Math.Clamp((double)progress.CompletedBytes / progress.TotalBytes, 0, 1);
	}

	private void ResetOperationProgress(int? version = null)
	{
		if (version.HasValue && version.Value != Volatile.Read(ref operationProgressVersion))
		{
			return;
		}

		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => ResetOperationProgress(version));
			return;
		}

		var statusText = GetRequiredControl<TextBlock>("OperationStatusTextBlock");
		var bar = GetRequiredControl<ProgressBar>("OperationProgressBar");

		statusText.Text = "Ready";
		bar.IsIndeterminate = false;
		bar.Value = 0;
	}

	private static void AppendLimitedList(List<string> lines, string label, IReadOnlyList<string> items, int maxItems)
	{
		lines.Add($"  {label}: {items.Count}");
		for (var i = 0; i < items.Count && i < maxItems; i++)
		{
			lines.Add($"    {items[i]}");
		}

		if (items.Count > maxItems)
		{
			lines.Add($"    … ({items.Count - maxItems} more)");
		}
	}

	private static void AppendMdlInspectLines(List<string> outputLines, MdlInspectResult mdl)
	{
		outputLines.Add(string.Empty);
		outputLines.Add("MDL:");
		outputLines.Add($"  Name: {mdl.Name}");
		outputLines.Add($"  Version: {mdl.Version}");
		outputLines.Add($"  Checksum: {mdl.Checksum}");
		outputLines.Add($"  Length: {mdl.Length}");
		outputLines.Add($"  Flags: 0x{mdl.Flags:x8}");
		outputLines.Add($"  Bones: {mdl.BoneCount}");
		outputLines.Add($"  Sequences: {mdl.LocalSequenceCount}");
		outputLines.Add($"  Animations: {mdl.LocalAnimationCount}");
		outputLines.Add($"  TexturePaths: {mdl.TexturePathCount}");
		outputLines.Add($"  Textures: {mdl.TextureCount}");
		outputLines.Add($"  SkinFamilies: {mdl.SkinFamilyCount}");
		outputLines.Add($"  SkinReferences: {mdl.SkinReferenceCount}");
		outputLines.Add($"  BodyParts: {mdl.BodyPartCount}");
		outputLines.Add($"  FlexDescs: {mdl.FlexDescCount}");
		outputLines.Add($"  FlexControllers: {mdl.FlexControllerCount}");
		outputLines.Add($"  FlexRules: {mdl.FlexRuleCount}");
		outputLines.Add($"  AnimBlocks: {mdl.AnimBlockCount}");

		outputLines.Add(string.Empty);
		AppendLimitedList(outputLines, "TexturePaths", mdl.TexturePaths, maxItems: 12);
		outputLines.Add(string.Empty);
		AppendLimitedList(outputLines, "Textures", mdl.Textures, maxItems: 24);
		outputLines.Add(string.Empty);
		AppendLimitedList(outputLines, "BodyParts", mdl.BodyParts, maxItems: 16);
		outputLines.Add(string.Empty);
		AppendLimitedList(outputLines, "Bones", mdl.Bones, maxItems: 24);
		outputLines.Add(string.Empty);
		AppendLimitedList(outputLines, "Sequences", mdl.Sequences, maxItems: 24);
		outputLines.Add(string.Empty);
			AppendLimitedList(outputLines, "Animations", mdl.Animations, maxItems: 24);
		}

			private static string FormatBytes(long bytes)
			{
		var units = new[] { "B", "KB", "MB", "GB", "TB" };
		double value = bytes;
		var unitIndex = 0;

		while (value >= 1024 && unitIndex < units.Length - 1)
		{
			value /= 1024;
			unitIndex++;
		}

				return $"{value:0.##} {units[unitIndex]}";
			}

		private string FormatUnpackSizeText(long bytes)
		{
			if (GetChecked("UnpackShowSizesInBytesCheckBox"))
			{
				return $"{bytes:N0} B";
			}

			return FormatBytes(bytes);
		}

			private static long ClampToInt64(ulong value)
			{
				return value > (ulong)long.MaxValue ? long.MaxValue : (long)value;
			}

	private void UpdateUnpackSavedSearchesComboBox()
	{
		isUnpackSavedSearchesSyncing = true;
		try
		{
			var combo = GetRequiredControl<ComboBox>("UnpackSavedSearchesComboBox");
			var selected = combo.SelectedItem as string;

			var items = unpackSavedSearches
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
				.ToArray();

			combo.ItemsSource = items;

			if (!string.IsNullOrWhiteSpace(selected))
			{
				combo.SelectedItem = items.FirstOrDefault(s => string.Equals(s, selected, StringComparison.OrdinalIgnoreCase));
			}
			else
			{
				combo.SelectedItem = null;
			}
		}
		finally
		{
			isUnpackSavedSearchesSyncing = false;
		}
	}

	private void UpdateUnpackEntriesList()
	{
		var listBox = GetRequiredControl<ListBox>("UnpackEntriesListBox");
		var countText = GetRequiredControl<TextBlock>("UnpackEntriesCountTextBlock");
		var tree = GetRequiredControl<TreeView>("UnpackTreeView");
		var selectedText = GetRequiredControl<TextBlock>("UnpackSelectedEntryTextBlock");

		if (unpackEntries is null || unpackEntries.Count == 0)
		{
			listBox.ItemsSource = Array.Empty<PackageEntryListItem>();
			countText.Text = "0 entries";
			selectedText.Text = string.Empty;
			unpackFilteredEntries = null;
			tree.ItemsSource = Array.Empty<PackageBrowserNode>();
			return;
		}

		var query = GetRequiredControl<TextBox>("UnpackSearchTextBox").Text?.Trim();
		unpackFilteredEntries = string.IsNullOrWhiteSpace(query)
			? unpackEntries
			: unpackEntries.Where(entry => entry.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

			listBox.ItemsSource = unpackFilteredEntries
				.Select(entry => new PackageEntryListItem(entry, FormatUnpackSizeText(entry.SizeBytes)))
				.ToArray();

		countText.Text = $"{unpackFilteredEntries.Count} entr{(unpackFilteredEntries.Count == 1 ? "y" : "ies")}";
		selectedText.Text = string.Empty;
		tree.ItemsSource = BuildPackageTree(unpackFilteredEntries);
	}

	private IReadOnlyList<string> GetSelectedUnpackRelativePaths()
	{
		var selected = new List<string>();

		var list = GetRequiredControl<ListBox>("UnpackEntriesListBox");
		var listSelected = list.SelectedItems?.OfType<PackageEntryListItem>().ToArray() ?? Array.Empty<PackageEntryListItem>();
		if (listSelected.Length > 0)
		{
			selected.AddRange(listSelected.Select(item => item.Entry.RelativePath));
			return selected;
		}

		var tree = GetRequiredControl<TreeView>("UnpackTreeView");
		if (tree.SelectedItem is not PackageBrowserNode node)
		{
			return selected;
		}

		if (!node.IsDirectory)
		{
			selected.Add(node.RelativePath);
			return selected;
		}

		if (unpackEntries is null || unpackEntries.Count == 0)
		{
			return selected;
		}

		var prefix = node.RelativePath.Replace('\\', '/').Trim('/');
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

		if (string.IsNullOrWhiteSpace(prefix))
		{
			selected.AddRange(unpackEntries.Select(entry => entry.RelativePath));
			return selected;
		}

		var prefixWithSlash = prefix + "/";
		selected.AddRange(unpackEntries
			.Where(entry => entry.RelativePath.StartsWith(prefixWithSlash, comparison))
			.Select(entry => entry.RelativePath));

		return selected;
	}

	private static IReadOnlyList<PackageBrowserNode> BuildPackageTree(IReadOnlyList<PackageEntry> entries)
	{
		var root = new PackageBrowserNode(name: string.Empty, relativePath: string.Empty, isDirectory: true);
		var directoryIndex = new Dictionary<string, PackageBrowserNode>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

		foreach (var entry in entries)
		{
			var path = entry.RelativePath.Replace('\\', '/').TrimStart('/');
			if (string.IsNullOrWhiteSpace(path))
			{
				continue;
			}

			var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
			var current = root;
			var currentPath = string.Empty;

			for (var i = 0; i < parts.Length; i++)
			{
				var part = parts[i];
				var isLeaf = i == parts.Length - 1;

				currentPath = string.IsNullOrEmpty(currentPath) ? part : currentPath + "/" + part;

				if (isLeaf)
				{
					current.Children.Add(new PackageBrowserNode(
						name: part,
						relativePath: currentPath,
						isDirectory: false,
						sizeBytes: entry.SizeBytes,
						crc32: entry.Crc32));
					continue;
				}

				if (!directoryIndex.TryGetValue(currentPath, out var dirNode))
				{
					dirNode = new PackageBrowserNode(name: part, relativePath: currentPath, isDirectory: true);
					directoryIndex[currentPath] = dirNode;
					current.Children.Add(dirNode);
				}

				current = dirNode;
			}
		}

		SortTree(root);
		return root.Children;
	}

	private static void SortTree(PackageBrowserNode node)
	{
		if (node.Children.Count == 0)
		{
			return;
		}

		var ordered = node.Children
			.OrderByDescending(child => child.IsDirectory)
			.ThenBy(child => child.Name, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
			.ToList();

		node.Children.Clear();
		foreach (var child in ordered)
		{
			node.Children.Add(child);
			SortTree(child);
		}
	}

			private static string CreateTempOutputDirectoryFor(string packagePath)
			{
				var baseName = Path.GetFileNameWithoutExtension(packagePath);
				if (string.IsNullOrWhiteSpace(baseName))
				{
				baseName = "package";
			}

			var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			var root = Path.Combine(Path.GetTempPath(), "Stunstick", "Unpack", $"{baseName}_{stamp}");

		var candidate = root;
		var suffix = 1;
		while (Directory.Exists(candidate))
		{
			candidate = root + $"_{suffix}";
			suffix++;
		}

			Directory.CreateDirectory(candidate);
			return candidate;
		}

			private static string GetDecompileOutputQcPath(string mdlPath, string outputDirectory, bool folderForEachModel)
			{
				if (!folderForEachModel)
				{
					return Path.Combine(outputDirectory, "model.qc");
				}

			var modelName = Path.GetFileNameWithoutExtension(mdlPath);
			if (string.IsNullOrWhiteSpace(modelName))
			{
				return Path.Combine(outputDirectory, "model.qc");
			}

				return Path.Combine(outputDirectory, modelName, "model.qc");
			}

			private static string? ResolveGameDirectory(string? gameDir, uint? steamAppId, string? steamRoot)
			{
				if (!string.IsNullOrWhiteSpace(gameDir))
				{
					return gameDir;
				}

				if (steamAppId is null)
				{
					return null;
				}

				var preset = ToolchainDiscovery.FindSteamPreset(steamAppId.Value, string.IsNullOrWhiteSpace(steamRoot) ? null : steamRoot);
				if (preset is null)
				{
					return null;
				}

				return ToolchainDiscovery.FindPreferredGameDirectory(preset.GameDirectory, preset.AppId) ?? preset.GameDirectory;
			}

	private static string? TryGetCompiledModelPath(string qcPathFileName, string? gameDirectory, string? copyOutputRoot = null)
	{
		if (string.IsNullOrWhiteSpace(qcPathFileName) || !File.Exists(qcPathFileName))
		{
			return null;
				}

				var modelNamePath = TryReadQcModelNamePath(qcPathFileName);
				if (string.IsNullOrWhiteSpace(modelNamePath))
				{
					return null;
				}

				var normalized = modelNamePath.Replace('\\', '/').Trim();

				if (Path.IsPathRooted(normalized))
				{
					var rootedCandidates = new List<string> { normalized };
					if (string.IsNullOrWhiteSpace(Path.GetExtension(normalized)))
					{
						rootedCandidates.Add(normalized + ".mdl");
					}

					return rootedCandidates.FirstOrDefault(File.Exists);
				}

			var relative = normalized.Replace('/', Path.DirectorySeparatorChar);
			var relativeCandidates = new List<string> { relative };
			if (string.IsNullOrWhiteSpace(Path.GetExtension(relative)))
			{
				relativeCandidates.Add(relative + ".mdl");
			}

			if (!string.IsNullOrWhiteSpace(copyOutputRoot))
			{
				var copyBase = GetCompileCopyModelsRoot(copyOutputRoot);
				foreach (var relativeCandidate in relativeCandidates)
				{
					var copyPath = Path.Combine(copyBase, relativeCandidate);
					if (File.Exists(copyPath))
					{
						return copyPath;
					}
				}
			}

			if (!string.IsNullOrWhiteSpace(gameDirectory))
			{
				foreach (var relativeCandidate in relativeCandidates)
				{
					var direct = Path.Combine(gameDirectory, relativeCandidate);
					if (File.Exists(direct))
					{
						return direct;
					}

					var inModels = Path.Combine(gameDirectory, "models", relativeCandidate);
					if (File.Exists(inModels))
					{
						return inModels;
					}
				}
			}

			try
			{
				var qcDir = Path.GetDirectoryName(Path.GetFullPath(qcPathFileName));
				if (!string.IsNullOrWhiteSpace(qcDir))
				{
					foreach (var relativeCandidate in relativeCandidates)
					{
						var direct = Path.Combine(qcDir, relativeCandidate);
						if (File.Exists(direct))
						{
							return direct;
						}

						var inModels = Path.Combine(qcDir, "models", relativeCandidate);
						if (File.Exists(inModels))
						{
							return inModels;
						}
					}
				}
			}
			catch
			{
			}

			return null;
				}

				private static string? TryReadQcModelNamePath(string qcPathFileName)
				{
				try
				{
					var text = File.ReadAllText(qcPathFileName);

					var match = Regex.Match(
						text,
						pattern: @"(?im)^\s*\$modelname\s+(?:""([^""]+)""|(\S+))",
						options: RegexOptions.CultureInvariant);

					if (!match.Success)
					{
						return null;
					}

					return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
				}
				catch
				{
					return null;
					}
				}

				private string? TryCopyCompileOutputs(string qcPathFileName, string? gameDirectory, string outputRoot, CancellationToken cancellationToken)
				{
					try
					{
						if (string.IsNullOrWhiteSpace(outputRoot))
						{
							return null;
						}

						var modelNamePath = TryReadQcModelNamePath(qcPathFileName);
						if (string.IsNullOrWhiteSpace(modelNamePath))
						{
							AppendLog($"Compile: no $modelname found; could not copy outputs for: {qcPathFileName}");
							return null;
						}

						var compiledMdlPath = TryGetCompiledModelPath(qcPathFileName, gameDirectory, copyOutputRoot: null);
						if (string.IsNullOrWhiteSpace(compiledMdlPath) || !File.Exists(compiledMdlPath))
						{
							AppendLog($"Compile: could not locate compiled MDL to copy outputs for: {qcPathFileName}");
							return null;
						}

						var outputModelsRoot = GetCompileCopyModelsRoot(outputRoot);
						var modelsSubpath = GetModelsSubpath(modelNamePath);
						var targetFolder = string.IsNullOrWhiteSpace(modelsSubpath) ? outputModelsRoot : Path.Combine(outputModelsRoot, modelsSubpath);

						Directory.CreateDirectory(targetFolder);

						var sourceFolder = Path.GetDirectoryName(compiledMdlPath);
						if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
						{
							AppendLog($"Compile: could not locate source folder for compiled MDL: {compiledMdlPath}");
							return null;
						}

						var baseName = Path.GetFileNameWithoutExtension(compiledMdlPath);
						if (string.IsNullOrWhiteSpace(baseName))
						{
							AppendLog($"Compile: could not determine compiled model base name: {compiledMdlPath}");
							return null;
						}

						var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
						{
							".ani",
							".mdl",
							".phy",
							".vtx",
							".vvd"
						};

						var copied = 0;
						foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, $"{baseName}.*", SearchOption.TopDirectoryOnly))
						{
							cancellationToken.ThrowIfCancellationRequested();

							var ext = Path.GetExtension(sourceFile);
							if (!allowedExtensions.Contains(ext))
							{
								continue;
							}

							var destFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
							File.Copy(sourceFile, destFile, overwrite: true);
							copied++;
						}

						AppendLog($"Compile: copied {copied} file(s) to: {targetFolder}");

						var copiedMdl = Path.Combine(targetFolder, baseName + ".mdl");
						return File.Exists(copiedMdl) ? copiedMdl : null;
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception ex)
					{
						AppendLog($"Compile copy failed: {ex.Message}");
						return null;
					}
				}

				private static string GetCompileCopyModelsRoot(string outputRoot)
				{
					var trimmed = Path.TrimEndingDirectorySeparator(outputRoot);
					var lastSegment = Path.GetFileName(trimmed);
					if (string.Equals(lastSegment, "models", StringComparison.OrdinalIgnoreCase))
					{
						return outputRoot;
					}

					return Path.Combine(outputRoot, "models");
				}

				private static string GetModelsSubpath(string modelNamePath)
				{
					if (string.IsNullOrWhiteSpace(modelNamePath))
					{
						return string.Empty;
					}

					var normalized = modelNamePath.Replace('\\', '/').Trim();
					var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length <= 1)
					{
						return string.Empty;
					}

					var directoryCount = parts.Length - 1;
					var modelsIndex = -1;
					for (var i = 0; i < directoryCount; i++)
					{
						if (string.Equals(parts[i], "models", StringComparison.OrdinalIgnoreCase))
						{
							modelsIndex = i;
						}
					}

					var start = modelsIndex >= 0 ? modelsIndex + 1 : 0;
					if (start >= directoryCount)
					{
						return string.Empty;
					}

					return Path.Combine(parts[start..directoryCount]);
				}

				private void TryOpenFolder(string folderPath)
				{
					try
					{
				if (string.IsNullOrWhiteSpace(folderPath))
			{
				return;
			}

			var fullPath = Path.GetFullPath(folderPath);
			if (!Directory.Exists(fullPath))
			{
				return;
			}

				if (OperatingSystem.IsWindows())
				{
					var psi = new ProcessStartInfo
					{
						FileName = "explorer.exe",
						UseShellExecute = true
					};
					psi.ArgumentList.Add(fullPath);
					Process.Start(psi);
				}
				else if (OperatingSystem.IsMacOS())
				{
					var psi = new ProcessStartInfo
					{
						FileName = "open",
						UseShellExecute = false
					};
					psi.ArgumentList.Add(fullPath);
					Process.Start(psi);
				}
				else
				{
					var psi = new ProcessStartInfo
					{
						FileName = "xdg-open",
						UseShellExecute = false
					};
					psi.ArgumentList.Add(fullPath);
					Process.Start(psi);
				}
			}
			catch (Exception ex)
			{
			AppendLog($"Failed to open folder: {ex.Message}");
		}
	}

		private void TryOpenFile(string filePath)
		{
		try
		{
			if (string.IsNullOrWhiteSpace(filePath))
			{
				return;
			}

			var fullPath = Path.GetFullPath(filePath);
			if (!File.Exists(fullPath))
			{
				return;
			}

			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = fullPath,
					UseShellExecute = true
				});
			}
			else if (OperatingSystem.IsMacOS())
			{
				var psi = new ProcessStartInfo
				{
					FileName = "open",
					UseShellExecute = false
				};
				psi.ArgumentList.Add(fullPath);
				Process.Start(psi);
			}
			else
			{
				var psi = new ProcessStartInfo
				{
					FileName = "xdg-open",
					UseShellExecute = false
				};
				psi.ArgumentList.Add(fullPath);
				Process.Start(psi);
			}
		}
		catch (Exception ex)
		{
			AppendLog($"Failed to open file: {ex.Message}");
			}
		}

			private void TryOpenUri(string uri)
			{
				try
				{
				if (string.IsNullOrWhiteSpace(uri))
				{
					return;
				}

				if (OperatingSystem.IsWindows())
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = uri,
						UseShellExecute = true
					});
				}
				else if (OperatingSystem.IsMacOS())
				{
					var psi = new ProcessStartInfo
					{
						FileName = "open",
						UseShellExecute = false
					};
					psi.ArgumentList.Add(uri);
					Process.Start(psi);
				}
				else
				{
					var psi = new ProcessStartInfo
					{
						FileName = "xdg-open",
						UseShellExecute = false
					};
					psi.ArgumentList.Add(uri);
					Process.Start(psi);
				}
				}
				catch (Exception ex)
				{
					AppendLog($"Failed to open link: {ex.Message}");
				}
			}

		private static void RequireNonEmpty(string value, string fieldName)
		{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw new InvalidDataException($"{fieldName} is required.");
		}
	}

	private static void RequireFileExists(string path, string fieldName)
	{
		RequireNonEmpty(path, fieldName);
		if (!File.Exists(path))
		{
			throw new FileNotFoundException($"{fieldName} not found.", path);
		}
	}

	private static void RequireDirectoryExists(string path, string fieldName)
	{
		RequireNonEmpty(path, fieldName);
		if (!Directory.Exists(path))
		{
			throw new DirectoryNotFoundException($"{fieldName} not found: \"{path}\".");
		}
	}

	private static void RequireFileOrDirectoryExists(string path, string fieldName)
	{
		RequireNonEmpty(path, fieldName);
		if (!File.Exists(path) && !Directory.Exists(path))
		{
			throw new FileNotFoundException($"{fieldName} not found.", path);
		}
	}

	private static void ValidatePackOptions(string inputDirectory, string outputPath, bool multiFile, bool withMd5, uint vpkVersion)
	{
		var extension = Path.GetExtension(outputPath).Trim().ToLowerInvariant();
		if (extension is ".vpk" or ".fpx")
		{
			if (vpkVersion is not 1 and not 2)
			{
				throw new InvalidDataException("VPK Version must be 1 or 2.");
			}

			if (withMd5 && vpkVersion != 2)
			{
				throw new InvalidDataException("Write MD5 sections requires VPK Version 2.");
			}

			if (multiFile)
			{
				var expectedDirectorySuffix = extension == ".fpx" ? "_fdr" : "_dir";
				var baseName = Path.GetFileNameWithoutExtension(outputPath);
				if (!baseName.EndsWith(expectedDirectorySuffix, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Multi-file output path must end with \"{expectedDirectorySuffix}{extension}\".");
				}
			}

			return;
		}

		if (extension == ".gma")
		{
			var addonJsonPath = Path.Combine(inputDirectory, "addon.json");
			if (!File.Exists(addonJsonPath))
			{
				throw new InvalidDataException("GMA packing requires an addon.json in the input folder.");
			}

			return;
		}

		throw new InvalidDataException($"Unsupported output type: {extension}");
	}

		private static void ValidateToolLaunchInputs(string? toolPath, string? gameDir, uint? steamAppId, string? steamRoot, string toolDisplayName)
		{
			if (!string.IsNullOrWhiteSpace(toolPath) && !File.Exists(toolPath))
			{
				throw new FileNotFoundException($"{toolDisplayName} not found.", toolPath);
		}

		if (!string.IsNullOrWhiteSpace(gameDir) && !Directory.Exists(gameDir))
		{
			throw new DirectoryNotFoundException($"Game Dir not found: \"{gameDir}\".");
		}

		if (!string.IsNullOrWhiteSpace(steamRoot) && !Directory.Exists(steamRoot))
		{
			throw new DirectoryNotFoundException($"Steam Root not found: \"{steamRoot}\".");
		}

			if (string.IsNullOrWhiteSpace(toolPath) && string.IsNullOrWhiteSpace(gameDir) && steamAppId is null)
			{
				throw new InvalidDataException($"{toolDisplayName} path is required unless Game Dir or Steam AppID is provided.");
			}
		}

	private void OpenHelpResource(string? relativePath, string? fallbackRelativePath = null, string? webUrl = null)
	{
		try
		{
			var candidates = new List<string>();

			void AddCandidate(string? basePath, string? rel)
			{
				if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(rel))
				{
					return;
				}

				try
				{
					candidates.Add(Path.GetFullPath(Path.Combine(basePath, rel)));
				}
				catch
				{
				}
			}

			AddCandidate(AppContext.BaseDirectory, relativePath);
			AddCandidate(Environment.CurrentDirectory, relativePath);
			AddCandidate(AppContext.BaseDirectory, fallbackRelativePath);
			AddCandidate(Environment.CurrentDirectory, fallbackRelativePath);

			foreach (var candidate in candidates)
			{
				if (File.Exists(candidate))
				{
					TryOpenFile(candidate);
					return;
				}
			}

			if (!string.IsNullOrWhiteSpace(webUrl))
			{
				OpenUrl(webUrl!);
			}
			else
			{
				AppendLog("Help file not found.");
			}
		}
		catch (Exception ex)
		{
			AppendLog($"Help open failed: {ex.Message}");
		}
	}

		private void SelectMainTab(string tabItemName)
		{
			GetRequiredControl<TabControl>("MainTabControl").SelectedItem = GetRequiredControl<TabItem>(tabItemName);
		}

		private T GetRequiredControl<T>(string name) where T : Control
		{
			var control = this.FindControl<T>(name);
			if (control is null)
			{
			throw new InvalidOperationException($"Missing UI control: \"{name}\".");
		}

		return control;
	}

	private string GetText(string name)
	{
		return GetRequiredControl<TextBox>(name).Text?.Trim() ?? string.Empty;
	}

	private bool GetChecked(string name)
	{
		return GetRequiredControl<CheckBox>(name).IsChecked ?? false;
	}

	private void SetChecked(string name, bool value)
	{
		GetRequiredControl<CheckBox>(name).IsChecked = value;
	}

	private void SetText(string name, string? value)
	{
		if (value is null)
		{
			return;
		}

		GetRequiredControl<TextBox>(name).Text = value;
	}

	private int GetComboBoxIndex(string name)
	{
		return GetRequiredControl<ComboBox>(name).SelectedIndex;
	}

	private void SetComboBoxIndex(string name, int index)
	{
		var combo = GetRequiredControl<ComboBox>(name);
		if (index < 0)
		{
			return;
		}

		var count = combo.ItemCount;
		if (count > 0 && index >= count)
		{
			return;
		}

		combo.SelectedIndex = index;
	}

	private string? GetComboBoxText(string name)
	{
		var combo = GetRequiredControl<ComboBox>(name);
		return combo.SelectedItem switch
		{
			ComboBoxItem item => item.Content?.ToString(),
			_ => combo.SelectedItem?.ToString()
		};
	}

	private uint? ParseUInt(string text, string fieldName)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		if (!uint.TryParse(text.Trim(), out var value))
		{
			throw new InvalidDataException($"Invalid {fieldName}.");
		}

		return value;
	}

	private void AppendLog(string message)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => AppendLog(message));
			return;
		}

		try
		{
			var log = GetRequiredControl<TextBox>("LogTextBox");
			var existing = log.Text ?? string.Empty;
			log.Text = string.IsNullOrWhiteSpace(existing) ? message : existing + Environment.NewLine + message;
		}
		catch
		{
			try
			{
				Console.Error.WriteLine(message);
			}
			catch
			{
			}
		}
	}

	private void SetInspectOutput(string text)
	{
		if (!Dispatcher.UIThread.CheckAccess())
		{
			Dispatcher.UIThread.Post(() => SetInspectOutput(text));
			return;
		}

		GetRequiredControl<TextBox>("InspectOutputTextBox").Text = text ?? string.Empty;
	}

	internal void BringToFrontFromIpc()
	{
		try
		{
			if (WindowState == WindowState.Minimized)
			{
				WindowState = WindowState.Normal;
			}

			Activate();
			Focus();
		}
		catch
		{
		}
	}
}
