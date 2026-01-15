using Stunstick.Core.GoldSrc;
using Stunstick.Core.Mdl;
using Stunstick.Core.Phy;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;
using Stunstick.Core.IO;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Linq;

namespace Stunstick.App.Decompile;

internal static class MdlDecompiler
{
	private const uint MdlzId = 0x5A4C444D; // "MDLZ"

	public static async Task DecompileAsync(string mdlPath, string outputDirectory, DecompileOptions options, CancellationToken cancellationToken)
	{
		options ??= new DecompileOptions();

		if (string.IsNullOrWhiteSpace(mdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(mdlPath));
		}

		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
		}

		if (!File.Exists(mdlPath))
		{
			throw new FileNotFoundException("MDL file not found.", mdlPath);
		}

		var modelName = Path.GetFileNameWithoutExtension(mdlPath);
		if (string.IsNullOrWhiteSpace(modelName))
		{
			throw new InvalidDataException("Unable to determine model name from file path.");
		}

		var modelOutputFolder = options.FolderForEachModel
			? Path.Combine(outputDirectory, modelName)
			: outputDirectory;
		Directory.CreateDirectory(modelOutputFolder);

		AccessedBytesDebugLogs? accessedBytesDebugLogs = null;
		AccessedBytesLog? mdlAccessedBytesLog = null;
		if (options.WriteDebugInfoFiles)
		{
			try
			{
				accessedBytesDebugLogs = new AccessedBytesDebugLogs(modelName);
				var fullMdlPath = Path.GetFullPath(mdlPath);
				var mdlLength = new FileInfo(fullMdlPath).Length;
				mdlAccessedBytesLog = accessedBytesDebugLogs.GetOrCreateLog(
					accessedBytesDebugLogs.BuildFileName("decompile-MDL.txt"),
					displayPath: fullMdlPath,
					containerPath: fullMdlPath,
					containerOffset: 0,
					length: mdlLength);
			}
			catch
			{
				// Best-effort.
				accessedBytesDebugLogs = null;
				mdlAccessedBytesLog = null;
			}
		}

		var originalFolder = Path.Combine(modelOutputFolder, "original");
		Directory.CreateDirectory(originalFolder);
		var copiedOriginalFiles = CopyRelatedModelFiles(mdlPath, originalFolder).ToList();

