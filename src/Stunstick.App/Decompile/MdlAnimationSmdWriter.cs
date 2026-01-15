using Stunstick.Core.IO;
using Stunstick.Core.Mdl;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Stunstick.App.Decompile;

internal static class MdlAnimationSmdWriter
{
	internal static bool TryGetFirstAnimationDescFrame0RootTransform(
		string mdlPath,
		MdlModel model,
		out Vector3 position,
		out Vector3 rotation)
	{
		return TryGetFirstAnimationDescFrame0RootTransform(
			mdlPath,
			model,
			out position,
			out rotation,
			mdlAccessedBytesLog: null,
			accessedBytesDebugLogs: null);
	}

	internal static bool TryGetFirstAnimationDescFrame0RootTransform(
		string mdlPath,
		MdlModel model,
		out Vector3 position,
		out Vector3 rotation,
		AccessedBytesLog? mdlAccessedBytesLog,
		AccessedBytesDebugLogs? accessedBytesDebugLogs)
	{
		position = default;
		rotation = default;

		if (string.IsNullOrWhiteSpace(mdlPath) || model.Bones.Count == 0 || model.AnimationDescs.Count == 0)
		{
			return false;
		}

		var animDesc = model.AnimationDescs[0];

		if (animDesc.FrameCount <= 0)
		{
			return false;
		}

		Stream? aniStream = null;
		BinaryReader? aniReader = null;

		try
		{
			using var stream = OpenReadStream(mdlPath, mdlAccessedBytesLog);

			using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

			var animBlocks = ReadAnimBlocks(stream, reader, model.Header);
			var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
			var baseName = Path.GetFileNameWithoutExtension(mdlPath);
			string? aniPath = null;

			var sections = ReadAnimSections(stream, reader, animDesc);
			var (sectionIndex, localFrameIndex) = GetSectionFrame(animDesc, sections, frameIndex: 0);
			if ((uint)sectionIndex >= (uint)sections.Count)
			{
				return false;
			}

			var section = sections[sectionIndex];
			var sectionFrameCount = animDesc.FrameCount;
			var lastSectionIsBeingRead = true;
			if (sections.Count > 1 && animDesc.SectionFrameCount > 0)
			{
				if (sectionIndex < sections.Count - 2)
				{
					sectionFrameCount = animDesc.SectionFrameCount;
				}
				else
				{
					sectionFrameCount = animDesc.FrameCount - ((sections.Count - 2) * animDesc.SectionFrameCount);
				}

				lastSectionIsBeingRead = sectionIndex >= sections.Count - 2 ||
					animDesc.FrameCount == (sectionIndex + 1) * animDesc.SectionFrameCount;
			}

			if (sectionFrameCount <= 0)
			{
				return false;
			}

			Stream animStreamToReadFrom;
			BinaryReader animReaderToReadFrom;
			long animDataOffset;
			if (section.AnimBlock == 0)
			{
				var adjustedAnimOffset = section.AnimOffset;
				if (sections.Count > 1)
				{
					// Match old Crowbar's "adjustedAnimOffset" quirk for certain oddball models.
					adjustedAnimOffset = section.AnimOffset + (animDesc.AnimOffset - sections[0].AnimOffset);
				}

				animStreamToReadFrom = stream;
				animReaderToReadFrom = reader;
				animDataOffset = animDesc.OffsetStart + adjustedAnimOffset;
			}
			else
			{
				if ((uint)section.AnimBlock >= (uint)animBlocks.Count)
				{
					return false;
				}

				aniPath ??= TryResolveAniPath(mdlPath, directoryPath, baseName, stream, reader, model.Header);
				if (string.IsNullOrWhiteSpace(aniPath) || !File.Exists(aniPath))
				{
					return false;
				}

				aniStream = new FileStream(aniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				if (accessedBytesDebugLogs is not null)
				{
					try
					{
						var fullAniPath = Path.GetFullPath(aniPath);
						var aniLog = accessedBytesDebugLogs.GetOrCreateLog(
							accessedBytesDebugLogs.BuildFileName("decompile-ANI.txt"),
							displayPath: fullAniPath,
							containerPath: fullAniPath,
							containerOffset: 0,
							length: new FileInfo(fullAniPath).Length);

						aniStream = new AccessLoggedStream(aniStream, aniLog);
					}
					catch
					{
						// Best-effort.
					}
				}

				aniReader = new BinaryReader(aniStream, Encoding.ASCII, leaveOpen: true);

				animStreamToReadFrom = aniStream;
				animReaderToReadFrom = aniReader;
				animDataOffset = animBlocks[section.AnimBlock].DataStart + (long)section.AnimOffset;
				}

				var boneCount = Math.Max(1, model.Bones.Count);
				if ((animDesc.Flags & StudioAnimDescFrameAnim) != 0)
				{
					var sectionContext = TryReadFrameAnimSection(animStreamToReadFrom, animReaderToReadFrom, animDataOffset, boneCount);
					if (sectionContext is null || sectionContext.BoneFlags.Length == 0 || sectionContext.Constants.Length == 0)
					{
						return false;
					}

						var bonesByIndex = new MdlBone?[boneCount];
						for (var i = 0; i < model.Bones.Count; i++)
						{
							var mdlBone = model.Bones[i];
							if ((uint)mdlBone.Index < (uint)boneCount)
							{
								bonesByIndex[mdlBone.Index] = mdlBone;
							}
						}

					var targetBoneIndex = 0;
					for (var i = 0; i < Math.Min(sectionContext.BoneFlags.Length, boneCount); i++)
					{
						if (sectionContext.BoneFlags[i] != 0)
						{
							targetBoneIndex = i;
							break;
						}
					}

					var targetBone = bonesByIndex[targetBoneIndex] ?? model.Bones[0];
					var boneFlag = sectionContext.BoneFlags[targetBoneIndex];
					var constants = sectionContext.Constants[targetBoneIndex];

					var useDeltaBasePose = (animDesc.Flags & StudioAnimDescDelta) != 0;
					var pos = useDeltaBasePose ? Vector3.Zero : targetBone.Position;
					var rot = useDeltaBasePose ? Vector3.Zero : targetBone.RotationRadians;

					if ((boneFlag & StudioFrameRawRot) != 0 && constants.RawRot is not null)
					{
						rot = ToEulerAngles(constants.RawRot.Value);
					}
					if ((boneFlag & StudioFrameRawPos) != 0 && constants.RawPos is not null)
					{
						pos = constants.RawPos.Value;
					}
					if ((boneFlag & StudioFrameConstPos2) != 0 && constants.ConstPos2 is not null)
					{
						pos = constants.ConstPos2.Value;
					}
					if ((boneFlag & StudioFrameConstRot2) != 0 && constants.ConstRot2 is not null)
					{
						rot = ToEulerAngles(constants.ConstRot2.Value);
					}

					if (sectionContext.FrameLength > 0 && sectionContext.FrameDataStart > 0)
					{
						var frameStart = sectionContext.FrameDataStart + (long)localFrameIndex * sectionContext.FrameLength;
						if (frameStart < 0 || frameStart + sectionContext.FrameLength > sectionContext.Stream.Length)
						{
							return false;
						}

						sectionContext.Stream.Seek(frameStart, SeekOrigin.Begin);

						var quaternion48SBytes = new byte[6];
						Quaternion? animRot2 = null;
						Quaternion? animRot = null;
						Vector3? animPos = null;
						Vector3? fullAnimPos = null;

						for (var i = 0; i <= targetBoneIndex && i < boneCount; i++)
						{
							var flag = sectionContext.BoneFlags[i];

							if ((flag & StudioFrameAnimRot2) != 0)
							{
								if (!TryReadBytesExact(sectionContext.Reader, quaternion48SBytes))
								{
									return false;
								}

								if (i == targetBoneIndex)
								{
									animRot2 = DecodeQuaternion48S(quaternion48SBytes);
								}
							}

							if ((flag & StudioFrameAnimRot) != 0)
							{
								var x = sectionContext.Reader.ReadUInt16();
								var y = sectionContext.Reader.ReadUInt16();
								var zw = sectionContext.Reader.ReadUInt16();
								if (i == targetBoneIndex)
								{
									animRot = DecodeQuaternion48(x, y, zw);
								}
							}

							if ((flag & StudioFrameAnimPos) != 0)
							{
								var x = sectionContext.Reader.ReadUInt16();
								var y = sectionContext.Reader.ReadUInt16();
								var z = sectionContext.Reader.ReadUInt16();
								if (i == targetBoneIndex)
								{
									animPos = DecodeVector48(x, y, z);
								}
							}

							if ((flag & StudioFrameFullAnimPos) != 0)
							{
								var full = new Vector3(
									sectionContext.Reader.ReadSingle(),
									sectionContext.Reader.ReadSingle(),
									sectionContext.Reader.ReadSingle());
								if (i == targetBoneIndex)
								{
									fullAnimPos = full;
								}
							}
						}

						if ((boneFlag & StudioFrameAnimRot) != 0 && animRot is not null)
						{
							rot = ToEulerAngles(animRot.Value);
						}
						if ((boneFlag & StudioFrameAnimPos) != 0 && animPos is not null)
						{
							pos = animPos.Value;
						}
						if ((boneFlag & StudioFrameFullAnimPos) != 0 && fullAnimPos is not null)
						{
							pos = fullAnimPos.Value;
						}
						if ((boneFlag & StudioFrameAnimRot2) != 0 && animRot2 is not null)
						{
							rot = ToEulerAngles(animRot2.Value);
						}
					}

					position = pos;
					rotation = rot;
					return true;
				}

				var animationsByBone = ReadAnimationsForSection(
					animStreamToReadFrom,
					animReaderToReadFrom,
					animDataOffset: animDataOffset,
				boneCount: boneCount,
				sectionFrameCount: sectionFrameCount,
				lastSectionIsBeingRead: lastSectionIsBeingRead);

			// Match Crowbar: pick the first (lowest index) bone that has animation data.
			var boneIndex = 0;
			for (var i = 0; i < Math.Min(model.Bones.Count, animationsByBone.Length); i++)
			{
				if (animationsByBone[i] is not null)
				{
					boneIndex = i;
					break;
				}
			}

			var bone = model.Bones[boneIndex];
			var anim = (uint)boneIndex < (uint)animationsByBone.Length ? animationsByBone[boneIndex] : null;

			position = CalcBonePosition(localFrameIndex, bone, anim);
			rotation = CalcBoneRotation(localFrameIndex, bone, anim);

			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			aniReader?.Dispose();
			aniStream?.Dispose();
		}
	}

	public static async Task<Dictionary<int, string>> WriteAnimationSmdFilesAsync(
		string mdlPath,
		string modelOutputFolder,
		MdlModel model,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		return await WriteAnimationSmdFilesAsync(
			mdlPath,
			modelOutputFolder,
			model,
			options,
			mdlAccessedBytesLog: null,
			accessedBytesDebugLogs: null,
			cancellationToken);
	}

	public static async Task<Dictionary<int, string>> WriteAnimationSmdFilesAsync(
		string mdlPath,
		string modelOutputFolder,
		MdlModel model,
		DecompileOptions options,
		AccessedBytesLog? mdlAccessedBytesLog,
		AccessedBytesDebugLogs? accessedBytesDebugLogs,
		CancellationToken cancellationToken)
	{
		options ??= new DecompileOptions();

		var result = new Dictionary<int, string>();
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		if (model.AnimationDescs.Count == 0)
		{
			return result;
		}

		var modelName = Path.GetFileNameWithoutExtension(mdlPath);
		if (string.IsNullOrWhiteSpace(modelName))
		{
			modelName = "model";
		}

		var usedRelativePathFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var stream = OpenReadStream(mdlPath, mdlAccessedBytesLog);

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
		var animBlocks = ReadAnimBlocks(stream, reader, model.Header);
		var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";

		var format = CultureInfo.InvariantCulture;

		string? aniPath = null;
		Stream? aniStream = null;
		BinaryReader? aniReader = null;

		try
		{
			foreach (var animDesc in model.AnimationDescs)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if (animDesc.FrameCount <= 0)
				{
					continue;
				}

				var relativePathFileName = DecompileFileNames.CreateAnimationSmdRelativePathFileName(
					options,
					modelName,
					animDesc.Name,
					usedRelativePathFileNames);

				var outputPathFileName = Path.Combine(modelOutputFolder, relativePathFileName);
				Directory.CreateDirectory(Path.GetDirectoryName(outputPathFileName) ?? modelOutputFolder);

			var sections = ReadAnimSections(stream, reader, animDesc);
			var requiresAni = false;
			var maxRequiredAnimBlockIndex = 0;
			for (var i = 0; i < sections.Count; i++)
				{
					if (sections[i].AnimBlock == 0)
					{
						continue;
					}

					requiresAni = true;
					if (sections[i].AnimBlock > maxRequiredAnimBlockIndex)
					{
						maxRequiredAnimBlockIndex = sections[i].AnimBlock;
					}
				}

				if (requiresAni)
				{
					if (animBlocks.Count == 0 || (uint)maxRequiredAnimBlockIndex >= (uint)animBlocks.Count)
					{
						continue;
					}

					aniPath ??= TryResolveAniPath(mdlPath, directoryPath, modelName, stream, reader, model.Header);
					if (string.IsNullOrWhiteSpace(aniPath) || !File.Exists(aniPath))
					{
						continue;
					}

					if (aniStream is null)
					{
						aniStream = new FileStream(aniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						if (accessedBytesDebugLogs is not null)
						{
							try
							{
								var fullAniPath = Path.GetFullPath(aniPath);
								var aniLog = accessedBytesDebugLogs.GetOrCreateLog(
									accessedBytesDebugLogs.BuildFileName("decompile-ANI.txt"),
									displayPath: fullAniPath,
									containerPath: fullAniPath,
									containerOffset: 0,
									length: new FileInfo(fullAniPath).Length);

								aniStream = new AccessLoggedStream(aniStream, aniLog);
							}
							catch
							{
								// Best-effort.
							}
						}
					}

					aniReader ??= new BinaryReader(aniStream, Encoding.ASCII, leaveOpen: true);
				}

				await using var outStream = new FileStream(outputPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
				await using var writer = new StreamWriter(outStream);

				await DecompileFormat.WriteHeaderCommentAsync(writer, options);

				await writer.WriteLineAsync("version 1");
				await writer.WriteLineAsync("nodes");
				for (var i = 0; i < model.Bones.Count; i++)
				{
					var bone = model.Bones[i];
					var name = bone.Name.Replace("\"", "'");
					await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
				}
				await writer.WriteLineAsync("end");

				await writer.WriteLineAsync("skeleton");
				if ((animDesc.Flags & StudioAnimDescFrameAnim) != 0)
				{
					await WriteFrameAnimationSkeletonAsync(
						writer,
						timePrefix,
						format,
						model.Bones,
						model.Header.Version,
						animDesc,
						sections,
						animBlocks,
						stream,
						reader,
						aniStream,
						aniReader,
						cancellationToken);
				}
				else
				{
					var sectionData = ReadAnimSectionData(stream, reader, aniStream, aniReader, model.Bones, animDesc, sections, animBlocks);

					for (var frameIndex = 0; frameIndex < animDesc.FrameCount; frameIndex++)
					{
						cancellationToken.ThrowIfCancellationRequested();

						await writer.WriteLineAsync($"{timePrefix}{frameIndex}");

						var (sectionIndex, localFrameIndex) = GetSectionFrame(animDesc, sections, frameIndex);
						var animationsByBone = (uint)sectionIndex < (uint)sectionData.Length ? sectionData[sectionIndex] : null;

						for (var boneIndex = 0; boneIndex < model.Bones.Count; boneIndex++)
						{
							var bone = model.Bones[boneIndex];
							var anim = animationsByBone is not null && (uint)bone.Index < (uint)animationsByBone.Length
								? animationsByBone[bone.Index]
								: null;

							var position = CalcBonePosition(localFrameIndex, bone, anim);
							var rotation = CalcBoneRotation(localFrameIndex, bone, anim);
							ApplyRootBoneAdjustments(model.Header.Version, frameIndex, bone, animDesc.Movements, ref position, ref rotation);
							position = NormalizeSignedZeros(position);
							rotation = NormalizeSignedZeros(rotation);

							await writer.WriteLineAsync(string.Format(
								format,
								"{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######} {5:0.######} {6:0.######}",
								bone.Index,
								position.X, position.Y, position.Z,
								rotation.X, rotation.Y, rotation.Z));
						}
					}
				}

				await writer.WriteLineAsync("end");
				await writer.WriteLineAsync("triangles");
				await writer.WriteLineAsync("end");
				await writer.FlushAsync();

				result[animDesc.Index] = relativePathFileName.Replace('\\', '/');
			}
		}
		finally
		{
			aniReader?.Dispose();
			aniStream?.Dispose();
		}

		return result;
	}

	private static (int SectionIndex, int LocalFrameIndex) GetSectionFrame(MdlAnimationDesc animDesc, IReadOnlyList<AnimSection> sections, int frameIndex)
	{
		if (sections.Count <= 1 || animDesc.SectionFrameCount <= 0)
		{
			return (0, frameIndex);
		}

		var sectionIndex = frameIndex / animDesc.SectionFrameCount;
		if (sectionIndex < 0)
		{
			sectionIndex = 0;
		}
		if (sectionIndex >= sections.Count)
		{
			sectionIndex = sections.Count - 1;
		}

		var localFrameIndex = frameIndex - sectionIndex * animDesc.SectionFrameCount;
		if (localFrameIndex < 0)
		{
			localFrameIndex = 0;
		}

		return (sectionIndex, localFrameIndex);
	}

	private static IReadOnlyList<AnimSection> ReadAnimSections(Stream stream, BinaryReader reader, MdlAnimationDesc animDesc)
	{
		if (animDesc.SectionOffset == 0 || animDesc.SectionFrameCount <= 0)
		{
			return new[] { new AnimSection(AnimBlock: animDesc.AnimBlock, AnimOffset: animDesc.AnimOffset) };
		}

		var sectionCount = (int)Math.Truncate((double)animDesc.FrameCount / animDesc.SectionFrameCount) + 2;
		if (sectionCount <= 0)
		{
			return new[] { new AnimSection(AnimBlock: animDesc.AnimBlock, AnimOffset: animDesc.AnimOffset) };
		}

		var sectionsStart = animDesc.OffsetStart + animDesc.SectionOffset;
		var bytesNeeded = (long)sectionCount * 8;
		if (sectionsStart < 0 || sectionsStart + bytesNeeded > stream.Length)
		{
			return new[] { new AnimSection(AnimBlock: animDesc.AnimBlock, AnimOffset: animDesc.AnimOffset) };
		}

		stream.Seek(sectionsStart, SeekOrigin.Begin);
		var sections = new List<AnimSection>(capacity: sectionCount);
		for (var i = 0; i < sectionCount; i++)
		{
			var animBlock = reader.ReadInt32();
			var animOffset = reader.ReadInt32();
			sections.Add(new AnimSection(animBlock, animOffset));
		}

		return sections;
	}

	private static Animation?[][] ReadAnimSectionData(
		Stream stream,
		BinaryReader reader,
		Stream? aniStream,
		BinaryReader? aniReader,
		IReadOnlyList<MdlBone> bones,
		MdlAnimationDesc animDesc,
		IReadOnlyList<AnimSection> sections,
		IReadOnlyList<AnimBlock> animBlocks)
	{
		var boneCount = Math.Max(1, bones.Count);
		var output = new Animation?[sections.Count][];

		for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
		{
			var section = sections[sectionIndex];

			var sectionFrameCount = animDesc.FrameCount;
			var lastSectionIsBeingRead = true;
			if (sections.Count > 1 && animDesc.SectionFrameCount > 0)
			{
				if (sectionIndex < sections.Count - 2)
				{
					sectionFrameCount = animDesc.SectionFrameCount;
				}
				else
				{
					sectionFrameCount = animDesc.FrameCount - ((sections.Count - 2) * animDesc.SectionFrameCount);
				}

				lastSectionIsBeingRead = sectionIndex >= sections.Count - 2 ||
					animDesc.FrameCount == (sectionIndex + 1) * animDesc.SectionFrameCount;
			}

			if (sectionFrameCount <= 0)
			{
				output[sectionIndex] = new Animation?[boneCount];
				continue;
			}

			Stream animStream;
			BinaryReader animReader;
			long animDataOffset;
			if (section.AnimBlock == 0)
			{
				var adjustedAnimOffset = section.AnimOffset;
				if (sections.Count > 1)
				{
					// Match old Crowbar's "adjustedAnimOffset" quirk for certain oddball models.
					adjustedAnimOffset = section.AnimOffset + (animDesc.AnimOffset - sections[0].AnimOffset);
				}

				animStream = stream;
				animReader = reader;
				animDataOffset = animDesc.OffsetStart + adjustedAnimOffset;
			}
			else
			{
				if (aniStream is null || aniReader is null || (uint)section.AnimBlock >= (uint)animBlocks.Count)
				{
					output[sectionIndex] = new Animation?[boneCount];
					continue;
				}

				animStream = aniStream;
				animReader = aniReader;
				animDataOffset = animBlocks[section.AnimBlock].DataStart + (long)section.AnimOffset;
			}

			var animationsByBone = ReadAnimationsForSection(
				animStream,
				animReader,
				animDataOffset: animDataOffset,
				boneCount: boneCount,
				sectionFrameCount: sectionFrameCount,
				lastSectionIsBeingRead: lastSectionIsBeingRead);

			output[sectionIndex] = animationsByBone;
		}

		return output;
	}

		private static async Task WriteFrameAnimationSkeletonAsync(
			StreamWriter writer,
			string timePrefix,
			IFormatProvider format,
			IReadOnlyList<MdlBone> bones,
			int modelVersion,
			MdlAnimationDesc animDesc,
			IReadOnlyList<AnimSection> sections,
			IReadOnlyList<AnimBlock> animBlocks,
			Stream mdlStream,
			BinaryReader mdlReader,
			Stream? aniStream,
			BinaryReader? aniReader,
			CancellationToken cancellationToken)
		{
			var boneCount = Math.Max(1, bones.Count);
			var bonesByIndex = new MdlBone?[boneCount];
			for (var i = 0; i < bones.Count; i++)
			{
				var bone = bones[i];
				if ((uint)bone.Index < (uint)boneCount)
				{
					bonesByIndex[bone.Index] = bone;
				}
			}

			var sectionContexts = ReadFrameAnimSectionContexts(
				animDesc,
				sections,
				animBlocks,
				boneCount,
				mdlStream,
				mdlReader,
				aniStream,
				aniReader);

			var positionsByBoneIndex = new Vector3[boneCount];
			var rotationsByBoneIndex = new Vector3[boneCount];
			var quaternion48SBytes = new byte[6];

			var useDeltaBasePose = (animDesc.Flags & StudioAnimDescDelta) != 0;

			for (var frameIndex = 0; frameIndex < animDesc.FrameCount; frameIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				await writer.WriteLineAsync($"{timePrefix}{frameIndex}");

				var (sectionIndex, localFrameIndex) = GetSectionFrame(animDesc, sections, frameIndex);
				var sectionContext = (uint)sectionIndex < (uint)sectionContexts.Length ? sectionContexts[sectionIndex] : null;

				if (sectionContext is null)
				{
					for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
					{
						var bone = bonesByIndex[boneIndex];
						positionsByBoneIndex[boneIndex] = useDeltaBasePose ? Vector3.Zero : bone?.Position ?? Vector3.Zero;
						rotationsByBoneIndex[boneIndex] = useDeltaBasePose ? Vector3.Zero : bone?.RotationRadians ?? Vector3.Zero;
					}
				}
				else
				{
					var hasFrameData = sectionContext.FrameLength > 0 && sectionContext.FrameDataStart > 0;
					long frameStart = 0;
					if (hasFrameData)
					{
						frameStart = sectionContext.FrameDataStart + (long)localFrameIndex * sectionContext.FrameLength;
						if (frameStart < 0 || frameStart + sectionContext.FrameLength > sectionContext.Stream.Length)
						{
							hasFrameData = false;
						}
						else
						{
							sectionContext.Stream.Seek(frameStart, SeekOrigin.Begin);
						}
					}

					var frameStartPosition = hasFrameData ? sectionContext.Stream.Position : 0;

					for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
					{
						var bone = bonesByIndex[boneIndex];
						var position = useDeltaBasePose ? Vector3.Zero : bone?.Position ?? Vector3.Zero;
						var rotation = useDeltaBasePose ? Vector3.Zero : bone?.RotationRadians ?? Vector3.Zero;

						var boneFlag = sectionContext.BoneFlags[boneIndex];
						var constants = sectionContext.Constants[boneIndex];

						// Match Crowbar's precedence: RAWROT/RAWPOS then CONST_POS2/CONST_ROT2.
						if ((boneFlag & StudioFrameRawRot) != 0 && constants.RawRot is not null)
						{
							rotation = ToEulerAngles(constants.RawRot.Value);
						}
						if ((boneFlag & StudioFrameRawPos) != 0 && constants.RawPos is not null)
						{
							position = constants.RawPos.Value;
						}
						if ((boneFlag & StudioFrameConstPos2) != 0 && constants.ConstPos2 is not null)
						{
							position = constants.ConstPos2.Value;
						}
						if ((boneFlag & StudioFrameConstRot2) != 0 && constants.ConstRot2 is not null)
						{
							rotation = ToEulerAngles(constants.ConstRot2.Value);
						}

						Quaternion? animRot2 = null;
						Quaternion? animRot = null;
						Vector3? animPos = null;
						Vector3? fullAnimPos = null;

						if (hasFrameData)
						{
							if ((boneFlag & StudioFrameAnimRot2) != 0)
							{
								if (!TryReadBytesExact(sectionContext.Reader, quaternion48SBytes))
								{
									hasFrameData = false;
								}
								else
								{
									animRot2 = DecodeQuaternion48S(quaternion48SBytes);
								}
							}

							if (hasFrameData && (boneFlag & StudioFrameAnimRot) != 0)
							{
								var x = sectionContext.Reader.ReadUInt16();
								var y = sectionContext.Reader.ReadUInt16();
								var zw = sectionContext.Reader.ReadUInt16();
								animRot = DecodeQuaternion48(x, y, zw);
							}

							if (hasFrameData && (boneFlag & StudioFrameAnimPos) != 0)
							{
								var x = sectionContext.Reader.ReadUInt16();
								var y = sectionContext.Reader.ReadUInt16();
								var z = sectionContext.Reader.ReadUInt16();
								animPos = DecodeVector48(x, y, z);
							}

							if (hasFrameData && (boneFlag & StudioFrameFullAnimPos) != 0)
							{
								fullAnimPos = new Vector3(
									sectionContext.Reader.ReadSingle(),
									sectionContext.Reader.ReadSingle(),
									sectionContext.Reader.ReadSingle());
							}
						}

						// Match Crowbar's precedence: ANIMROT/ANIMPOS then FULLANIMPOS, with ANIM_ROT2 last.
						if ((boneFlag & StudioFrameAnimRot) != 0 && animRot is not null)
						{
							rotation = ToEulerAngles(animRot.Value);
						}
						if ((boneFlag & StudioFrameAnimPos) != 0 && animPos is not null)
						{
							position = animPos.Value;
						}
						if ((boneFlag & StudioFrameFullAnimPos) != 0 && fullAnimPos is not null)
						{
							position = fullAnimPos.Value;
						}
						if ((boneFlag & StudioFrameAnimRot2) != 0 && animRot2 is not null)
						{
							rotation = ToEulerAngles(animRot2.Value);
						}

						positionsByBoneIndex[boneIndex] = position;
						rotationsByBoneIndex[boneIndex] = rotation;
					}

					if (hasFrameData && sectionContext.FrameLength > 0 && sectionContext.Stream.Position != frameStartPosition + sectionContext.FrameLength)
					{
						// Ensure we are not thrown off by unexpected bytes; the next frame is always addressed explicitly anyway.
						sectionContext.Stream.Seek(frameStartPosition + sectionContext.FrameLength, SeekOrigin.Begin);
					}
				}

				for (var i = 0; i < bones.Count; i++)
				{
					var bone = bones[i];
					var boneIndex = bone.Index;
					var position = (uint)boneIndex < (uint)positionsByBoneIndex.Length ? positionsByBoneIndex[boneIndex] : bone.Position;
					var rotation = (uint)boneIndex < (uint)rotationsByBoneIndex.Length ? rotationsByBoneIndex[boneIndex] : bone.RotationRadians;
					ApplyRootBoneAdjustments(modelVersion, frameIndex, bone, animDesc.Movements, ref position, ref rotation);
					position = NormalizeSignedZeros(position);
					rotation = NormalizeSignedZeros(rotation);

					await writer.WriteLineAsync(string.Format(
						format,
						"{0} {1:0.######} {2:0.######} {3:0.######} {4:0.######} {5:0.######} {6:0.######}",
						bone.Index,
						position.X, position.Y, position.Z,
						rotation.X, rotation.Y, rotation.Z));
				}
			}
		}

		private static FrameAnimSection?[] ReadFrameAnimSectionContexts(
			MdlAnimationDesc animDesc,
			IReadOnlyList<AnimSection> sections,
			IReadOnlyList<AnimBlock> animBlocks,
			int boneCount,
			Stream mdlStream,
			BinaryReader mdlReader,
			Stream? aniStream,
			BinaryReader? aniReader)
		{
			var output = new FrameAnimSection?[sections.Count];
			for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
			{
				var section = sections[sectionIndex];

				Stream animStream;
				BinaryReader animReader;
				long animDataOffset;
				if (section.AnimBlock == 0)
				{
					var adjustedAnimOffset = section.AnimOffset;
					if (sections.Count > 1)
					{
						adjustedAnimOffset = section.AnimOffset + (animDesc.AnimOffset - sections[0].AnimOffset);
					}

					animStream = mdlStream;
					animReader = mdlReader;
					animDataOffset = animDesc.OffsetStart + adjustedAnimOffset;
				}
				else
				{
					if (aniStream is null || aniReader is null || (uint)section.AnimBlock >= (uint)animBlocks.Count)
					{
						continue;
					}

					animStream = aniStream;
					animReader = aniReader;
					animDataOffset = animBlocks[section.AnimBlock].DataStart + (long)section.AnimOffset;
				}

				output[sectionIndex] = TryReadFrameAnimSection(animStream, animReader, animDataOffset, boneCount);
			}

			return output;
		}

		private static FrameAnimSection? TryReadFrameAnimSection(Stream stream, BinaryReader reader, long animDataOffset, int boneCount)
		{
			// mstudio_frame_anim_t header is 24 bytes, followed by boneFlags[boneCount].
			if (animDataOffset < 0 || animDataOffset + 24 > stream.Length)
			{
				return null;
			}

			if (boneCount <= 0)
			{
				return null;
			}

			stream.Seek(animDataOffset, SeekOrigin.Begin);
			var constantsOffset = reader.ReadInt32();
			var frameOffset = reader.ReadInt32();
			var frameLength = reader.ReadInt32();
			_ = reader.ReadInt32();
			_ = reader.ReadInt32();
			_ = reader.ReadInt32();

			var boneFlags = reader.ReadBytes(boneCount);
			if (boneFlags.Length != boneCount)
			{
				return null;
			}

			var constants = new FrameAnimBoneConstants[boneCount];
			if (constantsOffset != 0)
			{
				var constantsStart = animDataOffset + constantsOffset;
				if (constantsStart > 0 && constantsStart < stream.Length)
				{
					stream.Seek(constantsStart, SeekOrigin.Begin);
					var quaternion48SBytes = new byte[6];
					for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
					{
						var boneFlag = boneFlags[boneIndex];

						Quaternion? constRot2 = null;
						Vector3? constPos2 = null;
						Quaternion? rawRot = null;
						Vector3? rawPos = null;

						// Read order matches Crowbar's SourceMdlFile49.ReadAnimationFrameByBone().
						if ((boneFlag & StudioFrameConstRot2) != 0)
						{
							if (TryReadBytesExact(reader, quaternion48SBytes))
							{
								constRot2 = DecodeQuaternion48S(quaternion48SBytes);
							}
						}
						if ((boneFlag & StudioFrameConstPos2) != 0)
						{
							constPos2 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
						}
						if ((boneFlag & StudioFrameRawRot) != 0)
						{
							var x = reader.ReadUInt16();
							var y = reader.ReadUInt16();
							var zw = reader.ReadUInt16();
							rawRot = DecodeQuaternion48(x, y, zw);
						}
						if ((boneFlag & StudioFrameRawPos) != 0)
						{
							var x = reader.ReadUInt16();
							var y = reader.ReadUInt16();
							var z = reader.ReadUInt16();
							rawPos = DecodeVector48(x, y, z);
						}

						constants[boneIndex] = new FrameAnimBoneConstants(rawPos, rawRot, constPos2, constRot2);
					}
				}
			}

			var frameDataStart = frameOffset != 0 ? animDataOffset + frameOffset : 0;

			return new FrameAnimSection(stream, reader, boneFlags, constants, frameDataStart, frameLength);
		}

		private static bool TryReadBytesExact(BinaryReader reader, byte[] buffer)
		{
			try
			{
				var offset = 0;
				while (offset < buffer.Length)
				{
					var read = reader.Read(buffer, offset, buffer.Length - offset);
					if (read <= 0)
					{
						return false;
					}

					offset += read;
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		private static Animation?[] ReadAnimationsForSection(
			Stream stream,
			BinaryReader reader,
			long animDataOffset,
		int boneCount,
		int sectionFrameCount,
		bool lastSectionIsBeingRead)
	{
		var animationsByBone = new Animation?[boneCount];

		if (animDataOffset < 0 || animDataOffset >= stream.Length)
		{
			return animationsByBone;
		}

		var animPos = animDataOffset;
		for (var j = 0; j < boneCount; j++)
		{
			if (animPos < 0 || animPos + 4 > stream.Length)
			{
				break;
			}

			stream.Seek(animPos, SeekOrigin.Begin);
			var entryStart = animPos;

			var boneIndex = reader.ReadByte();
			if (boneIndex == 255)
			{
				_ = reader.ReadByte();
				_ = reader.ReadInt16();
				break;
			}

			if (boneIndex >= boneCount)
			{
				break;
			}

			var flags = reader.ReadByte();
			var nextOffset = reader.ReadInt16();

			Quaternion? rawRot = null;
			Vector3? rawPos = null;
			if ((flags & StudioAnimRawRot2) != 0)
			{
				var bytes = reader.ReadBytes(8);
				if (bytes.Length == 8)
				{
					rawRot = DecodeQuaternion64(bytes);
				}
			}
			if ((flags & StudioAnimRawRot) != 0)
			{
				var x = reader.ReadUInt16();
				var y = reader.ReadUInt16();
				var zw = reader.ReadUInt16();
				rawRot = DecodeQuaternion48(x, y, zw);
			}
			if ((flags & StudioAnimRawPos) != 0)
			{
				var x = reader.ReadUInt16();
				var y = reader.ReadUInt16();
				var z = reader.ReadUInt16();
				rawPos = DecodeVector48(x, y, z);
			}

			AnimValuePointer? rotV = null;
			AnimValuePointer? posV = null;

			long rotPtrPos = 0;
			long posPtrPos = 0;
			short rotXOff = 0;
			short rotYOff = 0;
			short rotZOff = 0;
			short posXOff = 0;
			short posYOff = 0;
			short posZOff = 0;

			if ((flags & StudioAnimAnimRot) != 0)
			{
				rotPtrPos = stream.Position;
				rotXOff = reader.ReadInt16();
				rotYOff = reader.ReadInt16();
				rotZOff = reader.ReadInt16();
			}

			if ((flags & StudioAnimAnimPos) != 0)
			{
				posPtrPos = stream.Position;
				posXOff = reader.ReadInt16();
				posYOff = reader.ReadInt16();
				posZOff = reader.ReadInt16();
			}

			if ((flags & StudioAnimAnimRot) != 0)
			{
				var xVals = rotXOff > 0 ? ReadAnimValues(stream, reader, rotPtrPos + rotXOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();
				var yVals = rotYOff > 0 ? ReadAnimValues(stream, reader, rotPtrPos + rotYOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();
				var zVals = rotZOff > 0 ? ReadAnimValues(stream, reader, rotPtrPos + rotZOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();

				rotV = new AnimValuePointer(rotXOff, rotYOff, rotZOff, xVals, yVals, zVals);
			}

			if ((flags & StudioAnimAnimPos) != 0)
			{
				var xVals = posXOff > 0 ? ReadAnimValues(stream, reader, posPtrPos + posXOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();
				var yVals = posYOff > 0 ? ReadAnimValues(stream, reader, posPtrPos + posYOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();
				var zVals = posZOff > 0 ? ReadAnimValues(stream, reader, posPtrPos + posZOff, sectionFrameCount, lastSectionIsBeingRead) : Array.Empty<short>();

				posV = new AnimValuePointer(posXOff, posYOff, posZOff, xVals, yVals, zVals);
			}

			animationsByBone[boneIndex] = new Animation(boneIndex, flags, rawPos, rawRot, posV, rotV);

			if (nextOffset == 0)
			{
				break;
			}

			animPos = entryStart + nextOffset;
		}

		return animationsByBone;
	}

	private static IReadOnlyList<short> ReadAnimValues(Stream stream, BinaryReader reader, long offset, int frameCount, bool lastSectionIsBeingRead)
	{
		if (offset < 0 || offset + 2 > stream.Length)
		{
			return Array.Empty<short>();
		}

		stream.Seek(offset, SeekOrigin.Begin);

		var values = new List<short>(capacity: 64);
		var remaining = frameCount;
		var accumulatedTotal = 0;
		while (remaining > 0 && stream.Position + 2 <= stream.Length)
		{
			var raw = reader.ReadInt16();
			var total = GetAnimValueTotal(raw);
			accumulatedTotal += total;
			if (total == 0)
			{
				break;
			}

			remaining -= total;
			values.Add(raw);

			var valid = GetAnimValueValid(raw);
			for (var i = 0; i < valid && stream.Position + 2 <= stream.Length; i++)
			{
				values.Add(reader.ReadInt16());
			}
		}

		if (!lastSectionIsBeingRead && accumulatedTotal == frameCount && stream.Position + 4 <= stream.Length)
		{
			_ = reader.ReadInt16();
			_ = reader.ReadInt16();
		}

		return values;
	}

	private static Vector3 CalcBonePosition(int frameIndex, MdlBone bone, Animation? anim)
	{
		if (anim is null)
		{
			return bone.Position;
		}

		if ((anim.Flags & StudioAnimRawPos) != 0 && anim.RawPos is not null)
		{
			return anim.RawPos.Value;
		}

		if ((anim.Flags & StudioAnimAnimPos) == 0)
		{
			return (anim.Flags & StudioAnimDelta) != 0 ? Vector3.Zero : bone.Position;
		}

		var pos = Vector3.Zero;
		if (anim.PosV is not null)
		{
			pos.X = anim.PosV.XOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.PosV.XValues, bone.PositionScale.X);
			pos.Y = anim.PosV.YOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.PosV.YValues, bone.PositionScale.Y);
			pos.Z = anim.PosV.ZOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.PosV.ZValues, bone.PositionScale.Z);
		}

		if ((anim.Flags & StudioAnimDelta) == 0)
		{
			pos += bone.Position;
		}

		return pos;
	}

	private static Vector3 CalcBoneRotation(int frameIndex, MdlBone bone, Animation? anim)
	{
		if (anim is null)
		{
			return bone.RotationRadians;
		}

		if ((anim.Flags & (StudioAnimRawRot | StudioAnimRawRot2)) != 0 && anim.RawRot is not null)
		{
			return ToEulerAngles(anim.RawRot.Value);
		}

		if ((anim.Flags & StudioAnimAnimRot) == 0)
		{
			return (anim.Flags & StudioAnimDelta) != 0 ? Vector3.Zero : bone.RotationRadians;
		}

		var angles = Vector3.Zero;
		if (anim.RotV is not null)
		{
			angles.X = anim.RotV.XOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.RotV.XValues, bone.RotationScale.X);
			angles.Y = anim.RotV.YOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.RotV.YValues, bone.RotationScale.Y);
			angles.Z = anim.RotV.ZOffset <= 0 ? 0 : (float)ExtractAnimValue(frameIndex, anim.RotV.ZValues, bone.RotationScale.Z);
		}

		if ((anim.Flags & StudioAnimDelta) == 0)
		{
			angles += bone.RotationRadians;
		}

		return angles;
	}

	private static double ExtractAnimValue(int frameIndex, IReadOnlyList<short> animValues, float scale)
	{
		if (animValues.Count == 0)
		{
			return 0;
		}

		var k = frameIndex;
		var animValueIndex = 0;
		while (GetAnimValueTotal(animValues[animValueIndex]) <= k)
		{
			k -= GetAnimValueTotal(animValues[animValueIndex]);
			animValueIndex += GetAnimValueValid(animValues[animValueIndex]) + 1;
			if (animValueIndex >= animValues.Count || GetAnimValueTotal(animValues[animValueIndex]) == 0)
			{
				return 0;
			}
		}

		if (GetAnimValueValid(animValues[animValueIndex]) > k)
		{
			return animValues[animValueIndex + k + 1] * scale;
		}

		return animValues[animValueIndex + GetAnimValueValid(animValues[animValueIndex])] * scale;
	}

	private static byte GetAnimValueValid(short raw)
	{
		return (byte)((ushort)raw & 0xFF);
	}

	private static int GetAnimValueTotal(short raw)
	{
		return (byte)(((ushort)raw >> 8) & 0xFF);
	}

	// Crowbar rotates the root bone -90° around Z when converting coordinates. Our
	// loader already aligns the root orientation, so applying that extra offset
	// would double-rotate and lose the expected yaw. Keep zero here to preserve
	// the animation's stored root yaw.
	private const float RootRotationOffsetRadians = 0f;

	private static void ApplyRootBoneAdjustments(int modelVersion, int frameIndex, MdlBone bone, IReadOnlyList<MdlMovement> movements, ref Vector3 position, ref Vector3 rotation)
	{
		if (bone.ParentIndex != -1)
		{
			return;
		}

		if (frameIndex > 0 && movements.Count > 0)
		{
			var (deltaPosition, deltaYawRadians) = CalcPiecewiseMovement(frameIndex, movements);
			position += deltaPosition;
			rotation = new Vector3(rotation.X, rotation.Y, rotation.Z + deltaYawRadians);
		}

		if (modelVersion <= 47)
		{
			position = new Vector3(position.X, position.Y, position.Z);
		}
		else
		{
			position = new Vector3(position.Y, -position.X, position.Z);
		}
		rotation = new Vector3(rotation.X, rotation.Y, rotation.Z + RootRotationOffsetRadians);
	}

	private static Vector3 NormalizeSignedZeros(Vector3 value)
	{
		return new Vector3(
			NormalizeZero(value.X),
			NormalizeZero(value.Y),
			NormalizeZero(value.Z));
	}

	private static float NormalizeZero(float value)
	{
		return value;
	}

	private static (Vector3 DeltaPosition, float DeltaYawRadians) CalcPiecewiseMovement(int frameIndex, IReadOnlyList<MdlMovement> movements)
	{
		var previousFrameIndex = 0;
		var position = Vector3.Zero;
		var yawRadians = 0f;

		for (var i = 0; i < movements.Count; i++)
		{
			var movement = movements[i];
			if (frameIndex <= movement.EndFrameIndex)
			{
				var span = movement.EndFrameIndex - previousFrameIndex;
				if (span <= 0)
				{
					break;
				}

				var f = (float)(frameIndex - previousFrameIndex) / span;
				var d = movement.V0 * f + 0.5f * (movement.V1 - movement.V0) * f * f;

				position += d * movement.Vector;

				var movementYawRadians = movement.AngleDegrees * (MathF.PI / 180f);
				yawRadians = yawRadians * (1 - f) + movementYawRadians * f;

				return (position, yawRadians);
			}

			previousFrameIndex = movement.EndFrameIndex;
			position = movement.Position;
			yawRadians = movement.AngleDegrees * (MathF.PI / 180f);
		}

		return (position, yawRadians);
	}

	private static Vector3 DecodeVector48(ushort xBits, ushort yBits, ushort zBits)
	{
		var x = (float)BitConverter.UInt16BitsToHalf(xBits);
		var y = (float)BitConverter.UInt16BitsToHalf(yBits);
		var z = (float)BitConverter.UInt16BitsToHalf(zBits);
		return new Vector3(x, y, z);
	}

	private static Quaternion DecodeQuaternion48(ushort xInput, ushort yInput, ushort zwInput)
	{
		var x = ((int)xInput - 32768) * (1f / 32768f);
		var y = ((int)yInput - 32768) * (1f / 32768f);
		var zInput = zwInput & 0x7FFF;
		var z = ((int)zInput - 16384) * (1f / 16384f);
		var w = MathF.Sqrt(MathF.Max(0f, 1f - x * x - y * y - z * z));
		if ((zwInput & 0x8000) != 0)
		{
			w = -w;
		}

		return new Quaternion(x, y, z, w);
	}

	private static Quaternion DecodeQuaternion48S(ReadOnlySpan<byte> bytes)
	{
		// Port of Crowbar's SourceQuaternion48bitsViaBytes (Quaternion48S).
		if (bytes.Length < 6)
		{
			return Quaternion.Identity;
		}

		var uA = ((bytes[1] & 0x7F) << 8) | bytes[0];
		var uB = ((bytes[3] & 0x7F) << 8) | bytes[2];
		var uC = ((bytes[5] & 0x7F) << 8) | bytes[4];

		var missingComponentIndex = ((bytes[1] & 0x80) >> 6) | ((bytes[3] & 0x80) >> 7);
		var missingComponentSign = (bytes[5] & 0x80) != 0 ? -1f : 1f;

		const float shift48S = 16384f;
		const float scale48S = 23168f;

		var a = (uA - shift48S) / scale48S;
		var b = (uB - shift48S) / scale48S;
		var c = (uC - shift48S) / scale48S;

		var missing = MathF.Sqrt(MathF.Max(0f, 1f - a * a - b * b - c * c)) * missingComponentSign;

		return missingComponentIndex switch
		{
			1 => new Quaternion(missing, a, b, c), // missing X
			2 => new Quaternion(c, missing, a, b), // missing Y
			3 => new Quaternion(b, c, missing, a), // missing Z
			_ => new Quaternion(a, b, c, missing)  // missing W
		};
	}

	private static Quaternion DecodeQuaternion64(byte[] bytes)
	{
		var b0 = bytes[0];
		var b1 = bytes[1];
		var b2 = bytes[2];
		var b3 = bytes[3];
		var b4 = bytes[4];
		var b5 = bytes[5];
		var b6 = bytes[6];
		var b7 = bytes[7];

		var xBits = (b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0x1F) << 16);
		var yBits = ((b2 & 0xE0) >> 5) | ((b3 & 0xFF) << 3) | ((b4 & 0xFF) << 11) | ((b5 & 0x03) << 19);
		var zBits = ((b5 & 0xFC) >> 2) | ((b6 & 0xFF) << 6) | ((b7 & 0x7F) << 14);

		var x = (xBits - 1048576) * (1f / 1048576.5f);
		var y = (yBits - 1048576) * (1f / 1048576.5f);
		var z = (zBits - 1048576) * (1f / 1048576.5f);
		var w = MathF.Sqrt(MathF.Max(0f, 1f - x * x - y * y - z * z));
		if ((b7 & 0x80) != 0)
		{
			w = -w;
		}

		return new Quaternion(x, y, z, w);
	}

	private static Vector3 ToEulerAngles(Quaternion q)
	{
		// Port of Crowbar's MathModule.ToEulerAngles(q) using Eul_FromQuat(q, 0,1,2,0, Even, No, S).
		var x = (double)q.X;
		var y = (double)q.Y;
		var z = (double)q.Z;
		var w = (double)q.W;

		var nq = x * x + y * y + z * z + w * w;
		var s = nq > 0 ? 2.0 / nq : 0.0;

		var xs = x * s;
		var ys = y * s;
		var zs = z * s;

		var wx = w * xs;
		var wy = w * ys;
		var wz = w * zs;
		var xx = x * xs;
		var xy = x * ys;
		var xz = x * zs;
		var yy = y * ys;
		var yz = y * zs;
		var zz = z * zs;

		// 3x3 portion of matrix.
		var m00 = 1.0 - (yy + zz);
		var m01 = xy - wz;
		var m02 = xz + wy;
		var m10 = xy + wz;
		var m11 = 1.0 - (xx + zz);
		var m12 = yz - wx;
		var m20 = xz - wy;
		var m21 = yz + wx;
		var m22 = 1.0 - (xx + yy);

		// Eul_FromHMatrix with i=0,j=1,k=2, parity=Even, repeat=No, frame=S.
		var cy = Math.Sqrt(m00 * m00 + m10 * m10);
		double ex;
		double ey;
		double ez;

		if (cy > 16 * 0.00001)
		{
			ex = Math.Atan2(m21, m22);
			ey = Math.Atan2(-m20, cy);
			ez = Math.Atan2(m10, m00);
		}
		else
		{
			ex = Math.Atan2(-m12, m11);
			ey = Math.Atan2(-m20, cy);
			ez = 0;
		}

		return new Vector3((float)ex, (float)ey, (float)ez);
	}

		private readonly record struct AnimSection(int AnimBlock, int AnimOffset);

		private readonly record struct AnimBlock(int DataStart, int DataEnd);

		private sealed record FrameAnimSection(
			Stream Stream,
			BinaryReader Reader,
			byte[] BoneFlags,
			FrameAnimBoneConstants[] Constants,
			long FrameDataStart,
			int FrameLength);

		private readonly record struct FrameAnimBoneConstants(
			Vector3? RawPos,
			Quaternion? RawRot,
			Vector3? ConstPos2,
			Quaternion? ConstRot2);

		private sealed record Animation(
			byte BoneIndex,
			byte Flags,
			Vector3? RawPos,
		Quaternion? RawRot,
		AnimValuePointer? PosV,
		AnimValuePointer? RotV);

	private sealed record AnimValuePointer(
		short XOffset,
		short YOffset,
		short ZOffset,
		IReadOnlyList<short> XValues,
		IReadOnlyList<short> YValues,
		IReadOnlyList<short> ZValues);

	private const byte StudioAnimRawPos = 0x01;
	private const byte StudioAnimRawRot = 0x02;
		private const byte StudioAnimAnimPos = 0x04;
		private const byte StudioAnimAnimRot = 0x08;
		private const byte StudioAnimDelta = 0x10;
		private const byte StudioAnimRawRot2 = 0x20;

		private const int StudioAnimDescDelta = 0x04;
		private const int StudioAnimDescFrameAnim = 0x40;

		private const byte StudioFrameRawPos = 0x01;
		private const byte StudioFrameRawRot = 0x02;
		private const byte StudioFrameAnimPos = 0x04;
		private const byte StudioFrameAnimRot = 0x08;
		private const byte StudioFrameFullAnimPos = 0x10;
		private const byte StudioFrameConstPos2 = 0x20;
		private const byte StudioFrameConstRot2 = 0x40;
		private const byte StudioFrameAnimRot2 = 0x80;

		private static IReadOnlyList<AnimBlock> ReadAnimBlocks(Stream stream, BinaryReader reader, MdlHeader header)
		{
			if (header.AnimBlockCount <= 0 || header.AnimBlockOffset <= 0)
			{
			return Array.Empty<AnimBlock>();
		}

		if (header.AnimBlockOffset >= stream.Length)
		{
			return Array.Empty<AnimBlock>();
		}

		var bytesNeeded = (long)header.AnimBlockCount * 8;
		if ((long)header.AnimBlockOffset + bytesNeeded > stream.Length)
		{
			return Array.Empty<AnimBlock>();
		}

		stream.Seek(header.AnimBlockOffset, SeekOrigin.Begin);
		var blocks = new AnimBlock[header.AnimBlockCount];
		for (var i = 0; i < header.AnimBlockCount; i++)
		{
			var dataStart = reader.ReadInt32();
			var dataEnd = reader.ReadInt32();
			blocks[i] = new AnimBlock(dataStart, dataEnd);
		}

		return blocks;
	}

	private static string? TryResolveAniPath(
		string mdlPath,
		string directoryPath,
		string baseName,
		Stream mdlStream,
		BinaryReader mdlReader,
		MdlHeader header)
	{
		if (header.AnimBlockCount <= 0)
		{
			return null;
		}

		var rel = string.Empty;
		if (header.AnimBlockNameOffset > 0)
		{
			rel = ReadNullTerminatedStringAt(mdlStream, mdlReader, header.AnimBlockNameOffset, maxBytes: 1024).Trim();
		}

		var candidates = new List<string>(capacity: 4);

		if (!string.IsNullOrWhiteSpace(rel))
		{
			rel = rel.Replace('\\', '/');
			while (rel.Contains("//", StringComparison.Ordinal))
			{
				rel = rel.Replace("//", "/", StringComparison.Ordinal);
			}

			rel = rel.Trim();

			if (rel.StartsWith("/models/", StringComparison.OrdinalIgnoreCase))
			{
				rel = rel.TrimStart('/');
			}

			if (string.IsNullOrWhiteSpace(Path.GetExtension(rel)))
			{
				rel += ".ani";
			}

			if (Path.IsPathRooted(rel))
			{
				candidates.Add(rel);
			}

			candidates.Add(Path.Combine(directoryPath, rel));

			var mdlFull = Path.GetFullPath(mdlPath).Replace('\\', '/');
			var idx = mdlFull.IndexOf("/models/", StringComparison.OrdinalIgnoreCase);
			if (idx >= 0)
			{
				var gameRoot = mdlFull[..idx];
				candidates.Add(Path.Combine(gameRoot, rel));
			}

			candidates.Add(Path.Combine(directoryPath, Path.GetFileName(rel)));
		}

		candidates.Add(Path.Combine(directoryPath, baseName + ".ani"));

		foreach (var candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static string ReadNullTerminatedStringAt(Stream stream, BinaryReader reader, long offset, int maxBytes)
	{
		if (offset < 0 || offset >= stream.Length || maxBytes <= 0)
		{
			return string.Empty;
		}

		stream.Seek(offset, SeekOrigin.Begin);

		var bytes = new byte[Math.Min(maxBytes, 1024)];
		var length = 0;

		while (length < bytes.Length && stream.Position < stream.Length)
		{
			var b = reader.ReadByte();
			if (b == 0)
			{
				break;
			}

			bytes[length] = b;
			length++;
		}

		return length > 0 ? Encoding.ASCII.GetString(bytes, 0, length) : string.Empty;
	}

	private static Stream OpenReadStream(string path, AccessedBytesLog? accessedBytesLog)
	{
		var stream = (Stream)new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		if (accessedBytesLog is not null)
		{
			stream = new AccessLoggedStream(stream, accessedBytesLog);
		}

		return stream;
	}
}
