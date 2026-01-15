using Stunstick.Core.Mdl;
using Stunstick.Core.Vvd;
using System.Globalization;
using System.Numerics;

namespace Stunstick.App.Decompile;

internal static class MdlVertexAnimationVtaWriter
{
	internal sealed record VtaResult(
		string VtaFileName,
		IReadOnlyList<FlexFrame> FlexFrames
	);

	internal sealed class FlexFrame
	{
		public int FrameIndex { get; init; }
		public int FlexDescIndex { get; init; }
		public float Target0 { get; init; }
		public float Target1 { get; init; }
		public float Target2 { get; init; }
		public float Target3 { get; init; }
		public string FlexName { get; init; } = string.Empty;
		public string FlexDescription { get; init; } = string.Empty;
		public bool FlexHasPartner { get; init; }
		public float FlexSplit { get; init; }

		public List<(MdlFlex Flex, int BodyAndMeshVertexIndexStart)> Flexes { get; } = new();
	}

	public static async Task<VtaResult?> WriteVtaAsync(
		string modelOutputFolder,
		string modelName,
		MdlModel model,
		VvdFile vvd,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		options ??= new DecompileOptions();

		var flexFrames = CreateFlexFrames(model, options);
		if (flexFrames.Count <= 1)
		{
			return null;
		}

		var hasAnyVertexAnimationData = flexFrames.Any(frame => frame.Flexes.Any(entry => entry.Flex.VertAnims.Count > 0));
		if (!hasAnyVertexAnimationData)
		{
			return null;
		}

		var vtaFileName = GetVtaFileName(modelName, bodyPartIndex: 0);
		var vtaPathFileName = Path.Combine(modelOutputFolder, vtaFileName);

		await using var stream = new FileStream(vtaPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

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

		await WriteSkeletonSectionForVertexAnimationAsync(writer, flexFrames, options);
		await WriteVertexAnimationSectionAsync(writer, model, vvd, flexFrames, options, cancellationToken);

		await writer.FlushAsync();
		return new VtaResult(vtaFileName, flexFrames);
	}

	private static async Task WriteSkeletonSectionForVertexAnimationAsync(
		StreamWriter writer,
		IReadOnlyList<FlexFrame> flexFrames,
		DecompileOptions options)
	{
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0 # basis shape key");

		for (var frameIndex = 1; frameIndex < flexFrames.Count; frameIndex++)
		{
			var frame = flexFrames[frameIndex];
			await writer.WriteLineAsync($"{timePrefix}{frameIndex} # {frame.FlexDescription}");
		}

		await writer.WriteLineAsync("end");
	}

	private static async Task WriteVertexAnimationSectionAsync(
		StreamWriter writer,
		MdlModel model,
		VvdFile vvd,
		IReadOnlyList<FlexFrame> flexFrames,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		var format = CultureInfo.InvariantCulture;
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await writer.WriteLineAsync("vertexanimation");
		await writer.WriteLineAsync($"{timePrefix}0 # basis shape key");

		var baseVertexes = vvd.Header.FixupCount <= 0 || vvd.FixedVertexesByLod.Count == 0
			? vvd.Vertexes
			: vvd.FixedVertexesByLod[0];

		for (var vertexIndex = 0; vertexIndex < baseVertexes.Count; vertexIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var vertex = baseVertexes[vertexIndex];

			var position = vertex.Position;
			var normal = vertex.Normal;
			TransformForStaticProp(model.Header.Flags, ref position, ref normal);

			await writer.WriteLineAsync(string.Format(
				format,
				"    {0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000}",
				vertexIndex,
				position.X, position.Y, position.Z,
				normal.X, normal.Y, normal.Z));
		}

		for (var frameIndex = 1; frameIndex < flexFrames.Count; frameIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var frame = flexFrames[frameIndex];
			await writer.WriteLineAsync($"{timePrefix}{frameIndex} # {frame.FlexDescription}");

			for (var entryIndex = 0; entryIndex < frame.Flexes.Count; entryIndex++)
			{
				var (flex, bodyAndMeshVertexIndexStart) = frame.Flexes[entryIndex];
				var vertAnims = flex.VertAnims;
				for (var vertAnimIndex = 0; vertAnimIndex < vertAnims.Count; vertAnimIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var vertAnim = vertAnims[vertAnimIndex];
					var absoluteVertexIndex = bodyAndMeshVertexIndexStart + vertAnim.Index;
					if ((uint)absoluteVertexIndex >= (uint)baseVertexes.Count)
					{
						continue;
					}

					var baseVertex = baseVertexes[absoluteVertexIndex];

					var basePosition = baseVertex.Position;
					var baseNormal = baseVertex.Normal;
					TransformForStaticProp(model.Header.Flags, ref basePosition, ref baseNormal);

					var delta = new Vector3(
						Float16BitsToSingle(vertAnim.Delta0),
						Float16BitsToSingle(vertAnim.Delta1),
						Float16BitsToSingle(vertAnim.Delta2));
					var nDelta = new Vector3(
						Float16BitsToSingle(vertAnim.NDelta0),
						Float16BitsToSingle(vertAnim.NDelta1),
						Float16BitsToSingle(vertAnim.NDelta2));

					TransformForStaticProp(model.Header.Flags, ref delta, ref nDelta);

					var position = basePosition + delta;
					var normal = baseNormal + nDelta;

					await writer.WriteLineAsync(string.Format(
						format,
						"    {0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000}",
						absoluteVertexIndex,
						position.X, position.Y, position.Z,
						normal.X, normal.Y, normal.Z));
				}
			}
		}

		await writer.WriteLineAsync("end");
	}

	private static void TransformForStaticProp(int mdlFlags, ref Vector3 position, ref Vector3 normal)
	{
		if ((mdlFlags & MdlConstants.StudioHdrFlagsStaticProp) == 0)
		{
			return;
		}

		position = new Vector3(position.Y, -position.X, position.Z);
		normal = new Vector3(normal.Y, -normal.X, normal.Z);
	}

	private static float Float16BitsToSingle(ushort bits)
	{
		var value = (float)BitConverter.Int16BitsToHalf(unchecked((short)bits));
		if (float.IsNaN(value))
		{
			return 0f;
		}

		if (float.IsInfinity(value))
		{
			return MathF.CopySign(65504f, value);
		}

		return value;
	}

	private static string GetVtaFileName(string modelName, int bodyPartIndex)
	{
		return $"{modelName}_{bodyPartIndex + 1:00}.vta";
	}

	private static List<FlexFrame> CreateFlexFrames(MdlModel model, DecompileOptions options)
	{
		options ??= new DecompileOptions();

		var frames = new List<FlexFrame>();
		frames.Add(new FlexFrame
		{
			FrameIndex = 0,
			FlexDescIndex = -1,
			FlexName = "defaultflex",
			FlexDescription = "defaultflex"
		});

		if (model.FlexDescs.Count == 0 || model.BodyParts.Count == 0)
		{
			return frames;
		}

		var flexDescToFrames = new List<List<FlexFrame>>(capacity: model.FlexDescs.Count);
		for (var i = 0; i < model.FlexDescs.Count; i++)
		{
			flexDescToFrames.Add(new List<FlexFrame>());
		}

		var globalVertexIndexStart = 0;
		for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
		{
			var bodyPart = model.BodyParts[bodyPartIndex];
			for (var modelIndex = 0; modelIndex < bodyPart.Models.Count; modelIndex++)
			{
				var subModel = bodyPart.Models[modelIndex];
				for (var meshIndex = 0; meshIndex < subModel.Meshes.Count; meshIndex++)
				{
					var mesh = subModel.Meshes[meshIndex];
					if (mesh.Flexes.Count == 0)
					{
						continue;
					}

					var bodyAndMeshVertexIndexStart = globalVertexIndexStart + mesh.VertexIndexStart;
					for (var flexIndex = 0; flexIndex < mesh.Flexes.Count; flexIndex++)
					{
					var flex = mesh.Flexes[flexIndex];
					if ((uint)flex.FlexDescIndex >= (uint)flexDescToFrames.Count)
					{
						continue;
					}

						var candidates = flexDescToFrames[flex.FlexDescIndex];
						FlexFrame? frame = null;
						for (var x = 0; x < candidates.Count; x++)
						{
							var candidate = candidates[x];
							if (candidate.Target0 == flex.Target0 &&
								candidate.Target1 == flex.Target1 &&
								candidate.Target2 == flex.Target2 &&
								candidate.Target3 == flex.Target3)
							{
								frame = candidate;
								break;
							}
						}

						if (frame is null)
						{
							var flexName = model.FlexDescs[flex.FlexDescIndex].Name;
							if (string.IsNullOrWhiteSpace(flexName))
							{
								flexName = $"flex_{flex.FlexDescIndex}";
							}

							var hasPartner = flex.FlexDescPartnerIndex > 0 && (uint)flex.FlexDescPartnerIndex < (uint)model.FlexDescs.Count;
							var description = flexName;
							if (hasPartner)
							{
								var partnerName = model.FlexDescs[flex.FlexDescPartnerIndex].Name;
								if (string.IsNullOrWhiteSpace(partnerName))
								{
									partnerName = $"flex_{flex.FlexDescPartnerIndex}";
								}
								description += "+" + partnerName;
							}

							frame = new FlexFrame
							{
								FrameIndex = frames.Count,
								FlexDescIndex = flex.FlexDescIndex,
								Target0 = flex.Target0,
								Target1 = flex.Target1,
								Target2 = flex.Target2,
								Target3 = flex.Target3,
								FlexName = flexName,
								FlexDescription = description,
								FlexHasPartner = hasPartner,
								FlexSplit = 1f
							};

							frames.Add(frame);
							candidates.Add(frame);
						}

						frame.Flexes.Add((flex, bodyAndMeshVertexIndexStart));
					}
				}

				globalVertexIndexStart += Math.Max(0, subModel.VertexCount);
			}
		}

		return frames;
	}
}