		var signature = ReadMdlSignature(mdlPath);
		if (signature.Id == GoldSrcMdlReader.Idst && signature.Version == 10)
		{
			GoldSrcMdlFile goldSrcModel;
			if (mdlAccessedBytesLog is not null)
			{
				using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var loggedStream = new AccessLoggedStream(stream, mdlAccessedBytesLog);
				goldSrcModel = GoldSrcMdlReader.Read(loggedStream, sourcePath: Path.GetFullPath(mdlPath));
			}
			else
			{
				goldSrcModel = GoldSrcMdlReader.Read(mdlPath);
			}

			var textureMdlPath = mdlPath;
			AccessedBytesLog? textureMdlAccessedBytesLog = mdlAccessedBytesLog;
			if (goldSrcModel.Textures.Count == 0)
			{
				var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
				var baseName = Path.GetFileNameWithoutExtension(mdlPath);
				var extension = Path.GetExtension(mdlPath);
				var texturePathCandidate = Path.Combine(directoryPath, baseName + "T" + extension);
				if (File.Exists(texturePathCandidate))
				{
					try
					{
						if (accessedBytesDebugLogs is not null)
						{
							var fullTexturePath = Path.GetFullPath(texturePathCandidate);
							var textureLength = new FileInfo(fullTexturePath).Length;
							textureMdlAccessedBytesLog = accessedBytesDebugLogs.GetOrCreateLog(
								accessedBytesDebugLogs.BuildFileName("decompile-TextureMDL.txt"),
								displayPath: fullTexturePath,
								containerPath: fullTexturePath,
								containerOffset: 0,
								length: textureLength);
						}

						GoldSrcMdlFile textureModel;
						if (textureMdlAccessedBytesLog is not null)
						{
							using var stream = new FileStream(texturePathCandidate, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
							using var loggedStream = new AccessLoggedStream(stream, textureMdlAccessedBytesLog);
							textureModel = GoldSrcMdlReader.Read(loggedStream, sourcePath: Path.GetFullPath(texturePathCandidate));
						}
						else
						{
							textureModel = GoldSrcMdlReader.Read(texturePathCandidate);
						}

						if (textureModel.Textures.Count > 0)
						{
							goldSrcModel = goldSrcModel with { Textures = textureModel.Textures };
							textureMdlPath = texturePathCandidate;
						}
					}
					catch
					{
						// Best-effort.
					}
				}
			}

			await GoldSrcMdlDecompiler.DecompileAsync(
				mdlPath,
				textureMdlPath,
				modelOutputFolder,
				originalFolder,
				copiedOriginalFiles,
				goldSrcModel,
				options,
				textureMdlAccessedBytesLog,
				accessedBytesDebugLogs?.GetDebugFiles(),
				cancellationToken);
			return;
		}

		if (signature.Id == MdlzId && signature.Version == 14)
		{
			var decompressedBytes = DecompressMdlzToGoldSrcIdst(mdlPath);
			var decompressedFileName = modelName + "_decompressed.mdl";
			var decompressedPathFileName = Path.Combine(originalFolder, decompressedFileName);
			await File.WriteAllBytesAsync(decompressedPathFileName, decompressedBytes, cancellationToken);
			if (!copiedOriginalFiles.Contains(decompressedFileName, StringComparer.OrdinalIgnoreCase))
			{
				copiedOriginalFiles.Add(decompressedFileName);
			}

			using var decompressedStream = new MemoryStream(decompressedBytes, writable: false);
			var goldSrcModel = GoldSrcMdlReader.Read(decompressedStream, sourcePath: Path.GetFullPath(mdlPath) + "#decompressed");
			await GoldSrcMdlDecompiler.DecompileAsync(
				mdlPath,
				decompressedPathFileName,
				modelOutputFolder,
				originalFolder,
				copiedOriginalFiles,
				goldSrcModel,
				options,
				textureMdlAccessLog: null,
				accessedBytesDebugFiles: null,
				cancellationToken);
			return;
		}

			MdlModel model;
			if (mdlAccessedBytesLog is not null)
			{
				using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var loggedStream = new AccessLoggedStream(stream, mdlAccessedBytesLog);
				model = MdlReader.Read(loggedStream, sourcePath: Path.GetFullPath(mdlPath), versionOverride: options.VersionOverride);
			}
			else
			{
				model = MdlReader.Read(mdlPath, versionOverride: options.VersionOverride);
			}

		string? proceduralBonesVrdFileName = null;
		if (options.WriteProceduralBonesVrdFile)
		{
			try
			{
				proceduralBonesVrdFileName = await MdlProceduralBonesVrdWriter.TryWriteProceduralBonesVrdAsync(
					mdlPath,
					modelOutputFolder,
					modelName,
					model,
					options,
					mdlAccessedBytesLog,
					cancellationToken);
			}
			catch
			{
				// Best-effort for now.
			}
		}

		var skeletonPathFileName = Path.Combine(modelOutputFolder, "skeleton.smd");
		await WriteSkeletonSmdAsync(skeletonPathFileName, model.Bones, options, cancellationToken);

		Dictionary<int, string>? animationSmdRelativePathFileNames = null;
		if (options.WriteBoneAnimationSmdFiles)
		{
			try
			{
				animationSmdRelativePathFileNames = await MdlAnimationSmdWriter.WriteAnimationSmdFilesAsync(
					mdlPath,
					modelOutputFolder,
					model,
					options,
					mdlAccessedBytesLog,
					accessedBytesDebugLogs,
					cancellationToken);
			}
			catch
			{
				// Best-effort for now.
			}
		}

		VvdFile? vvd = null;
		VtxFile? vtx = null;
		PhyFile? phy = null;
		MdlVertexAnimationVtaWriter.VtaResult? vtaResult = null;

			if (model.Header.Version == 53)
			{
				using var mdlStream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using Stream mdlReadStream = mdlAccessedBytesLog is null
					? mdlStream
					: new AccessLoggedStream(mdlStream, mdlAccessedBytesLog);

				if (MdlV53EmbeddedSectionsReader.TryRead(mdlReadStream, out var embedded))
				{
					var fullMdlPath = Path.GetFullPath(mdlPath);

					using var vvdStream = new BoundedReadOnlyStream(mdlReadStream, embedded.VvdOffset, embedded.VvdSize);
					if (accessedBytesDebugLogs is not null)
					{
						var vvdLog = accessedBytesDebugLogs.GetOrCreateLog(
							accessedBytesDebugLogs.BuildFileName("decompile-VVD.txt"),
							displayPath: fullMdlPath + "#vvd",
							containerPath: fullMdlPath,
							containerOffset: embedded.VvdOffset,
							length: embedded.VvdSize);

						using var vvdLoggedStream = new AccessLoggedStream(vvdStream, vvdLog);
						vvd = VvdReader.Read(vvdLoggedStream, sourcePath: mdlPath + "#vvd", mdlVersion: options.VersionOverride ?? model.Header.Version);
					}
					else
					{
						vvd = VvdReader.Read(vvdStream, sourcePath: mdlPath + "#vvd", mdlVersion: options.VersionOverride ?? model.Header.Version);
					}

					using var vtxStream = new BoundedReadOnlyStream(mdlReadStream, embedded.VtxOffset, embedded.VtxSize);
					if (accessedBytesDebugLogs is not null)
					{
						var vtxLog = accessedBytesDebugLogs.GetOrCreateLog(
							accessedBytesDebugLogs.BuildFileName("decompile-VTX.txt"),
							displayPath: fullMdlPath + "#vtx",
							containerPath: fullMdlPath,
							containerOffset: embedded.VtxOffset,
							length: embedded.VtxSize);

						using var vtxLoggedStream = new AccessLoggedStream(vtxStream, vtxLog);
						vtx = VtxReader.Read(vtxLoggedStream, sourcePath: mdlPath + "#vtx");
					}
					else
					{
						vtx = VtxReader.Read(vtxStream, sourcePath: mdlPath + "#vtx");
					}

					if (embedded.PhyOffset > 0 && embedded.PhySize > 0)
					{
						try
						{
							using var phyStream = new BoundedReadOnlyStream(mdlReadStream, embedded.PhyOffset, embedded.PhySize);
							if (accessedBytesDebugLogs is not null)
							{
								var phyLog = accessedBytesDebugLogs.GetOrCreateLog(
									accessedBytesDebugLogs.BuildFileName("decompile-PHY.txt"),
									displayPath: fullMdlPath + "#phy",
									containerPath: fullMdlPath,
									containerOffset: embedded.PhyOffset,
									length: embedded.PhySize);

								using var phyLoggedStream = new AccessLoggedStream(phyStream, phyLog);
								phy = PhyReader.Read(phyLoggedStream, sourcePath: mdlPath + "#phy");
							}
							else
							{
								phy = PhyReader.Read(phyStream, sourcePath: mdlPath + "#phy");
							}
						}
						catch
						{
							// Best-effort for now.
						}
					}
				}
			}
		else
		{
			var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
			var baseName = Path.GetFileNameWithoutExtension(mdlPath);
			var vvdPath = Path.Combine(directoryPath, baseName + ".vvd");
			var vtxPath = FindVtxPath(directoryPath, baseName);
			var phyPath = Path.Combine(directoryPath, baseName + ".phy");

			if (File.Exists(vvdPath))
			{
				if (accessedBytesDebugLogs is not null)
				{
					var fullVvdPath = Path.GetFullPath(vvdPath);
					var vvdLog = accessedBytesDebugLogs.GetOrCreateLog(
						accessedBytesDebugLogs.BuildFileName("decompile-VVD.txt"),
						displayPath: fullVvdPath,
						containerPath: fullVvdPath,
						containerOffset: 0,
						length: new FileInfo(fullVvdPath).Length);

					using var stream = new FileStream(vvdPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					using var loggedStream = new AccessLoggedStream(stream, vvdLog);
						vvd = VvdReader.Read(loggedStream, sourcePath: fullVvdPath, mdlVersion: options.VersionOverride ?? model.Header.Version);
					}
					else
					{
						vvd = VvdReader.Read(vvdPath, options.VersionOverride ?? model.Header.Version);
					}
				}

			if (vtxPath is not null && File.Exists(vtxPath))
			{
				if (accessedBytesDebugLogs is not null)
				{
					var fullVtxPath = Path.GetFullPath(vtxPath);
					var vtxLog = accessedBytesDebugLogs.GetOrCreateLog(
						accessedBytesDebugLogs.BuildFileName("decompile-VTX.txt"),
						displayPath: fullVtxPath,
						containerPath: fullVtxPath,
						containerOffset: 0,
						length: new FileInfo(fullVtxPath).Length);

					using var stream = new FileStream(vtxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					using var loggedStream = new AccessLoggedStream(stream, vtxLog);
					vtx = VtxReader.Read(loggedStream, sourcePath: fullVtxPath);
				}
				else
				{
					vtx = VtxReader.Read(vtxPath);
				}
			}

			if (File.Exists(phyPath))
			{
				try
				{
					if (accessedBytesDebugLogs is not null)
					{
						var fullPhyPath = Path.GetFullPath(phyPath);
						var phyLog = accessedBytesDebugLogs.GetOrCreateLog(
							accessedBytesDebugLogs.BuildFileName("decompile-PHY.txt"),
							displayPath: fullPhyPath,
							containerPath: fullPhyPath,
							containerOffset: 0,
							length: new FileInfo(fullPhyPath).Length);

						using var stream = new FileStream(phyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						using var loggedStream = new AccessLoggedStream(stream, phyLog);
						phy = PhyReader.Read(loggedStream, sourcePath: fullPhyPath);
					}
					else
					{
						phy = PhyReader.Read(phyPath);
					}
				}
				catch
				{
					// Best-effort for now.
				}
			}
		}

		var hasFlexes = ModelHasFlexes(model);
		var writeVta = (options.WriteVertexAnimationVtaFile || hasFlexes) && vvd is not null;
		if (writeVta)
		{
			try
			{
				var vvdFile = vvd!;
				vtaResult = await MdlVertexAnimationVtaWriter.WriteVtaAsync(modelOutputFolder, modelName, model, vvdFile, options, cancellationToken);
			}
			catch
			{
				// Best-effort for now.
			}
		}

		var hasPhysics = false;
		if (phy is not null && options.WritePhysicsMeshSmdFile)
		{
			try
			{
				var physicsPathFileName = Path.Combine(modelOutputFolder, "physics.smd");
				var wrotePhysics = await WritePhysicsSmdAsync(physicsPathFileName, model, phy, options, mdlAccessedBytesLog, accessedBytesDebugLogs, cancellationToken);

				// Keep QC safe: we still don't emit $collisionjoints/options, so only reference physics when there's one solid.
				hasPhysics = wrotePhysics && phy.Header.SolidCount == 1;
			}
			catch
			{
				// Physics decompile is best-effort for now; keep mesh/skeleton outputs working.
			}
		}

		if (vvd is not null && vtx is not null && options.WriteReferenceMeshSmdFiles)
		{
			await WriteReferenceSmdsAsync(modelOutputFolder, modelName, model, vvd, vtx, options, cancellationToken);
		}

			if (vtx is not null && options.WriteQcFile)
			{
				await WriteQcAsync(modelOutputFolder, modelName, model, vtx, hasPhysics, proceduralBonesVrdFileName, options, animationSmdRelativePathFileNames, vtaResult, cancellationToken);
			}

			if (options.WriteDeclareSequenceQciFile)
			{
				try
				{
					await WriteDeclareSequenceQciAsync(modelOutputFolder, modelName, model, options, cancellationToken);
				}
				catch
				{
					// Best-effort for now.
				}
			}

			var manifest = new MdlDecompileManifest(
				SourceMdlPath: Path.GetFullPath(mdlPath),
				OutputFolder: Path.GetFullPath(modelOutputFolder),
				OriginalFilesFolder: Path.GetFullPath(originalFolder),
			Header: model.Header,
			Bones: model.Bones,
			CopiedOriginalFiles: copiedOriginalFiles,
			GeneratedBy: "Stunstick.Cli (fork)");

		var manifestJson = JsonSerializer.Serialize(
			manifest,
			new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() });

		var manifestPathFileName = Path.Combine(modelOutputFolder, "manifest.json");
		await File.WriteAllTextAsync(manifestPathFileName, manifestJson, cancellationToken);

		if (options.WriteDebugInfoFiles)
		{
			try
			{
				await MdlDecompileDebugInfoWriter.WriteDebugInfoAsync(
					mdlPath,
					modelOutputFolder,
					model,
					options,
					proceduralBonesVrdFileName,
					vvd,
					vtx,
					phy,
					accessedBytesDebugLogs?.GetDebugFiles(),
					cancellationToken);
			}
			catch
			{
				// Best-effort.
			}
			}
		}

		private static async Task WriteDeclareSequenceQciAsync(
			string modelOutputFolder,
			string modelFileNamePrefix,
			MdlModel model,
			DecompileOptions options,
			CancellationToken cancellationToken)
		{
			if (model.SequenceDescs.Count == 0)
			{
				return;
			}

			var qciFileName = modelFileNamePrefix + "_DeclareSequence.qci";
			var qciPathFileName = Path.Combine(modelOutputFolder, qciFileName);

			await using var stream = new FileStream(qciPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
			await using var writer = new StreamWriter(stream);

			await DecompileFormat.WriteHeaderCommentAsync(writer, options);
			await writer.WriteLineAsync();

			var keyword = options.QcUseMixedCaseForKeywords ? "$DeclareSequence" : "$declaresequence";
			for (var i = 0; i < model.SequenceDescs.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var seq = model.SequenceDescs[i];
				var name = string.IsNullOrWhiteSpace(seq.Name) ? $"sequence_{seq.Index}" : seq.Name;
				name = name.Replace("\"", "'");

				await writer.WriteLineAsync($"{keyword} \"{name}\"");
			}

			await writer.FlushAsync();
		}

		private static (uint Id, int Version) ReadMdlSignature(string path)
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (stream.Length < 8)
		{
			return (0, 0);
		}

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
		var id = reader.ReadUInt32();
		var version = reader.ReadInt32();
		return (id, version);
	}

	private static byte[] DecompressMdlzToGoldSrcIdst(string mdlPath)
	{
		var data = File.ReadAllBytes(mdlPath);
		if (data.Length < 8)
		{
			throw new InvalidDataException("File is too small to be a valid MDLZ.");
		}

		var maxOutputBytes = 256 * 1024 * 1024; // safety limit
		var offsets = new[] { 8, 12, 16, 20, 24, 28, 32, 36, 40 };

		foreach (var offset in offsets)
		{
			if (offset <= 0 || offset >= data.Length)
			{
				continue;
			}

			if (TryDecompressMdlz(data, offset, useZlib: true, maxOutputBytes, out var output) ||
				TryDecompressMdlz(data, offset, useZlib: false, maxOutputBytes, out output))
			{
				if (output.Length >= 8 &&
					BitConverter.ToUInt32(output, 0) == GoldSrcMdlReader.Idst &&
					BitConverter.ToInt32(output, 4) == 10)
				{
					return output;
				}
			}
		}

		throw new NotSupportedException("Unable to decompress MDLZ (expected GoldSrc IDST v10 payload).");
	}

	private static bool TryDecompressMdlz(byte[] data, int offset, bool useZlib, int maxOutputBytes, out byte[] output)
	{
		output = Array.Empty<byte>();

		try
		{
			using var input = new MemoryStream(data, offset, data.Length - offset, writable: false);
			using Stream decompressor = useZlib
				? (Stream)new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true)
				: new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);

			using var buffer = new MemoryStream();
			var temp = new byte[81920];

			while (true)
			{
				var read = decompressor.Read(temp, 0, temp.Length);
				if (read <= 0)
				{
					break;
				}

				buffer.Write(temp, 0, read);
				if (buffer.Length > maxOutputBytes)
				{
					return false;
				}
			}

			output = buffer.ToArray();
			return output.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	private static IReadOnlyList<string> CopyRelatedModelFiles(string mdlPath, string originalFolder)
	{
		var copied = new List<string>();

		var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
		var baseName = Path.GetFileNameWithoutExtension(mdlPath);
		var extension = Path.GetExtension(mdlPath);
		if (string.IsNullOrWhiteSpace(extension))
		{
			extension = ".mdl";
		}
		var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Path.GetFullPath(mdlPath),
			Path.Combine(directoryPath, baseName + ".mdl"),
			Path.Combine(directoryPath, baseName + "T" + extension),
			Path.Combine(directoryPath, baseName + ".ani"),
			Path.Combine(directoryPath, baseName + ".vvd"),
			Path.Combine(directoryPath, baseName + ".phy"),
			Path.Combine(directoryPath, baseName + ".vtx")
		};

		foreach (var vtxPath in Directory.EnumerateFiles(directoryPath, baseName + ".*.vtx"))
		{
			candidates.Add(vtxPath);
		}

		foreach (var sourcePath in candidates)
		{
			if (!File.Exists(sourcePath))
			{
				continue;
			}

			var fileName = Path.GetFileName(sourcePath);
			var destPath = Path.Combine(originalFolder, fileName);
			File.Copy(sourcePath, destPath, overwrite: true);
			copied.Add(fileName);
		}

		copied.Sort(StringComparer.OrdinalIgnoreCase);
		return copied;
	}

	private static string? FindVtxPath(string directoryPath, string baseName)
	{
		var preferredNames = new[]
		{
			baseName + ".dx11.vtx",
			baseName + ".dx90.vtx",
			baseName + ".dx80.vtx",
			baseName + ".sw.vtx",
			baseName + ".vtx"
		};

		foreach (var fileName in preferredNames)
		{
			var candidate = Path.Combine(directoryPath, fileName);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		// Fall back to any "<base>.*.vtx" (case-sensitive filesystems need an exact file name match).
		var any = Directory.EnumerateFiles(directoryPath, baseName + ".*.vtx").FirstOrDefault();
		return any;
	}

	private static async Task WriteReferenceSmdsAsync(
		string modelOutputFolder,
		string modelFileNamePrefix,
		MdlModel model,
		VvdFile vvd,
		VtxFile vtx,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		var globalVertexIndexStart = 0;

		for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
		{
			var mdlBodyPart = model.BodyParts[bodyPartIndex];
			var vtxBodyPart = bodyPartIndex < vtx.BodyParts.Count ? vtx.BodyParts[bodyPartIndex] : null;

			for (var modelIndex = 0; modelIndex < mdlBodyPart.Models.Count; modelIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var mdlSubModel = mdlBodyPart.Models[modelIndex];
				var vtxModel = (vtxBodyPart is not null && modelIndex < vtxBodyPart.Models.Count)
					? vtxBodyPart.Models[modelIndex]
					: new VtxModel(LodCount: 0, Lods: Array.Empty<VtxModelLod>());

				// Always write LOD0, even if VTX data is missing for this model (writes an empty triangles section).
				{
					const int lodIndex = 0;
					var smdFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex, lodIndex, options);
					var smdPathFileName = Path.Combine(modelOutputFolder, smdFileName);

					await WriteReferenceSmdAsync(
						pathFileName: smdPathFileName,
						model: model,
						mdlSubModel: mdlSubModel,
						vvd: vvd,
						vtxModel: vtxModel,
						lodIndex: lodIndex,
						globalVertexIndexStart: globalVertexIndexStart,
						options: options,
						cancellationToken: cancellationToken);
				}

				if (options.WriteLodMeshSmdFiles)
				{
					for (var lodIndex = 1; lodIndex < vtxModel.Lods.Count; lodIndex++)
					{
						var smdFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex, lodIndex, options);
						var smdPathFileName = Path.Combine(modelOutputFolder, smdFileName);

						await WriteReferenceSmdAsync(
							pathFileName: smdPathFileName,
							model: model,
							mdlSubModel: mdlSubModel,
							vvd: vvd,
							vtxModel: vtxModel,
							lodIndex: lodIndex,
							globalVertexIndexStart: globalVertexIndexStart,
							options: options,
							cancellationToken: cancellationToken);
					}
				}

				globalVertexIndexStart += Math.Max(0, mdlSubModel.VertexCount);
			}
		}
	}

