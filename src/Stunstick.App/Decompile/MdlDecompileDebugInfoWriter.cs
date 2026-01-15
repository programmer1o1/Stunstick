using Stunstick.Core.Mdl;
using Stunstick.Core.Phy;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stunstick.App.Decompile;

internal static class MdlDecompileDebugInfoWriter
{
	private const int AttachmentStructSizeBytes = 92;

	public static async Task WriteDebugInfoAsync(
		string mdlPath,
		string modelOutputFolder,
		MdlModel model,
		DecompileOptions options,
		string? proceduralBonesVrdFileName,
		VvdFile? vvd,
		VtxFile? vtx,
		PhyFile? phy,
		IReadOnlyList<AccessedBytesDebugFileWriter.DebugFile>? accessedBytesDebugFiles,
		CancellationToken cancellationToken)
	{
		if (!options.WriteDebugInfoFiles)
		{
			return;
		}

		var debugFolder = Path.Combine(modelOutputFolder, "debug");
		Directory.CreateDirectory(debugFolder);

		var attachments = ReadAttachmentNames(mdlPath, model.Header);

		// Procedural bones summary (best-effort).
		var proceduralBones = ReadProceduralBones(mdlPath, model, attachments);

		MdlEmbeddedSections? embeddedSections = null;
		if (model.Header.Version == 53)
		{
			try
			{
				using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				if (MdlV53EmbeddedSectionsReader.TryRead(stream, out var embedded))
				{
					embeddedSections = embedded;
				}
			}
			catch
			{
				// Best-effort.
			}
		}

		var fileSummary = BuildOutputFileSummary(modelOutputFolder);

		var info = new MdlDebugInfo(
			GeneratedAt: DateTimeOffset.Now,
			SourceMdlPath: Path.GetFullPath(mdlPath),
			OutputFolder: Path.GetFullPath(modelOutputFolder),
			Options: options,
			Header: model.Header,
			Attachments: attachments,
			ProceduralBones: proceduralBones,
			ProceduralBonesVrdFileName: proceduralBonesVrdFileName,
			EmbeddedSections: embeddedSections,
			Vvd: vvd is null ? null : CreateVvdSummary(vvd),
			Vtx: vtx is null ? null : CreateVtxSummary(vtx),
			Phy: phy is null ? null : CreatePhySummary(phy),
			Outputs: fileSummary);

		var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() });
		var pathFileName = Path.Combine(debugFolder, "debug-info.json");
		await File.WriteAllTextAsync(pathFileName, json, Encoding.UTF8, cancellationToken);

		if (accessedBytesDebugFiles is not null && accessedBytesDebugFiles.Count > 0)
		{
			foreach (var file in accessedBytesDebugFiles)
			{
				if (!file.Log.HasEntries)
				{
					continue;
				}

				try
				{
					await AccessedBytesDebugFileWriter.WriteAsync(debugFolder, file, options, cancellationToken);
				}
				catch
				{
					// Best-effort.
				}
			}
		}
	}

	private static OutputFileSummary BuildOutputFileSummary(string modelOutputFolder)
	{
		try
		{
			var all = Directory.EnumerateFiles(modelOutputFolder, "*", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(modelOutputFolder, path))
				.Where(rel => !rel.StartsWith($"original{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
				.Where(rel => !rel.StartsWith($"debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
				.ToList();

			var byExt = all
				.GroupBy(path => NormalizeExt(Path.GetExtension(path)))
				.OrderByDescending(g => g.Count())
				.ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

			var examples = all
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.Take(200)
				.ToList();

			return new OutputFileSummary(
				TotalFiles: all.Count,
				FilesByExtension: byExt,
				ExampleFiles: examples);
		}
		catch
		{
			return new OutputFileSummary(
				TotalFiles: 0,
				FilesByExtension: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
				ExampleFiles: Array.Empty<string>());
		}
	}

	private static string NormalizeExt(string ext)
	{
		if (string.IsNullOrWhiteSpace(ext))
		{
			return "(none)";
		}

		return ext.Trim().ToLowerInvariant();
	}

	private static VvdSummary CreateVvdSummary(VvdFile vvd)
	{
		var sourcePath = vvd.SourcePath;
		var embedded = sourcePath.Contains('#', StringComparison.Ordinal);
		var exists = !embedded && File.Exists(sourcePath);
		var sizeBytes = exists ? new FileInfo(sourcePath).Length : (long?)null;

		return new VvdSummary(
			SourcePath: sourcePath,
			Embedded: embedded,
			Exists: exists,
			SizeBytes: sizeBytes,
			Header: vvd.Header,
			VertexCount: vvd.Vertexes.Count,
			FixupCount: vvd.Fixups.Count);
	}

	private static VtxSummary CreateVtxSummary(VtxFile vtx)
	{
		var sourcePath = vtx.SourcePath;
		var embedded = sourcePath.Contains('#', StringComparison.Ordinal);
		var exists = !embedded && File.Exists(sourcePath);
		var sizeBytes = exists ? new FileInfo(sourcePath).Length : (long?)null;

		return new VtxSummary(
			SourcePath: sourcePath,
			Embedded: embedded,
			Exists: exists,
			SizeBytes: sizeBytes,
			Header: vtx.Header,
			UsesExtraStripGroupFields: vtx.UsesExtraStripGroupFields,
			BodyPartCount: vtx.BodyParts.Count);
	}

	private static PhySummary CreatePhySummary(PhyFile phy)
	{
		var sourcePath = phy.SourcePath;
		var embedded = sourcePath.Contains('#', StringComparison.Ordinal);
		var exists = !embedded && File.Exists(sourcePath);
		var sizeBytes = exists ? new FileInfo(sourcePath).Length : (long?)null;

		return new PhySummary(
			SourcePath: sourcePath,
			Embedded: embedded,
			Exists: exists,
			SizeBytes: sizeBytes,
			Header: phy.Header,
			SolidCount: phy.Solids.Count);
	}

	private static IReadOnlyList<string> ReadAttachmentNames(string mdlPath, MdlHeader header)
	{
		if (header.LocalAttachmentCount <= 0 || header.LocalAttachmentOffset <= 0)
		{
			return Array.Empty<string>();
		}

		try
		{
			using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

			if (header.LocalAttachmentOffset >= stream.Length)
			{
				return Array.Empty<string>();
			}

			var requiredBytes = (long)header.LocalAttachmentCount * AttachmentStructSizeBytes;
			if ((long)header.LocalAttachmentOffset + requiredBytes > stream.Length)
			{
				return Array.Empty<string>();
			}

			var attachments = new List<string>(capacity: header.LocalAttachmentCount);
			for (var index = 0; index < header.LocalAttachmentCount; index++)
			{
				var attachmentStart = (long)header.LocalAttachmentOffset + index * AttachmentStructSizeBytes;
				stream.Seek(attachmentStart, SeekOrigin.Begin);

				var nameOffset = reader.ReadInt32();
				if (nameOffset <= 0)
				{
					attachments.Add(string.Empty);
					continue;
				}

				var nameStart = attachmentStart + nameOffset;
				if (nameStart < 0 || nameStart >= stream.Length)
				{
					attachments.Add(string.Empty);
					continue;
				}

				var name = ReadNullTerminatedStringAt(stream, nameStart, maxBytes: 256);
				attachments.Add(name);
			}

			return attachments;
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	private static IReadOnlyList<ProceduralBoneSummary> ReadProceduralBones(string mdlPath, MdlModel model, IReadOnlyList<string> attachments)
	{
		try
		{
			using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

			var boneSize = DetermineBoneStructSize(stream, reader, model.Header.BoneOffset, model.Header.BoneCount);
			if (boneSize is null)
			{
				return Array.Empty<ProceduralBoneSummary>();
			}

			if (!TryGetProceduralRuleFieldOffsets(boneSize.Value, out var procTypeOffset, out var procIndexOffset))
			{
				return Array.Empty<ProceduralBoneSummary>();
			}

			var results = new List<ProceduralBoneSummary>();

			for (var boneIndex = 0; boneIndex < model.Header.BoneCount; boneIndex++)
			{
				var boneStart = (long)model.Header.BoneOffset + (long)boneIndex * boneSize.Value;
				if (boneStart < 0 || boneStart + Math.Max(procIndexOffset, procTypeOffset) + 4 > stream.Length)
				{
					break;
				}

				stream.Seek(boneStart + procTypeOffset, SeekOrigin.Begin);
				var procType = reader.ReadInt32();
				stream.Seek(boneStart + procIndexOffset, SeekOrigin.Begin);
				var procOffset = reader.ReadInt32();

				if (procOffset == 0)
				{
					continue;
				}

				var boneName = boneIndex >= 0 && boneIndex < model.Bones.Count ? model.Bones[boneIndex].Name : $"bone_{boneIndex}";
				QuatInterpSummary? quatInterp = null;
				AimAtSummary? aimAt = null;

				if (procType == 2 && TryReadQuatInterp(stream, reader, boneStart, procOffset, out var qi))
				{
					quatInterp = qi;
				}
				else if (procType is 3 or 4 && TryReadAimAt(stream, reader, boneStart, procOffset, out var a))
				{
					var parentName = (a.ParentBoneIndex >= 0 && a.ParentBoneIndex < model.Bones.Count) ? model.Bones[a.ParentBoneIndex].Name : string.Empty;
					string? aimName = null;
					if (procType == 3 && a.AimIndex >= 0 && a.AimIndex < model.Bones.Count)
					{
						aimName = model.Bones[a.AimIndex].Name;
					}
					else if (procType == 4 && a.AimIndex >= 0 && a.AimIndex < attachments.Count)
					{
						aimName = attachments[a.AimIndex];
					}

					aimAt = new AimAtSummary(
						ParentBoneIndex: a.ParentBoneIndex,
						ParentBoneName: parentName,
						AimIndex: a.AimIndex,
						AimName: aimName ?? string.Empty,
						Aim: a.Aim,
						Up: a.Up,
						BasePos: a.BasePos);
				}

				results.Add(new ProceduralBoneSummary(
					BoneIndex: boneIndex,
					BoneName: boneName,
					ProcType: procType,
					ProcOffset: procOffset,
					QuatInterp: quatInterp,
					AimAt: aimAt));
			}

			return results;
		}
		catch
		{
			return Array.Empty<ProceduralBoneSummary>();
		}
	}

	private static bool TryReadQuatInterp(FileStream stream, BinaryReader reader, long boneStart, int procOffset, out QuatInterpSummary summary)
	{
		summary = default!;
		try
		{
			var procStart = boneStart + procOffset;
			if (procStart < 0 || procStart + 12 > stream.Length)
			{
				return false;
			}

			stream.Seek(procStart, SeekOrigin.Begin);
			var controlBoneIndex = reader.ReadInt32();
			var triggerCount = reader.ReadInt32();
			var triggerOffset = reader.ReadInt32();

			summary = new QuatInterpSummary(
				ControlBoneIndex: controlBoneIndex,
				TriggerCount: triggerCount,
				TriggerOffset: triggerOffset);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryReadAimAt(FileStream stream, BinaryReader reader, long boneStart, int procOffset, out AimAtRawData data)
	{
		data = default;
		try
		{
			var procStart = boneStart + procOffset;
			if (procStart < 0 || procStart + 44 > stream.Length)
			{
				return false;
			}

			stream.Seek(procStart, SeekOrigin.Begin);
			var parentBoneIndex = reader.ReadInt32();
			var aimIndex = reader.ReadInt32();
			var aim = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var up = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var basePos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			data = new AimAtRawData(parentBoneIndex, aimIndex, aim, up, basePos);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static int? DetermineBoneStructSize(FileStream stream, BinaryReader reader, int boneOffset, int boneCount)
	{
		var candidates = new[]
		{
			244,
			216,
			200,
			184,
			176,
			160,
			152,
			144
		};

		foreach (var size in candidates)
		{
			var totalBytes = (long)boneCount * size;
			if ((long)boneOffset + totalBytes > stream.Length)
			{
				continue;
			}

			var ok = true;
			var maxToCheck = Math.Min(boneCount, 32);
			for (var index = 0; index < maxToCheck; index++)
			{
				var boneStart = (long)boneOffset + index * size;
				stream.Seek(boneStart, SeekOrigin.Begin);

				var nameOffset = reader.ReadInt32();
				var parentIndex = reader.ReadInt32();

				if (parentIndex < -1 || parentIndex >= boneCount)
				{
					ok = false;
					break;
				}

				var nameStart = boneStart + nameOffset;
				if (nameOffset <= 0 || nameStart < 0 || nameStart >= stream.Length)
				{
					ok = false;
					break;
				}

				var name = ReadNullTerminatedStringAt(stream, nameStart, maxBytes: 256);
				if (!IsPlausibleName(name))
				{
					ok = false;
					break;
				}
			}

			if (ok)
			{
				return size;
			}
		}

		return null;
	}

	private static bool TryGetProceduralRuleFieldOffsets(int boneStructSizeBytes, out int procTypeOffset, out int procIndexOffset)
	{
		if (boneStructSizeBytes >= 244)
		{
			procTypeOffset = 188;
			procIndexOffset = 192;
			return true;
		}

		if (boneStructSizeBytes >= 176)
		{
			procTypeOffset = 164;
			procIndexOffset = 168;
			return true;
		}

		procTypeOffset = 0;
		procIndexOffset = 0;
		return false;
	}

	private static bool IsPlausibleName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		if (name.Length > 200)
		{
			return false;
		}

		foreach (var c in name)
		{
			if (c == '\0' || char.IsControl(c))
			{
				return false;
			}
		}

		return true;
	}

	private static string ReadNullTerminatedStringAt(FileStream stream, long offset, int maxBytes)
	{
		var old = stream.Position;
		try
		{
			stream.Seek(offset, SeekOrigin.Begin);
			var bytes = new byte[maxBytes];
			var read = stream.Read(bytes, 0, maxBytes);
			if (read <= 0)
			{
				return string.Empty;
			}

			var zeroIndex = Array.IndexOf(bytes, (byte)0, 0, read);
			var len = zeroIndex >= 0 ? zeroIndex : read;
			return Encoding.ASCII.GetString(bytes, 0, len);
		}
		catch
		{
			return string.Empty;
		}
		finally
		{
			stream.Seek(old, SeekOrigin.Begin);
		}
	}

	private readonly record struct AimAtRawData(int ParentBoneIndex, int AimIndex, Vector3 Aim, Vector3 Up, Vector3 BasePos);

	private sealed record MdlDebugInfo(
		DateTimeOffset GeneratedAt,
		string SourceMdlPath,
		string OutputFolder,
		DecompileOptions Options,
		MdlHeader Header,
		IReadOnlyList<string> Attachments,
		IReadOnlyList<ProceduralBoneSummary> ProceduralBones,
		string? ProceduralBonesVrdFileName,
		MdlEmbeddedSections? EmbeddedSections,
		VvdSummary? Vvd,
		VtxSummary? Vtx,
		PhySummary? Phy,
		OutputFileSummary Outputs);

	private sealed record OutputFileSummary(
		int TotalFiles,
		IReadOnlyDictionary<string, int> FilesByExtension,
		IReadOnlyList<string> ExampleFiles);

	private sealed record VvdSummary(
		string SourcePath,
		bool Embedded,
		bool Exists,
		long? SizeBytes,
		VvdHeader Header,
		int VertexCount,
		int FixupCount);

	private sealed record VtxSummary(
		string SourcePath,
		bool Embedded,
		bool Exists,
		long? SizeBytes,
		VtxHeader Header,
		bool UsesExtraStripGroupFields,
		int BodyPartCount);

	private sealed record PhySummary(
		string SourcePath,
		bool Embedded,
		bool Exists,
		long? SizeBytes,
		PhyHeader Header,
		int SolidCount);

	private sealed record ProceduralBoneSummary(
		int BoneIndex,
		string BoneName,
		int ProcType,
		int ProcOffset,
		QuatInterpSummary? QuatInterp,
		AimAtSummary? AimAt);

	private sealed record QuatInterpSummary(
		int ControlBoneIndex,
		int TriggerCount,
		int TriggerOffset);

	private sealed record AimAtSummary(
		int ParentBoneIndex,
		string ParentBoneName,
		int AimIndex,
		string AimName,
		Vector3 Aim,
		Vector3 Up,
		Vector3 BasePos);
}
