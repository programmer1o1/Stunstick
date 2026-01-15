using Stunstick.Core.GoldSrc;
using Stunstick.Core.IO;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stunstick.App.Decompile;

internal static class GoldSrcMdlDecompiler
{
	public static async Task DecompileAsync(
		string mdlPath,
		string textureMdlPath,
		string modelOutputFolder,
		string originalFolder,
		IReadOnlyList<string> copiedOriginalFiles,
		GoldSrcMdlFile model,
		DecompileOptions options,
		AccessedBytesLog? textureMdlAccessLog,
		IReadOnlyList<AccessedBytesDebugFileWriter.DebugFile>? accessedBytesDebugFiles,
		CancellationToken cancellationToken)
	{
		options ??= new DecompileOptions();

		var bones = model.Bones;
		var boneTransforms = BuildBoneTransforms(bones);

		var skeletonPathFileName = Path.Combine(modelOutputFolder, "skeleton.smd");
		await WriteSkeletonSmdAsync(skeletonPathFileName, bones, options, cancellationToken);

		if (options.WriteReferenceMeshSmdFiles)
		{
			for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
			{
				var bodyPart = model.BodyParts[bodyPartIndex];
				for (var modelIndex = 0; modelIndex < bodyPart.Models.Count; modelIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var bodyModel = bodyPart.Models[modelIndex];
					var smdFileName = $"ref_bodypart{bodyPartIndex}_model{modelIndex}_lod0.smd";
					var smdPathFileName = Path.Combine(modelOutputFolder, smdFileName);

					await WriteReferenceSmdAsync(
						smdPathFileName,
						model,
						bodyModel,
						bones,
						boneTransforms,
						options,
						cancellationToken);
				}
			}
		}

		if (options.WriteQcFile)
		{
			await WriteQcAsync(modelOutputFolder, mdlPath, model, options, cancellationToken);
		}

		if (options.WriteTextureBmpFiles)
		{
			await WriteTextureBmpFilesAsync(textureMdlPath, modelOutputFolder, model.Textures, textureMdlAccessLog, cancellationToken);
		}

		var manifest = new GoldSrcMdlDecompileManifest(
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
				await GoldSrcMdlDecompileDebugInfoWriter.WriteDebugInfoAsync(
					mdlPath,
					textureMdlPath,
					modelOutputFolder,
					model,
					options,
					accessedBytesDebugFiles,
					cancellationToken);
			}
			catch
			{
				// Best-effort.
			}
		}
	}

	private static async Task WriteTextureBmpFilesAsync(
		string textureMdlPath,
		string modelOutputFolder,
		IReadOnlyList<GoldSrcMdlTexture> textures,
		AccessedBytesLog? textureMdlAccessLog,
		CancellationToken cancellationToken)
	{
		if (textures.Count == 0)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(textureMdlPath) || !File.Exists(textureMdlPath))
		{
			return;
		}

			using var fileStream = new FileStream(textureMdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			Stream stream = fileStream;
			if (textureMdlAccessLog is not null)
			{
				stream = new AccessLoggedStream(fileStream, textureMdlAccessLog);
			}

			using var reader = new BinaryReader(stream);

		for (var textureIndex = 0; textureIndex < textures.Count; textureIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var texture = textures[textureIndex];
			if (texture.Width == 0 || texture.Height == 0)
			{
				continue;
			}

			if (texture.Width > int.MaxValue || texture.Height > int.MaxValue)
			{
				continue;
			}

			var pixelCount = (long)texture.Width * texture.Height;
			if (pixelCount <= 0 || pixelCount > int.MaxValue)
			{
				continue;
			}

			const int paletteBytes = 256 * 3;
			var dataOffset = (long)texture.DataOffset;
			var requiredBytes = pixelCount + paletteBytes;
			if (dataOffset <= 0 || dataOffset + requiredBytes > stream.Length)
			{
				continue;
			}

			stream.Seek(dataOffset, SeekOrigin.Begin);
			var indices = reader.ReadBytes((int)pixelCount);
			if (indices.Length != (int)pixelCount)
			{
				continue;
			}

			var palette = reader.ReadBytes(paletteBytes);
			if (palette.Length != paletteBytes)
			{
				continue;
			}

			var relativePath = GetTextureRelativePath(texture.FileName, texture.Index);
			var bmpPathFileName = Path.Combine(modelOutputFolder, relativePath);

			var bmpFolder = Path.GetDirectoryName(bmpPathFileName);
			if (!string.IsNullOrWhiteSpace(bmpFolder))
			{
				Directory.CreateDirectory(bmpFolder);
			}

			await WriteBmp24Async(bmpPathFileName, (int)texture.Width, (int)texture.Height, indices, palette, cancellationToken);
		}
	}

	private static string GetTextureRelativePath(string? fileName, int textureIndex)
	{
		var name = string.IsNullOrWhiteSpace(fileName) ? $"texture_{textureIndex}.bmp" : fileName.Trim();

		var segments = name
			.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(SanitizeFileNameSegment)
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.ToArray();

		var relative = segments.Length == 0 ? $"texture_{textureIndex}.bmp" : Path.Combine(segments);
		if (string.IsNullOrWhiteSpace(Path.GetExtension(relative)))
		{
			relative += ".bmp";
		}

		return relative;
	}

	private static string SanitizeFileNameSegment(string segment)
	{
		if (string.IsNullOrEmpty(segment))
		{
			return segment;
		}

		var invalidChars = Path.GetInvalidFileNameChars();
		return string.Concat(segment.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
	}

	private static async Task WriteBmp24Async(
		string pathFileName,
		int width,
		int height,
		byte[] indices,
		byte[] paletteRgb,
		CancellationToken cancellationToken)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions must be positive.");
		}

		var rowBytes = checked(width * 3);
		var paddedRowBytes = (rowBytes + 3) & ~3;
		var imageBytes = checked(paddedRowBytes * height);
		var fileSize = 14 + 40 + imageBytes;

		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		using var writer = new BinaryWriter(stream);

		// BITMAPFILEHEADER
		writer.Write((ushort)0x4D42); // 'BM'
		writer.Write(fileSize);
		writer.Write((ushort)0);
		writer.Write((ushort)0);
		writer.Write(14 + 40);

		// BITMAPINFOHEADER
		writer.Write(40);
		writer.Write(width);
		writer.Write(height);
		writer.Write((ushort)1);
		writer.Write((ushort)24);
		writer.Write(0);
		writer.Write(imageBytes);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);
		writer.Write(0);

		var paddingBytes = paddedRowBytes - rowBytes;
		var padding = paddingBytes > 0 ? new byte[paddingBytes] : Array.Empty<byte>();

		for (var y = height - 1; y >= 0; y--)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var rowStart = y * width;
			for (var x = 0; x < width; x++)
			{
				var idx = indices[rowStart + x];
				var paletteIndex = idx * 3;
				writer.Write(paletteRgb[paletteIndex + 2]); // B
				writer.Write(paletteRgb[paletteIndex + 1]); // G
				writer.Write(paletteRgb[paletteIndex]); // R
			}

			if (paddingBytes > 0)
			{
				writer.Write(padding);
			}
		}

		await stream.FlushAsync(cancellationToken);
	}

	private static async Task WriteQcAsync(
		string modelOutputFolder,
		string mdlPath,
		GoldSrcMdlFile model,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		var modelName = Path.GetFileNameWithoutExtension(mdlPath);
		var qcPathFileName = Path.Combine(modelOutputFolder, "model.qc");

		await using var stream = new FileStream(qcPathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync($"$modelname \"{modelName}.mdl\"");
		await writer.WriteLineAsync("$cd \".\"");
		await writer.WriteLineAsync("$cdtexture \".\"");

		for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bodyPart = model.BodyParts[bodyPartIndex];
			var bodyGroupName = string.IsNullOrWhiteSpace(bodyPart.Name) ? $"bodypart{bodyPartIndex}" : bodyPart.Name;
			bodyGroupName = bodyGroupName.Replace("\"", "'");

			await writer.WriteLineAsync();
			await writer.WriteLineAsync($"$bodygroup \"{bodyGroupName}\"");
			await writer.WriteLineAsync("{");

			for (var modelIndex = 0; modelIndex < bodyPart.Models.Count; modelIndex++)
			{
				var smdFileName = $"ref_bodypart{bodyPartIndex}_model{modelIndex}_lod0.smd";
				await writer.WriteLineAsync($"    studio \"{smdFileName}\"");
			}

			await writer.WriteLineAsync("}");
		}

		await writer.FlushAsync();
	}

	private static async Task WriteSkeletonSmdAsync(string pathFileName, IReadOnlyList<GoldSrcMdlBone> bones, DecompileOptions options, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync("version 1");
		await writer.WriteLineAsync("nodes");
		for (var i = 0; i < bones.Count; i++)
		{
			var bone = bones[i];
			var name = bone.Name.Replace("\"", "'");
			await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0");

		var format = CultureInfo.InvariantCulture;
		for (var i = 0; i < bones.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = bones[i];
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
		await writer.WriteLineAsync("end");

		await writer.FlushAsync();
	}

	private static async Task WriteReferenceSmdAsync(
		string pathFileName,
		GoldSrcMdlFile mdl,
		GoldSrcMdlModel bodyModel,
		IReadOnlyList<GoldSrcMdlBone> bones,
		IReadOnlyList<Matrix3x4> boneTransforms,
		DecompileOptions options,
		CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(pathFileName, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 64, useAsync: true);
		await using var writer = new StreamWriter(stream);

		var format = CultureInfo.InvariantCulture;
		var timePrefix = DecompileFormat.GetTimePrefix(options);

		await DecompileFormat.WriteHeaderCommentAsync(writer, options);

		await writer.WriteLineAsync("version 1");
		await writer.WriteLineAsync("nodes");
		for (var i = 0; i < bones.Count; i++)
		{
			var bone = bones[i];
			var name = bone.Name.Replace("\"", "'");
			await writer.WriteLineAsync($"{bone.Index} \"{name}\" {bone.ParentIndex}");
		}
		await writer.WriteLineAsync("end");

		await writer.WriteLineAsync("skeleton");
		await writer.WriteLineAsync($"{timePrefix}0");
		for (var i = 0; i < bones.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var bone = bones[i];
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

		for (var meshIndex = 0; meshIndex < bodyModel.Meshes.Count; meshIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var mesh = bodyModel.Meshes[meshIndex];
			var materialName = GetMaterialName(mdl.Textures, mesh.SkinRef);
			if (options.RemovePathFromSmdMaterialFileNames)
			{
				materialName = Path.GetFileName(materialName);
			}

			for (var groupIndex = 0; groupIndex < mesh.StripsAndFans.Count; groupIndex++)
			{
				var group = mesh.StripsAndFans[groupIndex];
				if (group.Vertexes.Count < 3)
				{
					continue;
				}

				if (group.IsTriangleStrip)
				{
					for (var i = 0; i + 2 < group.Vertexes.Count; i++)
					{
						var v0 = group.Vertexes[i];
						var v1 = group.Vertexes[i + (i % 2 == 0 ? 2 : 1)];
						var v2 = group.Vertexes[i + (i % 2 == 0 ? 1 : 2)];

						var vertex1Line = FormatVertexLine(bodyModel, v0, mdl.Textures, mesh.SkinRef, boneTransforms, options);
						var vertex2Line = FormatVertexLine(bodyModel, v1, mdl.Textures, mesh.SkinRef, boneTransforms, options);
						var vertex3Line = FormatVertexLine(bodyModel, v2, mdl.Textures, mesh.SkinRef, boneTransforms, options);

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
				else
				{
					for (var i = 1; i + 1 < group.Vertexes.Count; i++)
					{
						var v0 = group.Vertexes[0];
						var v1 = group.Vertexes[i + 1];
						var v2 = group.Vertexes[i];

						var vertex1Line = FormatVertexLine(bodyModel, v0, mdl.Textures, mesh.SkinRef, boneTransforms, options);
						var vertex2Line = FormatVertexLine(bodyModel, v1, mdl.Textures, mesh.SkinRef, boneTransforms, options);
						var vertex3Line = FormatVertexLine(bodyModel, v2, mdl.Textures, mesh.SkinRef, boneTransforms, options);

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

	private static string GetMaterialName(IReadOnlyList<GoldSrcMdlTexture> textures, int materialIndex)
	{
		if ((uint)materialIndex < (uint)textures.Count)
		{
			var name = textures[materialIndex].FileName ?? string.Empty;
			return string.IsNullOrWhiteSpace(name) ? $"material_{materialIndex}" : name;
		}

		return $"material_{materialIndex}";
	}

	private static string FormatVertexLine(
		GoldSrcMdlModel model,
		GoldSrcMdlVertexInfo vertexInfo,
		IReadOnlyList<GoldSrcMdlTexture> textures,
		int skinRef,
		IReadOnlyList<Matrix3x4> boneTransforms,
		DecompileOptions options)
	{
		try
		{
			if ((uint)vertexInfo.VertexIndex >= (uint)model.Vertexes.Count)
			{
				return "// invalid vertex index";
			}
			if ((uint)vertexInfo.NormalIndex >= (uint)model.Normals.Count)
			{
				return "// invalid normal index";
			}

			var vertexBoneIndex = (uint)vertexInfo.VertexIndex < (uint)model.VertexBoneInfos.Count
				? model.VertexBoneInfos[vertexInfo.VertexIndex]
				: (byte)0;

			var normalBoneIndex = (uint)vertexInfo.NormalIndex < (uint)model.NormalBoneInfos.Count
				? model.NormalBoneInfos[vertexInfo.NormalIndex]
				: vertexBoneIndex;

			if ((uint)vertexBoneIndex >= (uint)boneTransforms.Count)
			{
				vertexBoneIndex = 0;
			}
			if ((uint)normalBoneIndex >= (uint)boneTransforms.Count)
			{
				normalBoneIndex = vertexBoneIndex;
			}

			var position = VectorTransform(model.Vertexes[vertexInfo.VertexIndex], boneTransforms[vertexBoneIndex]);
			var normal = VectorRotate(model.Normals[vertexInfo.NormalIndex], boneTransforms[normalBoneIndex]);
			if (normal.LengthSquared() > 1e-12f)
			{
				normal = Vector3.Normalize(normal);
			}

			var texture = (uint)skinRef < (uint)textures.Count ? textures[skinRef] : default;
			var u = 0d;
			var v = 0d;
			if (texture is not null)
			{
				(u, v) = ConvertTextureCoords(vertexInfo, texture, options);
			}

			return string.Format(
				CultureInfo.InvariantCulture,
				"  {0} {1:0.000000} {2:0.000000} {3:0.000000} {4:0.000000} {5:0.000000} {6:0.000000} {7:0.000000} {8:0.000000}",
				vertexBoneIndex,
				position.X, position.Y, position.Z,
				normal.X, normal.Y, normal.Z,
				u, v);
		}
		catch
		{
			return "// invalid vertex";
		}
	}

	private static (double U, double V) ConvertTextureCoords(GoldSrcMdlVertexInfo vertexInfo, GoldSrcMdlTexture texture, DecompileOptions options)
	{
		var s = (double)vertexInfo.S;
		var t = (double)vertexInfo.T;

		if (texture.Width == 1 || texture.Height == 1)
		{
			var fileName = texture.FileName ?? string.Empty;
			if (fileName.StartsWith("#", StringComparison.Ordinal) && fileName.Length >= 7 &&
				uint.TryParse(fileName.Substring(1, 3), out var width) &&
				uint.TryParse(fileName.Substring(4, 3), out var height) &&
				width > 0 && height > 0)
			{
				var u = s / width;
				var v = t / height;
				return (u, 1 - v);
			}

			return (s, 1 - t);
		}

		double u2;
		double v2;
		if (options.UseNonValveUvConversion)
		{
			u2 = s / texture.Width;
			v2 = t / texture.Height;
		}
		else
		{
			u2 = s / (texture.Width - 1);
			v2 = t / (texture.Height - 1);
		}

		return (u2, 1 - v2);
	}

	private static IReadOnlyList<Matrix3x4> BuildBoneTransforms(IReadOnlyList<GoldSrcMdlBone> bones)
	{
		var transforms = new Matrix3x4[bones.Count];

		for (var boneIndex = 0; boneIndex < bones.Count; boneIndex++)
		{
			var bone = bones[boneIndex];
			var r = bone.RotationRadians;

			// Match Crowbar's GoldSrc decompile: AngleMatrix(pitch=y, yaw=z, roll=x).
			var local = AngleMatrix(pitchRadians: r.Y, yawRadians: r.Z, rollRadians: r.X, translation: bone.Position);

			if (bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count)
			{
				transforms[boneIndex] = local;
			}
			else
			{
				transforms[boneIndex] = ConcatTransforms(transforms[bone.ParentIndex], local);
			}
		}

		return transforms;
	}

	private readonly record struct Matrix3x4(
		Vector3 Column0,
		Vector3 Column1,
		Vector3 Column2,
		Vector3 Column3
	);

	private static Matrix3x4 AngleMatrix(float pitchRadians, float yawRadians, float rollRadians, Vector3 translation)
	{
		var sy = MathF.Sin(yawRadians);
		var cy = MathF.Cos(yawRadians);
		var sp = MathF.Sin(pitchRadians);
		var cp = MathF.Cos(pitchRadians);
		var sr = MathF.Sin(rollRadians);
		var cr = MathF.Cos(rollRadians);

		var c0 = new Vector3(cp * cy, cp * sy, -sp);
		var c1 = new Vector3(sr * sp * cy + cr * -sy, sr * sp * sy + cr * cy, sr * cp);
		var c2 = new Vector3(cr * sp * cy + -sr * -sy, cr * sp * sy + -sr * cy, cr * cp);
		var c3 = translation;

		return new Matrix3x4(c0, c1, c2, c3);
	}

	private static Matrix3x4 ConcatTransforms(in Matrix3x4 parent, in Matrix3x4 local)
	{
		var c0 = VectorRotate(local.Column0, parent);
		var c1 = VectorRotate(local.Column1, parent);
		var c2 = VectorRotate(local.Column2, parent);
		var c3 = VectorTransform(local.Column3, parent);
		return new Matrix3x4(c0, c1, c2, c3);
	}

	private static Vector3 VectorRotate(in Vector3 input, in Matrix3x4 m)
	{
		var row0 = new Vector3(m.Column0.X, m.Column1.X, m.Column2.X);
		var row1 = new Vector3(m.Column0.Y, m.Column1.Y, m.Column2.Y);
		var row2 = new Vector3(m.Column0.Z, m.Column1.Z, m.Column2.Z);
		return new Vector3(
			Vector3.Dot(input, row0),
			Vector3.Dot(input, row1),
			Vector3.Dot(input, row2));
	}

	private static Vector3 VectorTransform(in Vector3 input, in Matrix3x4 m)
	{
		var row0 = new Vector3(m.Column0.X, m.Column1.X, m.Column2.X);
		var row1 = new Vector3(m.Column0.Y, m.Column1.Y, m.Column2.Y);
		var row2 = new Vector3(m.Column0.Z, m.Column1.Z, m.Column2.Z);
		return new Vector3(
			Vector3.Dot(input, row0) + m.Column3.X,
			Vector3.Dot(input, row1) + m.Column3.Y,
			Vector3.Dot(input, row2) + m.Column3.Z);
	}
}