	private static async Task WriteQcAsync(
		string modelOutputFolder,
		string modelFileNamePrefix,
		MdlModel model,
		VtxFile vtx,
		bool hasPhysics,
		string? proceduralBonesVrdFileName,
		DecompileOptions options,
		IReadOnlyDictionary<int, string>? animationSmdRelativePathFileNames,
		MdlVertexAnimationVtaWriter.VtaResult? vtaResult,
		CancellationToken cancellationToken)
	{
		var qcPathFileName = Path.Combine(modelOutputFolder, "model.qc");

		await using var stream = new FileStream(qcPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		var modelName = string.IsNullOrWhiteSpace(model.Header.Name)
			? Path.GetFileName(model.SourcePath)
			: model.Header.Name;

		await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$ModelName" : "$modelname")} \"{modelName.Replace("\"", "'")}\"");

		if ((model.Header.Flags & MdlConstants.StudioHdrFlagsStaticProp) != 0)
		{
			await writer.WriteLineAsync(options.QcUseMixedCaseForKeywords ? "$StaticProp" : "$staticprop");
		}

		var texturePaths = model.TexturePaths
			.Where(p => !string.IsNullOrWhiteSpace(p))
			.Select(p => p.Replace('\\', '/').Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
			.ToList();

		foreach (var texturePath in texturePaths)
		{
			await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$CDMaterials" : "$cdmaterials")} \"{texturePath.Replace("\"", "'")}\"");
		}

		await WriteTextureGroupAsync(writer, model, options, cancellationToken);

		if (options.QcIncludeDefineBoneLines)
		{
			if (options.QcGroupIntoQciFiles)
			{
				var qciFileName = "model_definebones.qci";
				var qciPathFileName = Path.Combine(modelOutputFolder, qciFileName);
				await WriteDefineBonesQciAsync(qciPathFileName, model, options, cancellationToken);

				await writer.WriteLineAsync();
				await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$Include" : "$include")} \"{qciFileName}\"");
			}
			else
			{
				await WriteDefineBonesAsync(writer, model, options, cancellationToken);
			}
		}

		if (!string.IsNullOrWhiteSpace(proceduralBonesVrdFileName))
		{
			var keyword = options.QcUseMixedCaseForKeywords ? "$ProceduralBones" : "$proceduralbones";
			var vrd = proceduralBonesVrdFileName.Replace("\"", "'");

			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"{keyword} \"{vrd}\"");
		}

		int? modelCommandBodyPartIndex = null;
		if (vtaResult is not null && vtaResult.FlexFrames.Count > 1)
		{
			modelCommandBodyPartIndex = FindModelCommandBodyPartIndex(model);
		}

			for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
			{
				var bodyPart = model.BodyParts[bodyPartIndex];
				if (modelCommandBodyPartIndex == bodyPartIndex)
				{
				var modelNameForCommand = string.IsNullOrWhiteSpace(bodyPart.Name) ? $"bodypart{bodyPartIndex}" : bodyPart.Name;
				modelNameForCommand = modelNameForCommand.Replace("\"", "'");

					var smdFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex: 0, lodIndex: 0, options);
					await writer.WriteLineAsync();
					await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$Model" : "$model")} \"{modelNameForCommand}\" \"{smdFileName}\"");
					await writer.WriteLineAsync("{");

				if (vtaResult is not null)
				{
					await WriteFlexGroupAsync(writer, model, vtaResult, indent: "    ", cancellationToken);
				}

				await writer.WriteLineAsync("}");
				continue;
			}

			var bodyGroupName = string.IsNullOrWhiteSpace(bodyPart.Name) ? $"bodypart{bodyPartIndex}" : bodyPart.Name;
			bodyGroupName = bodyGroupName.Replace("\"", "'");

			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$BodyGroup" : "$bodygroup")} \"{bodyGroupName}\"");
				await writer.WriteLineAsync("{");
				for (var modelIndex = 0; modelIndex < bodyPart.Models.Count; modelIndex++)
				{
					var smdFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex, lodIndex: 0, options);
					await writer.WriteLineAsync($"    studio \"{smdFileName}\"");
				}
				await writer.WriteLineAsync("}");
			}

			WriteLodGroups(writer, modelFileNamePrefix, options, model, vtx);

		if (modelCommandBodyPartIndex is null && vtaResult is not null)
		{
			await WriteFlexGroupAsync(writer, model, vtaResult, indent: string.Empty, cancellationToken);
		}

		var wroteAnySequences = false;
		if ((model.Header.Flags & MdlConstants.StudioHdrFlagsStaticProp) == 0 &&
			animationSmdRelativePathFileNames is not null &&
			animationSmdRelativePathFileNames.Count > 0 &&
			model.SequenceDescs.Count > 0)
		{
			var sequenceKeyword = options.QcUseMixedCaseForKeywords ? "$Sequence" : "$sequence";

			for (var i = 0; i < model.SequenceDescs.Count; i++)
			{
				var seq = model.SequenceDescs[i];
				var animIndex = seq.AnimDescIndexes.Count > 0 ? seq.AnimDescIndexes[0] : (short)-1;
				if (animIndex < 0)
				{
					continue;
				}

				if (!animationSmdRelativePathFileNames.TryGetValue(animIndex, out var animSmdRelativePathFileName))
				{
					continue;
				}

				var sequenceName = string.IsNullOrWhiteSpace(seq.Name) ? $"sequence_{seq.Index}" : seq.Name;
				sequenceName = sequenceName.Replace("\"", "'");
				animSmdRelativePathFileName = animSmdRelativePathFileName.Replace("\"", "'");

				await writer.WriteLineAsync();
				await writer.WriteLineAsync($"{sequenceKeyword} \"{sequenceName}\" \"{animSmdRelativePathFileName}\"");
				wroteAnySequences = true;
			}
		}

		if (!wroteAnySequences && (model.Header.Flags & MdlConstants.StudioHdrFlagsStaticProp) == 0)
		{
			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$Sequence" : "$sequence")} \"idle\" \"skeleton.smd\"");
		}

		if (hasPhysics)
		{
			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$CollisionModel" : "$collisionmodel")} \"physics.smd\"");
			await writer.WriteLineAsync("{");
			await writer.WriteLineAsync($"    {(options.QcUseMixedCaseForKeywords ? "$Mass" : "$mass")} 1");
			await writer.WriteLineAsync("}");
		}

		var hasShadowLodFlag = (model.Header.Flags & MdlConstants.StudioHdrFlagsHasShadowLod) != 0 || (model.Header.Flags & MdlConstants.StudioHdrFlagsUseShadowlodMaterials) != 0;
		var hasLod1 = vtx.BodyParts.Any(bp => bp.Models.Any(m => m.Lods.Count > 1));
		if (hasShadowLodFlag && hasLod1)
		{
			var firstBodyPart = model.BodyParts.FirstOrDefault();
			var bodyName = string.IsNullOrWhiteSpace(firstBodyPart?.Name) ? "bodypart0" : firstBodyPart!.Name.Replace("\"", "'");
			var shadowLodSmd = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex: 0, modelIndex: 0, lodIndex: 1, options);

			await writer.WriteLineAsync();
			await writer.WriteLineAsync(options.QcUseMixedCaseForKeywords ? "$ShadowLOD" : "$shadowlod");
			await writer.WriteLineAsync("{");
			await writer.WriteLineAsync($"    replacemodel \"{bodyName}\" \"{shadowLodSmd}\"");
			await writer.WriteLineAsync("}");
		}

		await writer.FlushAsync();
	}

	private static async Task WriteTextureGroupAsync(StreamWriter writer, MdlModel model, DecompileOptions options, CancellationToken cancellationToken)
	{
		if (model.SkinFamilies.Count == 0 || model.Header.SkinReferenceCount <= 0 || model.Textures.Count == 0)
		{
			return;
		}

		List<List<int>> skinFamiliesTextureIndexes;
		if (options.QcOnlyChangedMaterialsInTextureGroupLines)
		{
			skinFamiliesTextureIndexes = GetSkinFamiliesOfChangedMaterials(model.SkinFamilies);
		}
		else
		{
			skinFamiliesTextureIndexes = model.SkinFamilies
				.Select(family => family.TextureIndexes.Select(index => index).ToList())
				.ToList();
		}

		var skinFamilies = new List<List<string>>(skinFamiliesTextureIndexes.Count);
		for (var i = 0; i < skinFamiliesTextureIndexes.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var familyIndexes = skinFamiliesTextureIndexes[i];
			var textureFileNames = new List<string>(familyIndexes.Count);
			for (var j = 0; j < familyIndexes.Count; j++)
			{
				var material = GetMaterialName(model.Textures, familyIndexes[j]).Replace("\"", "'");
				textureFileNames.Add(material);
			}

			skinFamilies.Add(textureFileNames);
		}

		if ((!options.QcOnlyChangedMaterialsInTextureGroupLines) || skinFamilies.Count > 1)
		{
			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"{(options.QcUseMixedCaseForKeywords ? "$TextureGroup" : "$texturegroup")} \"skinfamilies\"");
			await writer.WriteLineAsync("{");

			var lines = GetTextureGroupSkinFamilyLines(skinFamilies, options);
			foreach (var line in lines)
			{
				await writer.WriteLineAsync(line);
			}

			await writer.WriteLineAsync("}");
		}
	}

	private static List<List<int>> GetSkinFamiliesOfChangedMaterials(IReadOnlyList<MdlSkinFamily> skinFamilies)
	{
		if (skinFamilies.Count == 0)
		{
			return new List<List<int>>();
		}

		var skinReferenceCount = skinFamilies[0].TextureIndexes.Count;
		var processed = new List<List<int>>(skinFamilies.Count);
		for (var i = 0; i < skinFamilies.Count; i++)
		{
			processed.Add(new List<int>(skinReferenceCount));
		}

		var first = skinFamilies[0].TextureIndexes;
		for (var j = 0; j < skinReferenceCount; j++)
		{
			for (var i = 1; i < skinFamilies.Count; i++)
			{
				var candidate = skinFamilies[i].TextureIndexes;
				if (j >= candidate.Count)
				{
					continue;
				}

				if (first[j] != candidate[j])
				{
					for (var skinFamilyIndex = 0; skinFamilyIndex < skinFamilies.Count; skinFamilyIndex++)
					{
						var familyIndexes = skinFamilies[skinFamilyIndex].TextureIndexes;
						if (j < familyIndexes.Count)
						{
							processed[skinFamilyIndex].Add(familyIndexes[j]);
						}
					}

					break;
				}
			}
		}

		return processed;
	}

	private static IReadOnlyList<string> GetTextureGroupSkinFamilyLines(List<List<string>> skinFamilies, DecompileOptions options)
	{
		var lines = new List<string>();
		if (skinFamilies.Count == 0)
		{
			return lines;
		}

		if (options.QcSkinFamilyOnSingleLine)
		{
			var columnMax = new List<int>();
			var first = skinFamilies[0];
			for (var i = 0; i < first.Count; i++)
			{
				columnMax.Add(first[i].Length);
			}

			for (var familyIndex = 1; familyIndex < skinFamilies.Count; familyIndex++)
			{
				var family = skinFamilies[familyIndex];
				for (var col = 0; col < family.Count; col++)
				{
					var len = family[col].Length;
					if (col >= columnMax.Count)
					{
						columnMax.Add(len);
					}
					else if (len > columnMax[col])
					{
						columnMax[col] = len;
					}
				}
			}

			for (var familyIndex = 0; familyIndex < skinFamilies.Count; familyIndex++)
			{
				var family = skinFamilies[familyIndex];
				var parts = new List<string>(capacity: family.Count);
				for (var col = 0; col < family.Count; col++)
				{
					var maxLen = col < columnMax.Count ? columnMax[col] : family[col].Length;
					parts.Add(("\"" + family[col] + "\"").PadRight(maxLen + 3));
				}

				lines.Add($"    {{ {string.Concat(parts)} }}");
			}
		}
		else
		{
			for (var familyIndex = 0; familyIndex < skinFamilies.Count; familyIndex++)
			{
				var family = skinFamilies[familyIndex];
				lines.Add("    {");
				for (var col = 0; col < family.Count; col++)
				{
					lines.Add($"        \"{family[col]}\"");
				}
				lines.Add("    }");
			}
		}

		return lines;
	}

	private static async Task WriteDefineBonesQciAsync(string qciPathFileName, MdlModel model, DecompileOptions options, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(qciPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);
		await DecompileFormat.WriteHeaderCommentAsync(writer, options);
		await WriteDefineBonesAsync(writer, model, options, cancellationToken, includeLeadingBlankLine: false);
		await writer.FlushAsync();
	}

	private static async Task WriteDefineBonesAsync(StreamWriter writer, MdlModel model, DecompileOptions options, CancellationToken cancellationToken, bool includeLeadingBlankLine = true)
	{
		if (model.Bones.Count == 0)
		{
			return;
		}

		if (includeLeadingBlankLine)
		{
			await writer.WriteLineAsync();
		}

		var keyword = options.QcUseMixedCaseForKeywords ? "$DefineBone" : "$definebone";
		var format = CultureInfo.InvariantCulture;
		var degrees = 180.0f / MathF.PI;

		for (var i = 0; i < model.Bones.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = model.Bones[i];
			var boneName = bone.Name.Replace("\"", "'");

			var parentName = string.Empty;
			if (bone.ParentIndex >= 0 && bone.ParentIndex < model.Bones.Count)
			{
				parentName = model.Bones[bone.ParentIndex].Name.Replace("\"", "'");
			}

			var p = bone.Position;
			var r = bone.RotationRadians;

			var ry = model.Header.Version == 2531 ? 0 : r.Y * degrees;
			var rz = model.Header.Version == 2531 ? 0 : r.Z * degrees;
			var rx = model.Header.Version == 2531 ? 0 : r.X * degrees;

			var line = string.Format(
				format,
				"{0} \"{1}\" \"{2}\" {3:0.######} {4:0.######} {5:0.######} {6:0.######} {7:0.######} {8:0.######} 0 0 0 0 0 0",
				keyword,
				boneName,
				parentName,
				p.X, p.Y, p.Z,
				ry, rz, rx);

			await writer.WriteLineAsync(line);
		}
	}

	private static int? FindModelCommandBodyPartIndex(MdlModel model)
	{
		for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
		{
			var bodyPart = model.BodyParts[bodyPartIndex];
			if (bodyPart.Models.Count != 1)
			{
				continue;
			}

			var subModel = bodyPart.Models[0];
			var hasFlexes = subModel.Meshes.Any(mesh => mesh.Flexes.Any(flex => flex.VertAnims.Count > 0));
			if (hasFlexes)
			{
				return bodyPartIndex;
			}
		}

		return null;
	}

	private static async Task WriteFlexGroupAsync(
		StreamWriter writer,
		MdlModel model,
		MdlVertexAnimationVtaWriter.VtaResult vtaResult,
		string indent,
		CancellationToken cancellationToken)
	{
		if (vtaResult.FlexFrames.Count <= 1)
		{
			return;
		}

		var safeVtaFileName = vtaResult.VtaFileName.Replace("\"", "'");
		var innerIndent = string.IsNullOrWhiteSpace(indent) ? "    " : indent + "    ";

		await writer.WriteLineAsync();
		await writer.WriteLineAsync($"{indent}flexfile \"{safeVtaFileName}\"");
		await writer.WriteLineAsync($"{indent}{{");
		await writer.WriteLineAsync($"{innerIndent}defaultflex frame 0");
		for (var frameIndex = 1; frameIndex < vtaResult.FlexFrames.Count; frameIndex++)
		{
			var frame = vtaResult.FlexFrames[frameIndex];
			var flexName = (frame.FlexName ?? string.Empty).Replace("\"", "'");
			if (string.IsNullOrWhiteSpace(flexName))
			{
				flexName = $"flex_{frame.FlexDescIndex}";
			}

			if (frame.FlexHasPartner && flexName.Length > 1)
			{
				await writer.WriteLineAsync($"{innerIndent}flexpair \"{flexName[..^1]}\" {frame.FlexSplit.ToString("0.######", CultureInfo.InvariantCulture)} frame {frameIndex}");
			}
			else
			{
				await writer.WriteLineAsync($"{innerIndent}flex \"{flexName}\" frame {frameIndex}");
			}
		}
		await writer.WriteLineAsync($"{indent}}}");

		if (model.FlexControllers.Count > 0)
		{
			await writer.WriteLineAsync();

			var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (var i = 0; i < model.FlexControllers.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var controller = model.FlexControllers[i];
				var name = (controller.Name ?? string.Empty).Replace("\"", "'");
				if (string.IsNullOrWhiteSpace(name))
				{
					name = $"controller_{controller.Index}";
				}

				var type = (controller.Type ?? string.Empty).Replace("\"", "'");
				if (string.IsNullOrWhiteSpace(type))
				{
					type = "unknown";
				}

				var line = $"{indent}flexcontroller {type} range {controller.Min.ToString("0.######", CultureInfo.InvariantCulture)} {controller.Max.ToString("0.######", CultureInfo.InvariantCulture)} \"{name}\"";

				if (!seenNames.Add(name))
				{
					await writer.WriteLineAsync($"{indent}// Duplicate flexcontroller from original model; commented out to avoid tool issues.");
					await writer.WriteLineAsync($"{indent}// {line.TrimStart()}");
					continue;
				}

				await writer.WriteLineAsync(line);
			}
		}

		if (model.FlexRules.Count > 0)
		{
			await writer.WriteLineAsync();

			var usedByFlex = new HashSet<int>();
			for (var i = 1; i < vtaResult.FlexFrames.Count; i++)
			{
				var frame = vtaResult.FlexFrames[i];
				if (frame.FlexDescIndex >= 0)
				{
					usedByFlex.Add(frame.FlexDescIndex);
				}

				for (var x = 0; x < frame.Flexes.Count; x++)
				{
					var (flex, _) = frame.Flexes[x];
					usedByFlex.Add(flex.FlexDescIndex);
					if (flex.FlexDescPartnerIndex > 0)
					{
						usedByFlex.Add(flex.FlexDescPartnerIndex);
					}
				}
			}

			var usedByRule = new HashSet<int>();
			for (var i = 0; i < model.FlexRules.Count; i++)
			{
				var rule = model.FlexRules[i];
				usedByRule.Add(rule.FlexIndex);
				for (var j = 0; j < rule.Ops.Count; j++)
				{
					var op = rule.Ops[j];
					if (op.Op == 3) // STUDIO_FETCH2
					{
						usedByRule.Add(op.Index);
					}
				}
			}

			for (var i = 0; i < model.FlexDescs.Count; i++)
			{
				if (usedByRule.Contains(i) && !usedByFlex.Contains(i))
				{
					var flexName = (model.FlexDescs[i].Name ?? string.Empty).Replace("\"", "'");
					if (string.IsNullOrWhiteSpace(flexName))
					{
						flexName = $"flex_{i}";
					}

					await writer.WriteLineAsync($"{indent}localvar {flexName}");
				}
			}

			for (var i = 0; i < model.FlexRules.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var rule = model.FlexRules[i];
				var line = MdlFlexRuleFormatter.GetFlexRuleLine(model.FlexDescs, model.FlexControllers, rule, indent);
				await writer.WriteLineAsync(line);
			}
		}
	}

	private static async Task<bool> WritePhysicsSmdAsync(
		string pathFileName,
		MdlModel model,
		PhyFile phy,
		DecompileOptions options,
		AccessedBytesLog? mdlAccessedBytesLog,
		AccessedBytesDebugLogs? accessedBytesDebugLogs,
		CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		var phyLinePrefix = options.IndentPhysicsTriangles ? "  phy" : "phy";

		var bones = model.Bones;
		var format = CultureInfo.InvariantCulture;
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync("version 1");
		await writer.WriteLineAsync("nodes");
		for (var index = 0; index < bones.Count; index++)
		{
			var bone = bones[index];
			var name = bone.Name.Replace("\"", "'");
			await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0");
		for (var index = 0; index < bones.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = bones[index];
			var p = bone.Position;
			var r = bone.RotationRadians;

			await writer.WriteLineAsync(string.Format(
				format,
				"{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######} {5:0.######} {6:0.######}",
				bone.Index,
				p.X, p.Y, p.Z,
				r.X, r.Y, r.Z));
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("triangles");

		var singleSolid = phy.Header.SolidCount == 1;
		var needsWorldToPose = singleSolid && model.Header.Version is < 44 or > 47;
		var worldToPose = needsWorldToPose
			? BuildWorldToPoseForPhysics(model, mdlAccessedBytesLog, accessedBytesDebugLogs, cancellationToken)
			: Matrix4x4.Identity;

		var hasAnyTriangles = false;
		for (var solidIndex = 0; solidIndex < phy.Solids.Count; solidIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var solid = phy.Solids[solidIndex];
			var vertices = solid.Vertices;

			for (var meshIndex = 0; meshIndex < solid.ConvexMeshes.Count; meshIndex++)
			{
				var mesh = solid.ConvexMeshes[meshIndex];

				// Match old Crowbar: skip convex meshes that have children.
				if ((mesh.Flags & 3) > 0)
				{
					continue;
				}

				var boneIndex = GetPhysicsBoneIndex(bones, mesh.BoneIndex);
				var bone = (uint)boneIndex < (uint)bones.Count ? bones[boneIndex] : bones[0];

				// Match old Crowbar: compute per-vertex normals in PHY space (meters, IVP axes).
				var vertexNormals = new Vector3[vertices.Count];
				for (var faceIndex = 0; faceIndex < mesh.Faces.Count; faceIndex++)
				{
					var face = mesh.Faces[faceIndex];
					var i0 = (int)face.VertexIndex0;
					var i1 = (int)face.VertexIndex1;
					var i2 = (int)face.VertexIndex2;

					if ((uint)i0 >= (uint)vertices.Count ||
						(uint)i1 >= (uint)vertices.Count ||
						(uint)i2 >= (uint)vertices.Count)
					{
						continue;
					}

					var v0 = vertices[i0];
					var v1 = vertices[i1];
					var v2 = vertices[i2];
					var vector0 = v0 - v1;
					var vector1 = v1 - v2;
					var normal = Vector3.Cross(vector0, vector1);

					vertexNormals[i0] += normal;
					vertexNormals[i1] += normal;
					vertexNormals[i2] += normal;
				}

				for (var i = 0; i < vertexNormals.Length; i++)
				{
					var n = vertexNormals[i];
					vertexNormals[i] = n.LengthSquared() > 0 ? Vector3.Normalize(n) : default;
				}

				for (var faceIndex = 0; faceIndex < mesh.Faces.Count; faceIndex++)
				{
					var face = mesh.Faces[faceIndex];
					var i0 = (int)face.VertexIndex0;
					var i1 = (int)face.VertexIndex1;
					var i2 = (int)face.VertexIndex2;

					if ((uint)i0 >= (uint)vertices.Count ||
						(uint)i1 >= (uint)vertices.Count ||
						(uint)i2 >= (uint)vertices.Count)
					{
						continue;
					}

					var p0 = TransformPhyPosition(model, singleSolid, worldToPose, bone, vertices[i0]);
					var p1 = TransformPhyPosition(model, singleSolid, worldToPose, bone, vertices[i1]);
					var p2 = TransformPhyPosition(model, singleSolid, worldToPose, bone, vertices[i2]);

					var faceNormal = Vector3.Normalize(Vector3.Cross(p0 - p1, p1 - p2));
					if (float.IsNaN(faceNormal.X) || float.IsNaN(faceNormal.Y) || float.IsNaN(faceNormal.Z))
					{
						faceNormal = Vector3.Zero;
					}

					var (normal0, normal1, normal2) = model.Header.Version is >= 44 and <= 47
						? (vertexNormals[i0], vertexNormals[i1], vertexNormals[i2])
						: (vertexNormals[i0], vertexNormals[i1], vertexNormals[i2]);

					await writer.WriteLineAsync(phyLinePrefix);
					await writer.WriteLineAsync(FormatPhysicsVertexLine(boneIndex, p0, normal0));
					await writer.WriteLineAsync(FormatPhysicsVertexLine(boneIndex, p1, normal1));
					await writer.WriteLineAsync(FormatPhysicsVertexLine(boneIndex, p2, normal2));
					hasAnyTriangles = true;
				}
			}
		}

		await writer.WriteLineAsync("end");
		await writer.FlushAsync();

		return hasAnyTriangles;
	}

	private static int GetPhysicsBoneIndex(IReadOnlyList<MdlBone> bones, int boneIndexFromPhy)
	{
		if (bones.Count <= 1)
		{
			return 0;
		}

		if (boneIndexFromPhy < 0)
		{
			return 0;
		}

		if ((uint)boneIndexFromPhy < (uint)bones.Count)
		{
			return boneIndexFromPhy;
		}

		return 0;
	}

	private static Vector3 TransformPhyPosition(MdlModel model, bool singleSolid, Matrix4x4 worldToPose, MdlBone bone, Vector3 metersIvp)
	{
		const float sourceUnitsPerMeter = 1f / 0.0254f;

		// Match old Crowbar (VB) logic for PHY vertex transforms.
		if (model.Header.Version is >= 44 and <= 47)
		{
			if (singleSolid)
			{
				return new Vector3(
					metersIvp.Z * sourceUnitsPerMeter,
					-metersIvp.X * sourceUnitsPerMeter,
					-metersIvp.Y * sourceUnitsPerMeter);
			}

			var toBoneSpace = new Vector3(
				metersIvp.X * sourceUnitsPerMeter,
				metersIvp.Z * sourceUnitsPerMeter,
				-metersIvp.Y * sourceUnitsPerMeter);

			return VectorITransform(toBoneSpace, bone.PoseToBone);
		}

		if (singleSolid)
		{
			if ((model.Header.Flags & MdlConstants.StudioHdrFlagsStaticProp) != 0)
			{
				var toWorldSpace = new Vector3(
					metersIvp.Z * sourceUnitsPerMeter,
					-metersIvp.X * sourceUnitsPerMeter,
					-metersIvp.Y * sourceUnitsPerMeter);

				var poseSpace = VectorTransform(toWorldSpace, worldToPose);

				// Static prop quirk: swap to match the reference SMD coordinate adjustment.
				poseSpace = new Vector3(poseSpace.X, poseSpace.Z, -poseSpace.Y);

				return VectorITransform(poseSpace, bone.PoseToBone);
			}
			else
			{
				var toWorldSpace = new Vector3(
					metersIvp.X * sourceUnitsPerMeter,
					-metersIvp.Y * sourceUnitsPerMeter,
					-metersIvp.Z * sourceUnitsPerMeter);

				var poseSpace = VectorTransform(toWorldSpace, worldToPose);
				return VectorITransform(poseSpace, bone.PoseToBone);
			}
		}

		// Source physics uses meters and IVP axes; this matches Crowbar's ConvertToBoneSpace baseline.
		var position = new Vector3(
			metersIvp.X * sourceUnitsPerMeter,
			metersIvp.Z * sourceUnitsPerMeter,
			-metersIvp.Y * sourceUnitsPerMeter);

		return VectorITransform(position, bone.PoseToBone);
	}

	private static Vector3 TransformPhyNormal(MdlModel model, bool singleSolid, Matrix4x4 worldToPose, MdlBone bone, Vector3 normal)
	{
		if (normal == default)
		{
			return normal;
		}

		if (model.Header.Version is >= 44 and <= 47)
		{
			// Apply the same axis remap used for positions (no translation).
			var mapped = singleSolid
				? new Vector3(normal.Z, -normal.X, -normal.Y)
				: new Vector3(normal.X, normal.Z, -normal.Y);

			if (!singleSolid)
			{
				mapped = VectorIRotate(mapped, bone.PoseToBone);
			}

			return mapped.LengthSquared() > 0 ? Vector3.Normalize(mapped) : mapped;
		}

		if (singleSolid)
		{
			if ((model.Header.Flags & MdlConstants.StudioHdrFlagsStaticProp) != 0)
			{
				var toWorld = new Vector3(normal.Z, -normal.X, -normal.Y);
				var poseSpace = VectorTransformNoTranslation(toWorld, worldToPose);
				poseSpace = new Vector3(poseSpace.X, poseSpace.Z, -poseSpace.Y);
				var rotated = VectorIRotate(poseSpace, bone.PoseToBone);
				return rotated.LengthSquared() > 0 ? Vector3.Normalize(rotated) : rotated;
			}

			var toWorldSpace = new Vector3(normal.X, -normal.Y, -normal.Z);
			var poseSpaceDefault = VectorTransformNoTranslation(toWorldSpace, worldToPose);
			var rotatedDefault = VectorIRotate(poseSpaceDefault, bone.PoseToBone);
			return rotatedDefault.LengthSquared() > 0 ? Vector3.Normalize(rotatedDefault) : rotatedDefault;
		}

		var mappedNormal = new Vector3(normal.X, normal.Z, -normal.Y);
		mappedNormal = VectorIRotate(mappedNormal, bone.PoseToBone);
		return mappedNormal.LengthSquared() > 0 ? Vector3.Normalize(mappedNormal) : mappedNormal;
	}

	private static Matrix4x4 BuildWorldToPoseForPhysics(
		MdlModel model,
		AccessedBytesLog? mdlAccessedBytesLog,
		AccessedBytesDebugLogs? accessedBytesDebugLogs,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Match Crowbar's ProcessTransformsForPhysics: prefer first animDesc frame 0, fall back to root bone base pose.
		var rootPosition = model.Bones.Count > 0 ? model.Bones[0].Position : Vector3.Zero;
		var rootRotation = model.Bones.Count > 0 ? model.Bones[0].RotationRadians : Vector3.Zero;

		if (MdlAnimationSmdWriter.TryGetFirstAnimationDescFrame0RootTransform(
			model.SourcePath,
			model,
			out var animPosition,
			out var animRotation,
			mdlAccessedBytesLog,
			accessedBytesDebugLogs))
		{
			rootPosition = animPosition;
			rootRotation = animRotation;
		}

		// Crowbar adjusts by -90 degrees on Z and swaps X/Y for translation before invert.
		rootRotation = new Vector3(rootRotation.X, rootRotation.Y, rootRotation.Z - MathF.PI / 2f);
		var translationX = model.Header.Version <= 47 ? MathF.Abs(rootPosition.Y) : rootPosition.Y;
		var poseToWorld = AngleMatrix(
			pitchRadians: rootRotation.X,
			yawRadians: rootRotation.Y,
			rollRadians: rootRotation.Z,
			translation: new Vector3(translationX, -rootPosition.X, rootPosition.Z));

		return Matrix3x4Invert(poseToWorld);
	}

	private static Matrix4x4 AngleMatrix(float pitchRadians, float yawRadians, float rollRadians, Vector3 translation)
	{
		var sy = MathF.Sin(yawRadians);
		var cy = MathF.Cos(yawRadians);
		var sp = MathF.Sin(pitchRadians);
		var cp = MathF.Cos(pitchRadians);
		var sr = MathF.Sin(rollRadians);
		var cr = MathF.Cos(rollRadians);

		// Source AngleMatrix stores basis vectors in columns.
		var c0 = new Vector3(cp * cy, cp * sy, -sp);
		var c1 = new Vector3(sr * sp * cy + cr * -sy, sr * sp * sy + cr * cy, sr * cp);
		var c2 = new Vector3(cr * sp * cy + -sr * -sy, cr * sp * sy + -sr * cy, cr * cp);

		// Store as row-major 3x4 (translation in M14/M24/M34), matching PoseToBone readout.
		return new Matrix4x4(
			c0.X, c1.X, c2.X, translation.X,
			c0.Y, c1.Y, c2.Y, translation.Y,
			c0.Z, c1.Z, c2.Z, translation.Z,
			0, 0, 0, 1);
	}

	private static Matrix4x4 Matrix3x4Invert(Matrix4x4 matrix)
	{
		// Match Crowbar's MathModule.MatrixInvert() behavior for orthonormal matrix3x4_t.
		// Note: Crowbar's implementation computes translation using the transposed columns (rows of input).
		var r00 = matrix.M11;
		var r01 = matrix.M12;
		var r02 = matrix.M13;
		var r10 = matrix.M21;
		var r11 = matrix.M22;
		var r12 = matrix.M23;
		var r20 = matrix.M31;
		var r21 = matrix.M32;
		var r22 = matrix.M33;

		var t0 = matrix.M14;
		var t1 = matrix.M24;
		var t2 = matrix.M34;

		// Transpose rotation.
		var i00 = r00;
		var i01 = r10;
		var i02 = r20;
		var i10 = r01;
		var i11 = r11;
		var i12 = r21;
		var i20 = r02;
		var i21 = r12;
		var i22 = r22;

		// Crowbar translation: -Dot(t, out_colN) where out_colN are the transposed columns.
		var itx = -(t0 * r00 + t1 * r01 + t2 * r02);
		var ity = -(t0 * r10 + t1 * r11 + t2 * r12);
		var itz = -(t0 * r20 + t1 * r21 + t2 * r22);

		return new Matrix4x4(
			i00, i01, i02, itx,
			i10, i11, i12, ity,
			i20, i21, i22, itz,
			0, 0, 0, 1);
	}

	private static Vector3 VectorTransform(Vector3 input, Matrix4x4 matrix)
	{
		// Source VectorTransform for matrix3x4_t (row-major with translation in column 3).
		return new Vector3(
			input.X * matrix.M11 + input.Y * matrix.M12 + input.Z * matrix.M13 + matrix.M14,
			input.X * matrix.M21 + input.Y * matrix.M22 + input.Z * matrix.M23 + matrix.M24,
			input.X * matrix.M31 + input.Y * matrix.M32 + input.Z * matrix.M33 + matrix.M34);
	}

	private static Vector3 VectorITransform(Vector3 input, Matrix4x4 matrix)
	{
		var temp = input - new Vector3(matrix.M14, matrix.M24, matrix.M34);
		return new Vector3(
			temp.X * matrix.M11 + temp.Y * matrix.M21 + temp.Z * matrix.M31,
			temp.X * matrix.M12 + temp.Y * matrix.M22 + temp.Z * matrix.M32,
			temp.X * matrix.M13 + temp.Y * matrix.M23 + temp.Z * matrix.M33);
	}

	private static Vector3 VectorIRotate(Vector3 input, Matrix4x4 matrix)
	{
		return new Vector3(
			input.X * matrix.M11 + input.Y * matrix.M21 + input.Z * matrix.M31,
			input.X * matrix.M12 + input.Y * matrix.M22 + input.Z * matrix.M32,
			input.X * matrix.M13 + input.Y * matrix.M23 + input.Z * matrix.M33);
	}

	private static Vector3 VectorTransformNoTranslation(Vector3 input, Matrix4x4 matrix)
	{
		return new Vector3(
			input.X * matrix.M11 + input.Y * matrix.M12 + input.Z * matrix.M13,
			input.X * matrix.M21 + input.Y * matrix.M22 + input.Z * matrix.M23,
			input.X * matrix.M31 + input.Y * matrix.M32 + input.Z * matrix.M33);
	}

	private static string FormatPhysicsVertexLine(int boneIndex, Vector3 position, Vector3 normal)
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"    {0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000} 0 0",
			boneIndex,
			position.X, position.Y, position.Z,
			normal.X, normal.Y, normal.Z);
	}

	private static void WriteLodGroups(StreamWriter writer, string modelFileNamePrefix, DecompileOptions options, MdlModel model, VtxFile vtx)
	{
		if (!options.WriteLodMeshSmdFiles)
		{
			return;
		}

		const float shadowSwitchPoint = -1f;
		const float epsilon = 0.0001f;
		var useShadowLodMaterials = (model.Header.Flags & MdlConstants.StudioHdrFlagsUseShadowlodMaterials) != 0;

		var groups = new SortedDictionary<float, LodGroup>();

		var bodyPartCount = Math.Min(model.BodyParts.Count, vtx.BodyParts.Count);
		for (var bodyPartIndex = 0; bodyPartIndex < bodyPartCount; bodyPartIndex++)
		{
			var mdlBodyPart = model.BodyParts[bodyPartIndex];
			var vtxBodyPart = vtx.BodyParts[bodyPartIndex];

			var modelCount = Math.Min(mdlBodyPart.Models.Count, vtxBodyPart.Models.Count);
			for (var modelIndex = 0; modelIndex < modelCount; modelIndex++)
			{
				var mdlSubModel = mdlBodyPart.Models[modelIndex];
				var vtxModel = vtxBodyPart.Models[modelIndex];

				// Skip empty bodygroups (mirrors Crowbar behavior).
				if (string.IsNullOrWhiteSpace(mdlSubModel.Name) &&
				    vtxModel.Lods.Count > 0 &&
				    vtxModel.Lods[0].Meshes.Count == 0)
				{
					continue;
				}

				for (var lodIndex = 1; lodIndex < vtxModel.Lods.Count; lodIndex++)
				{
					{
						var switchPoint = vtxModel.Lods[lodIndex].SwitchPoint;
						var referenceFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex, lodIndex: 0, options);
						var lodFileName = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex, modelIndex, lodIndex, options);

						if (!groups.TryGetValue(switchPoint, out var group))
						{
							group = new LodGroup();
							groups.Add(switchPoint, group);
						}

						group.Models.Add((referenceFileName, lodFileName));
						group.LodIndexes.Add(lodIndex);
						group.UsesFacial |= vtxModel.Lods[lodIndex].UsesFacial;
					}
				}
			}
		}

		if (groups.Count == 0)
		{
			return;
		}

		writer.WriteLine();

		// Separate out shadow LOD (switchPoint == -1). Some models omit it; keep fallback below.
		LodGroup? shadowLodGroup = null;
		if (groups.TryGetValue(shadowSwitchPoint, out var shadowGroup))
		{
			shadowLodGroup = shadowGroup;
		}

		// Regular LODs: ignore negative switch points (other than -1) and keep ascending order.
		var lodGroups = new SortedDictionary<float, LodGroup>();
		foreach (var kvp in groups)
		{
			if (Math.Abs(kvp.Key + 1f) < epsilon)
			{
				continue;
			}

			if (kvp.Key <= 0f)
			{
				continue;
			}

			lodGroups[kvp.Key] = kvp.Value;
		}

		var lodKeys = lodGroups.Keys
			.Where(k => k > 0f)
			.OrderBy(k => k)
			.ToList();

		for (var i = 0; i < lodKeys.Count; i++)
		{
			var switchPoint = lodKeys[i];
			if (!lodGroups.TryGetValue(switchPoint, out var group))
			{
				continue;
			}

			writer.WriteLine($"$lod {switchPoint.ToString("0.######", CultureInfo.InvariantCulture)}");
			writer.WriteLine("{");
			foreach (var (referenceFileName, lodFileName) in group.Models)
			{
				writer.WriteLine($"    replacemodel \"{referenceFileName}\" \"{lodFileName}\"");
			}

			WriteMaterialReplacements(writer, model, vtx, group.LodIndexes);

			var lodIndexForBones = group.LodIndexes.OrderBy(i => i).FirstOrDefault(1);
			WriteReplaceBones(writer, model.Bones, lodIndexForBones);

			writer.WriteLine(group.UsesFacial ? "    facial" : "    nofacial");

			if (useShadowLodMaterials)
			{
				writer.WriteLine("    use_shadowlod_materials");
			}

			writer.WriteLine("}");
		}

		// Prefer an explicit shadow LOD group (switchPoint == -1). Otherwise, fall back to the
		// highest available LOD +1 if present (matches Crowbar's behavior when no shadow switch is stored).
		if (shadowLodGroup is not null && shadowLodGroup.Models.Count > 0)
		{
			writer.WriteLine("$shadowlod");
			writer.WriteLine("{");
			foreach (var (referenceFileName, lodFileName) in shadowLodGroup.Models)
			{
				writer.WriteLine($"    replacemodel \"{referenceFileName}\" \"{lodFileName}\"");
			}

			WriteMaterialReplacements(writer, model, vtx, shadowLodGroup.LodIndexes);
			var shadowBoneLodIndex = shadowLodGroup.LodIndexes.OrderBy(i => i).FirstOrDefault(1);
			WriteReplaceBones(writer, model.Bones, shadowBoneLodIndex);
			writer.WriteLine("    nofacial");
			if (useShadowLodMaterials)
			{
				writer.WriteLine("    use_shadowlod_materials");
			}

			writer.WriteLine("}");
		}
		else
		{
			// Fallback shadow LOD selection.
			var maxRegularLodIndex = lodGroups.Values.SelectMany(g => g.LodIndexes).DefaultIfEmpty(0).Max();
			var shadowLodIndex = maxRegularLodIndex;
			if (vtx.BodyParts.SelectMany(bp => bp.Models).Any(m => m.Lods.Count > maxRegularLodIndex + 1))
			{
				shadowLodIndex = maxRegularLodIndex + 1;
			}

			if (shadowLodIndex > 0)
			{
				var refFile = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex: 0, modelIndex: 0, lodIndex: 0, options);
				var lodFile = GetMeshSmdFileName(modelFileNamePrefix, bodyPartIndex: 0, modelIndex: 0, lodIndex: shadowLodIndex, options);

				writer.WriteLine("$shadowlod");
				writer.WriteLine("{");
				writer.WriteLine($"    replacemodel \"{refFile}\" \"{lodFile}\"");
				WriteMaterialReplacements(writer, model, vtx, new[] { shadowLodIndex });
				WriteReplaceBones(writer, model.Bones, shadowLodIndex);
				writer.WriteLine("    nofacial");
				if (useShadowLodMaterials)
				{
					writer.WriteLine("    use_shadowlod_materials");
				}

				writer.WriteLine("}");
			}
		}
	}

	private static string GetMeshSmdFileName(string modelFileNamePrefix, int bodyPartIndex, int modelIndex, int lodIndex, DecompileOptions options)
	{
		var fileName = $"ref_bodypart{bodyPartIndex}_model{modelIndex}_lod{lodIndex}.smd";
		if (!options.PrefixFileNamesWithModelName)
		{
			return fileName;
		}

		var prefix = string.IsNullOrWhiteSpace(modelFileNamePrefix) ? "model" : modelFileNamePrefix.Trim();
		return $"{prefix}_{fileName}";
	}

	private static void WriteMaterialReplacements(
		StreamWriter writer,
		MdlModel model,
		VtxFile vtx,
		IReadOnlyCollection<int> lodIndexes)
	{
		if (vtx.MaterialReplacementLists.Count == 0 || lodIndexes.Count == 0)
		{
			return;
		}

		var uniqueLines = new HashSet<string>(StringComparer.Ordinal);
		foreach (var lodIndex in lodIndexes.OrderBy(i => i))
		{
			if ((uint)lodIndex >= (uint)vtx.MaterialReplacementLists.Count)
			{
				continue;
			}

			var list = vtx.MaterialReplacementLists[lodIndex];
			foreach (var replacement in list.Replacements)
			{
				if (replacement.MaterialIndex < 0)
				{
					continue;
				}

				var from = GetMaterialName(model.Textures, replacement.MaterialIndex);
				var to = NormalizeMaterialName(replacement.ReplacementMaterial);
				if (string.IsNullOrWhiteSpace(to))
				{
					continue;
				}

				from = from.Replace("\"", "'");
				to = to.Replace("\"", "'");
				var line = $"    replacematerial \"{from}\" \"{to}\"";
				if (uniqueLines.Add(line))
				{
					writer.WriteLine(line);
				}
			}
		}
	}

	private static void WriteReplaceBones(StreamWriter writer, IReadOnlyList<MdlBone> bones, int lodIndex)
	{
		if (lodIndex <= 0 || bones.Count == 0)
		{
			return;
		}

		var lodFlag = lodIndex switch
		{
			1 => MdlConstants.BoneUsedByVertexLod1,
			2 => MdlConstants.BoneUsedByVertexLod2,
			3 => MdlConstants.BoneUsedByVertexLod3,
			4 => MdlConstants.BoneUsedByVertexLod4,
			5 => MdlConstants.BoneUsedByVertexLod5,
			6 => MdlConstants.BoneUsedByVertexLod6,
			7 => MdlConstants.BoneUsedByVertexLod7,
			_ => 0
		};

		if (lodFlag == 0)
		{
			return;
		}

		for (var i = 0; i < bones.Count; i++)
		{
			var bone = bones[i];
			if (bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count)
			{
				continue;
			}

			if ((bone.Flags & lodFlag) != 0)
			{
				continue;
			}

			var parent = bones[bone.ParentIndex];
			var boneName = (bone.Name ?? string.Empty).Replace("\"", "'");
			var parentName = (parent.Name ?? string.Empty).Replace("\"", "'");
			writer.WriteLine($"    replacebone \"{boneName}\" \"{parentName}\"");
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

	private sealed class LodGroup
	{
		public List<(string ReferenceFileName, string LodFileName)> Models { get; } = new();
		public HashSet<int> LodIndexes { get; } = new();
		public bool UsesFacial { get; set; }
	}

	private static async Task WriteReferenceSmdAsync(
		string pathFileName,
		MdlModel model,
		MdlSubModel mdlSubModel,
		VvdFile vvd,
		VtxModel vtxModel,
		int lodIndex,
		int globalVertexIndexStart,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		var bones = model.Bones;
		var format = CultureInfo.InvariantCulture;
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync("version 1");
		await writer.WriteLineAsync("nodes");
		for (var index = 0; index < bones.Count; index++)
		{
			var bone = bones[index];
			var name = bone.Name.Replace("\"", "'");
			await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0");
		for (var index = 0; index < bones.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = bones[index];
			var p = bone.Position;
			var r = bone.RotationRadians;

			await writer.WriteLineAsync(string.Format(
				format,
				"{0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000}",
				bone.Index,
				p.X, p.Y, p.Z,
				r.X, r.Y, r.Z));
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("triangles");

		if (vtxModel.Lods.Count > lodIndex)
		{
			var vtxLod = vtxModel.Lods[lodIndex];

			var meshCount = Math.Min(vtxLod.Meshes.Count, mdlSubModel.Meshes.Count);
			for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
			{
				var vtxMesh = vtxLod.Meshes[meshIndex];
				var mdlMesh = mdlSubModel.Meshes[meshIndex];

				var materialName = GetMaterialName(model.Textures, mdlMesh.MaterialIndex);
				if (options.RemovePathFromSmdMaterialFileNames)
				{
					materialName = Path.GetFileName(materialName);
				}
				var meshVertexIndexStart = mdlMesh.VertexIndexStart;

				foreach (var stripGroup in vtxMesh.StripGroups)
				{
					var indexes = stripGroup.Indexes;
					var vertexes = stripGroup.Vertexes;

					for (var i = 0; i + 2 < indexes.Count; i += 3)
					{
						var v0 = indexes[i];
						var v1 = indexes[i + 2];
						var v2 = indexes[i + 1];

						var vertex1Line = FormatVertexLine(model.Header.Flags, vvd, vertexes, v0, meshVertexIndexStart, globalVertexIndexStart, options);
						var vertex2Line = FormatVertexLine(model.Header.Flags, vvd, vertexes, v1, meshVertexIndexStart, globalVertexIndexStart, options);
						var vertex3Line = FormatVertexLine(model.Header.Flags, vvd, vertexes, v2, meshVertexIndexStart, globalVertexIndexStart, options);

						var materialLine = materialName;
						if (vertex1Line.StartsWith("// ", StringComparison.Ordinal) ||
							vertex2Line.StartsWith("// ", StringComparison.Ordinal) ||
							vertex3Line.StartsWith("// ", StringComparison.Ordinal))
						{
							materialLine = "// " + materialLine;
							if (!vertex1Line.StartsWith("// ", StringComparison.Ordinal)) vertex1Line = "// " + vertex1Line;
							if (!vertex2Line.StartsWith("// ", StringComparison.Ordinal)) vertex2Line = "// " + vertex2Line;
							if (!vertex3Line.StartsWith("// ", StringComparison.Ordinal)) vertex3Line = "// " + vertex3Line;
						}

						await writer.WriteLineAsync(materialLine);
						await writer.WriteLineAsync(vertex1Line);
						await writer.WriteLineAsync(vertex2Line);
						await writer.WriteLineAsync(vertex3Line);
					}
				}
			}
		}

		await writer.WriteLineAsync("end");
		await writer.FlushAsync();
	}

	private static string GetMaterialName(IReadOnlyList<MdlTexture> textures, int materialIndex)
	{
		if ((uint)materialIndex < (uint)textures.Count)
		{
			var path = textures[materialIndex].PathFileName ?? string.Empty;
			path = path.Replace('\\', '/').Trim();
			if (path.EndsWith(".vmt", StringComparison.OrdinalIgnoreCase))
			{
				path = path[..^4];
			}
			return string.IsNullOrWhiteSpace(path) ? $"material_{materialIndex}" : path;
		}

		return $"material_{materialIndex}";
	}

	private static string FormatVertexLine(
		int mdlFlags,
		VvdFile vvd,
		IReadOnlyList<VtxVertex> stripGroupVertexes,
		ushort stripGroupVertexIndex,
		int meshVertexIndexStart,
		int globalVertexIndexStart,
		DecompileOptions options)
	{
		try
		{
			if ((uint)stripGroupVertexIndex >= (uint)stripGroupVertexes.Count)
			{
				return "// invalid stripGroup vertex index";
			}

			var vtxVertex = stripGroupVertexes[stripGroupVertexIndex];
			var vertexIndex = vtxVertex.OriginalMeshVertexIndex + globalVertexIndexStart + meshVertexIndexStart;

			if (vvd.Header.FixupCount <= 0 || vvd.FixedVertexesByLod.Count == 0)
			{
				if ((uint)vertexIndex >= (uint)vvd.Vertexes.Count)
				{
					return "// invalid VVD vertex index";
				}
			}
			else
			{
				var fixedVertexes = vvd.FixedVertexesByLod[0];
				if ((uint)vertexIndex >= (uint)fixedVertexes.Count)
				{
					return "// invalid VVD fixed vertex index";
				}
			}

			var vertex = GetVvdVertex(vvd, vertexIndex);
			var bw = vertex.BoneWeight;

			var boneCount = Math.Clamp((int)bw.BoneCount, 0, 3);
			var primaryBone = bw.Bone0;

			var position = vertex.Position;
			var normal = vertex.Normal;
			if ((mdlFlags & MdlConstants.StudioHdrFlagsStaticProp) != 0)
			{
				position = new System.Numerics.Vector3(position.Y, -position.X, position.Z);
				normal = new System.Numerics.Vector3(normal.Y, -normal.X, normal.Z);
			}

			var u = vertex.TexCoord.X;
			var v = options.UseNonValveUvConversion ? vertex.TexCoord.Y : 1 - vertex.TexCoord.Y;

			var line = string.Format(
				CultureInfo.InvariantCulture,
				"  {0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000} {7:0.000000} {8:0.000000} {9}",
				primaryBone,
				position.X, position.Y, position.Z,
				normal.X, normal.Y, normal.Z,
				u, v,
				boneCount);

			if (boneCount > 0)
			{
				line += $" {bw.Bone0} {bw.Weight0:0.000000}";
			}
			if (boneCount > 1)
			{
				line += $" {bw.Bone1} {bw.Weight1:0.000000}";
			}
			if (boneCount > 2)
			{
				line += $" {bw.Bone2} {bw.Weight2:0.000000}";
			}

			return line;
		}
		catch
		{
			return "// invalid vertex";
		}
	}

	private static Stunstick.Core.Vvd.VvdVertex GetVvdVertex(VvdFile vvd, int vertexIndex)
	{
		if (vvd.Header.FixupCount <= 0 || vvd.FixedVertexesByLod.Count == 0)
		{
			return (uint)vertexIndex < (uint)vvd.Vertexes.Count
				? vvd.Vertexes[vertexIndex]
				: default;
		}

		var fixedVertexes = vvd.FixedVertexesByLod[0];
		return (uint)vertexIndex < (uint)fixedVertexes.Count
			? fixedVertexes[vertexIndex]
			: default;
	}

	private static async Task WriteSkeletonSmdAsync(string pathFileName, IReadOnlyList<MdlBone> bones, CancellationToken cancellationToken)
	{
		await WriteSkeletonSmdAsync(pathFileName, bones, new DecompileOptions(), cancellationToken);
	}

	private static async Task WriteSkeletonSmdAsync(string pathFileName, IReadOnlyList<MdlBone> bones, DecompileOptions options, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync("version 1");
		await writer.WriteLineAsync("nodes");

		for (var index = 0; index < bones.Count; index++)
		{
			var bone = bones[index];
			var name = bone.Name.Replace("\"", "'");
			await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
		}

		await writer.WriteLineAsync("end");
		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0");

		var format = CultureInfo.InvariantCulture;
		for (var index = 0; index < bones.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = bones[index];
			var p = bone.Position;
			var r = bone.RotationRadians;

			await writer.WriteLineAsync(string.Format(
				format,
				"{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######} {5:0.######} {6:0.######}",
				bone.Index,
				p.X, p.Y, p.Z,
				r.X, r.Y, r.Z));
		}

		await writer.WriteLineAsync("end");
		await writer.WriteLineAsync("triangles");
		await writer.WriteLineAsync("end");

		await writer.FlushAsync();
	}

	private static bool ModelHasFlexes(MdlModel model)
	{
		if (model.FlexDescs.Count > 0 && model.FlexControllers.Count > 0)
		{
			return true;
		}

		foreach (var bodyPart in model.BodyParts)
		{
			foreach (var subModel in bodyPart.Models)
			{
				foreach (var mesh in subModel.Meshes)
				{
					if (mesh.Flexes.Any(f => f.VertAnims.Count > 0))
					{
						return true;
					}
				}
			}
		}

		return false;
	}
}
