using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Text;
using Stunstick.App.Decompile;
using Stunstick.App.Toolchain;
using Stunstick.App.Workshop;
using Stunstick.Core.Mdl;
using Stunstick.Core.Phy;
using Stunstick.Core.Vvd;
using Stunstick.Core.Vtx;

namespace Stunstick.App.Tests;

public class DecompileAndWorkshopTests
{
	[Theory]
	[InlineData(3)]
	[InlineData(4)]
	public async Task Decompile_GoldSrcTextureKeepsPaletteAndPixelIndices(int width)
	{
		var temp = Directory.CreateTempSubdirectory();
		try
		{
			const int height = 2;
			const int texture = 244;
			const int textureData = texture + 80;
			const int bitmapData = 14 + 40 + 256 * 4;
			var pixels = new byte[] { 0, 5, 255, 8, 9, 255, 4, 3 }[..(width * height)];
			var palette = Enumerable.Range(0, 768).Select(i => (byte)i).ToArray();
			var mdlBytes = new byte[textureData + pixels.Length + palette.Length];
			using (var stream = new MemoryStream(mdlBytes))
			using (var writer = new BinaryWriter(stream))
			{
				writer.Write(MdlConstants.Idst);
				writer.Write(10);
				stream.Position = 72;
				writer.Write(mdlBytes.Length);
				stream.Position = 180;
				writer.Write(1);
				writer.Write(texture);
				writer.Write(textureData);
				stream.Position = texture;
				writer.Write("indexed.bmp\0"u8);
				stream.Position = texture + 68;
				writer.Write(width);
				writer.Write(height);
				writer.Write(textureData);
				writer.Write(pixels);
				writer.Write(palette);
			}

			var mdl = Path.Combine(temp.FullName, "palette.mdl");
			await File.WriteAllBytesAsync(mdl, mdlBytes);
			var output = Path.Combine(temp.FullName, "out");
			var app = new StunstickApplication(new ToolchainLauncher(new SystemProcessLauncher()));
			await app.DecompileAsync(new DecompileRequest(mdl, output), CancellationToken.None);
			var bmp = await File.ReadAllBytesAsync(Path.Combine(output, "palette", "indexed.bmp"));
			Assert.Equal(bitmapData + 4 * height, bmp.Length);
			Assert.Equal(bitmapData, BitConverter.ToInt32(bmp, 10));
			Assert.Equal(width, BitConverter.ToInt32(bmp, 18));
			Assert.Equal((ushort)8, BitConverter.ToUInt16(bmp, 28));
			Assert.Equal(256, BitConverter.ToInt32(bmp, 46));
			for (var i = 0; i < 256; i++)
			{
				Assert.Equal(new byte[] { palette[i * 3 + 2], palette[i * 3 + 1], palette[i * 3], 0 }, bmp[(54 + i * 4)..(58 + i * 4)]);
			}
			for (var y = 0; y < height; y++)
			{
				var start = bitmapData + y * 4;
				var source = (height - y - 1) * width;
				Assert.Equal(pixels[source..(source + width)], bmp[start..(start + width)]);
				Assert.All(bmp[(start + width)..(start + 4)], pad => Assert.Equal(0, pad));
			}
		}
		finally
		{
			temp.Delete(recursive: true);
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MdlReader_ReadsTextureAndGeometryStrides(bool wide)
	{
		const int body = 408;
		const int models = body + 16;
		var modelSize = wide ? 160 : 148;
		var meshSize = wide ? 120 : 116;
		var textureSize = wide ? 72 : 64;
		var meshes = models + 2 * modelSize;
		var textures = meshes + 4 * meshSize;
		var texturePaths = textures + 2 * textureSize;
		var strings = texturePaths + sizeof(int);

		using var stream = new MemoryStream(new byte[strings + 16]);
		using var writer = new BinaryWriter(stream);
		writer.Write(MdlConstants.Idst);
		writer.Write(44);
		stream.Position = 76;
		writer.Write((int)stream.Length);
		stream.Position = 204;
		writer.Write(2);
		writer.Write(textures);
		writer.Write(0);
		writer.Write(texturePaths);
		stream.Position = 232;
		writer.Write(1);
		writer.Write(body);
		stream.Position = body + sizeof(int);
		writer.Write(2);
		writer.Write(1);
		writer.Write(models - body);

		for (var i = 0; i < 2; i++)
		{
			var model = models + i * modelSize;
			var firstMesh = meshes + i * 2 * meshSize;
			stream.Position = model;
			writer.Write(i == 0 ? "first\0"u8 : "second\0"u8);
			stream.Position = model + 72;
			writer.Write(2);
			writer.Write(firstMesh - model);
			writer.Write(6);
			for (var j = 0; j < 2; j++)
			{
				var mesh = firstMesh + j * meshSize;
				stream.Position = mesh;
				writer.Write(j);
				writer.Write(model - mesh);
				writer.Write(3);
				writer.Write(j * 3);
				stream.Position = mesh + (wide ? 56 : 52);
				writer.Write(3);
			}

			var texture = textures + i * textureSize;
			var textureName = strings + i * 8;
			stream.Position = texture;
			writer.Write(textureName - texture);
			stream.Position = textureName;
			writer.Write(i == 0 ? "body\0"u8 : "clothes\0"u8);
		}

		var mdl = MdlReader.Read(stream, "geometry.mdl");
		Assert.Equal(new[] { "body", "clothes" }, mdl.Textures.Select(texture => texture.PathFileName));
		var variants = Assert.Single(mdl.BodyParts).Models;
		Assert.Equal(new[] { "first", "second" }, variants.Select(model => model.Name));
		foreach (var variant in variants)
		{
			Assert.Equal(new[] { 0, 1 }, variant.Meshes.Select(mesh => mesh.MaterialIndex));
			Assert.Equal(new[] { 0, 3 }, variant.Meshes.Select(mesh => mesh.VertexIndexStart));
			Assert.All(variant.Meshes, mesh => Assert.Equal(3, mesh.LodVertexCount[0]));
		}
	}

	[Fact]
	public async Task Decompile_ExportsJiggleRulesInQcWithoutVrd()
	{
		var temp = Directory.CreateTempSubdirectory();
		try
		{
			const int boneOffset = 408;
			const int jiggleBytes = 30 * sizeof(int);
			var bytes = CreateAnimationOnlyMdl(blended: false, delta: false);
			var ruleStart = bytes.Length;
			Array.Resize(ref bytes, bytes.Length + jiggleBytes);
			using (var stream = new MemoryStream(bytes))
			using (var writer = new BinaryWriter(stream))
			{
				stream.Position = 76;
				writer.Write(bytes.Length);
				stream.Position = boneOffset + 164;
				writer.Write(5);
				writer.Write(ruleStart - boneOffset);
				stream.Position = ruleStart;
				writer.Write((int)(MdlJiggleFlags.Flexible | MdlJiggleFlags.YawConstraint | MdlJiggleFlags.AngleConstraint));
				writer.Write(4.25f);
				stream.Position = ruleStart + 9 * sizeof(float);
				writer.Write(MathF.PI / 4);
				writer.Write(-MathF.PI / 2);
				writer.Write(MathF.PI / 2);
				writer.Write(0.25f);
				writer.Write(0.5f);
			}
			var mdl = Path.Combine(temp.FullName, "jiggle.mdl");
			await File.WriteAllBytesAsync(mdl, bytes);
			var output = Path.Combine(temp.FullName, "output");
			var app = new StunstickApplication(new ToolchainLauncher(new SystemProcessLauncher()));
			await app.DecompileAsync(new DecompileRequest(mdl, output, Options: new DecompileOptions(WriteProceduralBonesVrdFile: false)), CancellationToken.None);

			var qc = await File.ReadAllTextAsync(Path.Combine(output, "jiggle", "model.qc"));
			Assert.Contains("$JiggleBone \"root\"", qc, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("length 4.25", qc, StringComparison.Ordinal);
			Assert.Contains("allow_length_flex", qc, StringComparison.Ordinal);
			Assert.Contains("yaw_friction 0.25", qc, StringComparison.Ordinal);
			Assert.Contains("yaw_bounce 0.5", qc, StringComparison.Ordinal);
			Assert.Contains("angle_constraint 45", qc, StringComparison.Ordinal);
		}
		finally
		{
			temp.Delete(recursive: true);
		}
	}

	[Fact]
	public void Decompile_VtaPreservesSmallPositionAndNormalDeltas()
	{
		var method = typeof(DecompileOptions).Assembly.GetType("Stunstick.App.Decompile.MdlVertexAnimationVtaWriter")!
			.GetMethod("FormatVertexAnimationLine", BindingFlags.NonPublic | BindingFlags.Static)!;
		var position = new Vector3(108.6171875f, 0, 0);
		var normal = new Vector3(0.9999995f, 0, 0);
		string Format(Vector3 positionDelta, Vector3 normalDelta) =>
			(string)method.Invoke(null, new object[] { 3, position, normal, positionDelta, normalDelta })!;

		Assert.Equal("    3 108.617188 0.000000 0.000000 1.000000 0.000000 0.000000", Format(Vector3.Zero, Vector3.Zero));
		Assert.Equal("    3 108.617191 0.000000 0.000000 0.999999 0.000000 0.000000",
			Format(new Vector3(MathF.ScaleB(1, -18), 0, 0), new Vector3(-MathF.ScaleB(1, -25), 0, 0)));
	}

	[Fact]
	public void Decompile_ReferenceSmdPreservesVertexPrecisionAndInvariantWeights()
	{
		var first = new VvdVertex(new VvdBoneWeight(0.75f, 0.25f, 0, 2, 4, 0, 2),
			new Vector3(108.6171875f, -22.209875f, 48.443237f), Vector3.UnitZ, new Vector2(0.0999995f));
		var second = first with { Position = new Vector3(MathF.BitIncrement(first.Position.X), first.Position.Y, first.Position.Z) };
		var vvd = new VvdFile("precision.vvd", new VvdHeader("IDSV", 4, 0, 1, new[] { 2 }, 0, 0, 0, 0),
			new[] { first, second }, Array.Empty<VvdFixup>(), Array.Empty<IReadOnlyList<VvdVertex>>());
		var method = typeof(DecompileOptions).Assembly.GetType("Stunstick.App.Decompile.MdlDecompiler")!
			.GetMethod("FormatVertexLine", BindingFlags.NonPublic | BindingFlags.Static)!;
		var indexes = new[] { new VtxVertex(0), new VtxVertex(1) };
		string Format(ushort index) =>
			(string)method.Invoke(null, new object[] { 0, vvd, indexes, index, 0, 0, new DecompileOptions() })!;

		var originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
			const string commonFields = " -22.209875 48.443237 0.000000 0.000000 1.000000 0.100000 0.900000 2 2 0.750000 4 0.250000";
			Assert.Equal("  2 108.617188" + commonFields, Format(0));
			Assert.Equal("  2 108.617195" + commonFields, Format(1));
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[Fact]
	public void PhyReader_ReadsAllHullsWithInlineVertexBuffers()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
		const int surfaceOffset = 16 + 4 + 28;
		const int firstLedgeOffset = surfaceOffset + 48;
		const int ledgeBytes = 5 * 16;
		const int treeOffset = firstLedgeOffset + 2 * ledgeBytes;
		const int solidEnd = treeOffset + 3 * 28;
		stream.SetLength(solidEnd);
		writer.Write(16);
		writer.Write(0);
		writer.Write(1);
		writer.Write(0);
		writer.Write(solidEnd - 20);
		writer.Write("VPHY"u8);
		stream.Position = surfaceOffset + 32;
		writer.Write(treeOffset - surfaceOffset);
		stream.Position = surfaceOffset + 44;
		writer.Write("IVPS"u8);
		for (var hull = 0; hull < 2; hull++)
		{
			writer.Write(32); // Vertex data follows the 16-byte ledge and 16-byte face.
			writer.Write(0);
			writer.Write((ledgeBytes / 16 << 8) | 4);
			writer.Write(1);
			writer.Write(0);
			for (var point = 0; point < 3; point++)
			{
				writer.Write(point);
			}
			for (var point = 0; point < 3; point++)
			{
				writer.Write((float)(hull * 3 + point));
				writer.Write(0f);
				writer.Write(0f);
				writer.Write(0f);
			}
		}

		var solid = Assert.Single(PhyReader.Read(stream, "inline.phy").Solids);
		Assert.Equal(Enumerable.Range(0, 6).Select(index => (float)index), solid.Vertices.Select(vertex => vertex.X));
		Assert.Collection(solid.ConvexMeshes,
			mesh => Assert.Equal(new PhyFace(0, 1, 2), Assert.Single(mesh.Faces)),
			mesh => Assert.Equal(new PhyFace(3, 4, 5), Assert.Single(mesh.Faces)));
	}

	[Fact]
	public void PhyReader_PreservesRepeatedSolidAndCollisionPairEntries()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
		writer.Write(16); // Header size.
		writer.Write(0);  // Header ID.
		writer.Write(2);  // Solid count.
		writer.Write(0);  // Checksum.
		writer.Write(0);  // Empty binary payloads leave the text blocks available to read.
		writer.Write(0);
		writer.Write("""
			solid { "index" "0" "name" "root" }
			solid { "index" "1" "name" "child" }
			ragdollconstraint { "child" "1" "xmin" "-30" "xmax" "45" "xfriction" "0.2" }
			collisionrules { "collisionpair" "0,1" "collisionpair" "1,0" }
			editparams { "totalmass" "90" "rootname" "root" }
			"""u8);
		writer.Write((byte)0);

		var collision = PhyReader.Read(stream, "test.phy").Collision!;

		Assert.Equal(90, collision.TotalMass);
		Assert.Equal("root", collision.RootBone);
		Assert.Collection(collision.Solids, root => Assert.Equal("root", root.Name), child => Assert.Equal("child", child.Name));
		Assert.Equal(new PhyJointLimit(1, 'x', -30, 45, 0.2), Assert.Single(collision.JointLimits));
		Assert.Equal(new[] { (0, 1), (1, 0) }, collision.CollisionPairs);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public async Task Decompile_AnimationOnlyModelExportsSequencesWithoutVtx(bool blended, bool delta)
	{
		var temp = Directory.CreateTempSubdirectory();
		try
		{
			var mdlPath = Path.Combine(temp.FullName, "animation_only.mdl");
			await File.WriteAllBytesAsync(mdlPath, CreateAnimationOnlyMdl(blended, delta));
			var output = Path.Combine(temp.FullName, "output");
			var app = new StunstickApplication(new ToolchainLauncher(new SystemProcessLauncher()));

			await app.DecompileAsync(new DecompileRequest(mdlPath, output), CancellationToken.None);

			var modelOutput = Path.Combine(output, "animation_only");
			var qc = await File.ReadAllTextAsync(Path.Combine(modelOutput, "model.qc"));
			Assert.DoesNotContain("$BodyGroup", qc, StringComparison.OrdinalIgnoreCase);
			var animationPaths = Directory.GetFiles(modelOutput, "*_anim_*.smd", SearchOption.AllDirectories)
				.Where(path => !path.EndsWith("_corrective_animation.smd", StringComparison.Ordinal)).ToArray();
			Assert.Equal(blended ? 2 : 1, animationPaths.Length);
			if (blended)
			{
				Assert.Contains("$Sequence \"walk\"", qc, StringComparison.OrdinalIgnoreCase);
				Assert.Contains("blendwidth 2", qc, StringComparison.Ordinal);
				Assert.Contains("blend \"move_yaw\" -30 30", qc, StringComparison.Ordinal);
				Assert.Contains("fps 24", qc, StringComparison.Ordinal);
				Assert.Contains("fps 18", qc, StringComparison.Ordinal);
				Assert.Contains("walkframe 1 Z LX", qc, StringComparison.Ordinal);
			}
			else if (delta)
			{
				var corrective = Assert.Single(Directory.GetFiles(modelOutput, "*_corrective_animation.smd", SearchOption.AllDirectories));
				var name = Path.GetFileNameWithoutExtension(corrective);
				Assert.Contains($"subtract \"{name}\" 0", qc, StringComparison.Ordinal);
				Assert.Matches("(?im)^\\$sequence \"walk\" .* loop delta$", qc.ReplaceLineEndings("\n"));
				Assert.Contains("0 0 0 0 0 0 0", await File.ReadAllTextAsync(corrective), StringComparison.Ordinal);
			}
			else
			{
				var relativePath = Path.GetRelativePath(modelOutput, animationPaths[0]).Replace('\\', '/');
				Assert.Contains($"$Sequence \"walk\" \"{relativePath}\" fps 24 loop", qc, StringComparison.OrdinalIgnoreCase);
			}

			foreach (var animationPath in animationPaths)
			{
				var animation = await File.ReadAllTextAsync(animationPath);
				Assert.Contains("0 3 -2 4 0 0 0", animation, StringComparison.Ordinal);
			}
		}
		finally
		{
			temp.Delete(recursive: true);
		}
	}

	[Fact]
	public async Task Decompile_UsesDistinctAnimationEntriesForDifferentSequenceWeights()
	{
		var temp = Directory.CreateTempSubdirectory();
		try
		{
			var mdlPath = Path.Combine(temp.FullName, "weighted.mdl");
			await File.WriteAllBytesAsync(mdlPath, CreateAnimationOnlyMdl(blended: true, delta: false, includeWeights: true));
			var output = Path.Combine(temp.FullName, "output");
			var app = new StunstickApplication(new ToolchainLauncher(new SystemProcessLauncher()));
			await app.DecompileAsync(new DecompileRequest(mdlPath, output), CancellationToken.None);

			var qc = (await File.ReadAllTextAsync(Path.Combine(output, "weighted", "model.qc"))).ReplaceLineEndings("\n");
			Assert.Single(qc.Split('\n'), line => line.StartsWith("$WeightList ", StringComparison.OrdinalIgnoreCase));
			Assert.Contains("$WeightList \"weights_walk\" {\n    \"root\" 0.375\n}", qc, StringComparison.OrdinalIgnoreCase);
			Assert.Equal(2, qc.Split('\n').Count(line => line.Trim() == "weightlist \"weights_walk\""));
			Assert.Contains("$Sequence \"walk\" {\n    \"@stunstick_anim_0\"\n    \"@stunstick_anim_1\"", qc, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("$Sequence \"walk_copy\" {\n    \"@stunstick_anim_0\"\n    \"@stunstick_anim_1\"", qc, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("$Sequence \"walk_default\" {\n    \"@stunstick_anim_0_\"\n    \"@stunstick_anim_1_\"", qc, StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			temp.Delete(recursive: true);
		}
	}

	private static byte[] CreateAnimationOnlyMdl(bool blended, bool delta, bool includeWeights = false)
	{
		const int boneOffset = 408;
		const int boneNameOffset = boneOffset + 216;
		const int animationOffset = boneNameOffset + 8;
		const int sequenceDescSize = 212;
		const int sequenceNameBytes = 16;
		var animationCount = blended ? 2 : 1;
		var sequenceNames = includeWeights ? new[] { "walk", "walk_copy", "walk_default" } : new[] { "walk" };
		var sequenceOffset = animationOffset + animationCount * 100;
		var sequenceNamesOffset = sequenceOffset + sequenceNames.Length * sequenceDescSize;
		var sequenceAnimationIndexOffset = sequenceNamesOffset + sequenceNames.Length * sequenceNameBytes;
		var poseParameterOffset = sequenceAnimationIndexOffset + animationCount * sizeof(short);
		var poseParameterNameOffset = poseParameterOffset + 20;
		var movementOffset = poseParameterNameOffset + 12;
		var weightsOffset = movementOffset + 44;
		var bytes = new byte[weightsOffset + sequenceNames.Length * sizeof(float)];
		using var stream = new MemoryStream(bytes);
		using var writer = new BinaryWriter(stream, Encoding.ASCII);

		writer.Write(MdlConstants.Idst);
		writer.Write(44);
		stream.Position = 76;
		writer.Write(bytes.Length);
		stream.Position = 156; // Bone count and offset.
		writer.Write(1);
		writer.Write(boneOffset);
		stream.Position = 180; // Local animation and sequence count and offsets.
		writer.Write(animationCount);
		writer.Write(animationOffset);
		writer.Write(sequenceNames.Length);
		writer.Write(sequenceOffset);
		if (blended)
		{
			stream.Position = 300; // Local pose parameter count and offset.
			writer.Write(1);
			writer.Write(poseParameterOffset);
		}

		stream.Position = boneOffset;
		writer.Write(boneNameOffset - boneOffset);
		writer.Write(-1); // Root parent index.
		stream.Position = boneOffset + 32;
		writer.Write(2f);
		writer.Write(3f);
		writer.Write(4f);
		stream.Position = boneOffset + 68;
		writer.Write(MathF.PI / 2);
		stream.Position = boneNameOffset;
		writer.Write("root\0"u8);

		stream.Position = animationOffset + 8;
		writer.Write(24f);
		writer.Write(0); // Animation flags.
		writer.Write(1); // Single frame uses the stored bone pose.
		if (blended)
		{
			var secondAnimationOffset = animationOffset + 100;
			stream.Position = secondAnimationOffset + 8;
			writer.Write(18f);
			writer.Write(0);
			writer.Write(2); // Frame count.
			writer.Write(1); // Movement count.
			writer.Write(movementOffset - secondAnimationOffset);
			stream.Position = movementOffset;
			writer.Write(1); // Movement ends on the last frame.
			writer.Write((1 << 2) | (1 << 6)); // Z and LX.
		}

		var sequenceFlags = MdlConstants.StudioSequenceLooping;
		if (delta)
		{
			sequenceFlags |= MdlConstants.StudioSequenceDelta | MdlConstants.StudioSequencePost;
		}
		for (var i = 0; i < sequenceNames.Length; i++)
		{
			var seqStart = sequenceOffset + i * sequenceDescSize;
			var nameOffset = sequenceNamesOffset + i * sequenceNameBytes;
			stream.Position = seqStart + 4;
			writer.Write(nameOffset - seqStart);
			stream.Position = seqStart + 12;
			writer.Write(sequenceFlags);
			stream.Position = seqStart + 56;
			writer.Write(animationCount); // Blend count.
			writer.Write(sequenceAnimationIndexOffset - seqStart);
			writer.Write(0); // Movement offset.
			writer.Write(animationCount);
			writer.Write(1);
			if (blended)
			{
				writer.Write(0); // First blend axis uses the model's first pose parameter.
				writer.Write(-1);
				writer.Write(-30f);
				writer.Write(0f);
				writer.Write(30f);
				writer.Write(0f);
			}
			if (includeWeights)
			{
				var weightOffset = weightsOffset + i * sizeof(float);
				stream.Position = seqStart + 156;
				writer.Write(weightOffset - seqStart);
				stream.Position = weightOffset;
				writer.Write(i < 2 ? 0.375f : 1f);
			}
			stream.Position = nameOffset;
			writer.Write(Encoding.ASCII.GetBytes(sequenceNames[i]));
			writer.Write((byte)0);
		}
		if (blended)
		{
			stream.Position = sequenceAnimationIndexOffset + sizeof(short);
			writer.Write((short)1);
			stream.Position = poseParameterOffset;
			writer.Write(poseParameterNameOffset - poseParameterOffset);
			stream.Position = poseParameterNameOffset;
			writer.Write("move_yaw\0"u8);
		}
		return bytes;
	}

	[Fact]
	public async Task WorkshopDownloader_ExtractsZipWhenConvertEnabled()
	{
		var temp = Directory.CreateTempSubdirectory();
		var zipPath = Path.Combine(temp.FullName, "item.zip");
		var extractedFolder = Path.Combine(temp.FullName, "item");

		// Arrange: create a small zip payload.
		using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
		{
			var entry = archive.CreateEntry("hello.txt");
			await using var writer = new StreamWriter(entry.Open());
			await writer.WriteAsync("hi");
		}

		var request = new WorkshopDownloadRequest(
			IdOrLink: "123",
			OutputDirectory: temp.FullName,
			AppId: 0,
			ConvertToExpectedFileOrFolder: true,
			OverwriteExisting: true);

		var method = typeof(WorkshopDownloadRequest).Assembly
			.GetType("Stunstick.App.Workshop.WorkshopDownloader")!
			.GetMethod("MaybeConvertDownloadedFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

		// Act
		var task = (Task<string>)method.Invoke(null, new object[] { request, 0u, zipPath, CancellationToken.None })!;
		var resultPath = await task;

		// Assert
		Assert.Equal(extractedFolder, resultPath);
		Assert.True(Directory.Exists(extractedFolder));
		var extractedFile = Path.Combine(extractedFolder, "hello.txt");
		Assert.True(File.Exists(extractedFile));
		Assert.Equal("hi", await File.ReadAllTextAsync(extractedFile));
	}

	[Fact]
	public async Task Decompile_QcIncludesShadowLodWhenFlagAndLodPresent()
	{
		var temp = Directory.CreateTempSubdirectory();
		var modelOutput = temp.FullName;

		var options = new DecompileOptions();

		var header = new MdlHeader(
			Id: 0,
			Version: 0,
			Checksum: 0,
			Name: "test",
			Length: 0,
			Flags: MdlConstants.StudioHdrFlagsHasShadowLod,
			BoneCount: 0,
			BoneOffset: 0,
			LocalAnimationCount: 0,
			LocalAnimationOffset: 0,
			LocalSequenceCount: 0,
			LocalSequenceOffset: 0,
			TextureCount: 0,
			TextureOffset: 0,
			TexturePathCount: 0,
			TexturePathOffset: 0,
			SkinReferenceCount: 0,
			SkinFamilyCount: 0,
			SkinFamilyOffset: 0,
			BodyPartCount: 1,
			BodyPartOffset: 0,
			LocalAttachmentCount: 0,
			LocalAttachmentOffset: 0,
			FlexDescCount: 0,
			FlexDescOffset: 0,
			FlexControllerCount: 0,
			FlexControllerOffset: 0,
			FlexRuleCount: 0,
			FlexRuleOffset: 0,
			AnimBlockNameOffset: 0,
			AnimBlockCount: 0,
			AnimBlockOffset: 0);

		var mesh = new MdlMesh(
			Index: 0,
			MaterialIndex: 0,
			VertexCount: 0,
			VertexIndexStart: 0,
			Flexes: Array.Empty<MdlFlex>(),
			LodVertexCount: new int[8]);
		var subModel = new MdlSubModel(Index: 0, Name: "model", VertexCount: 0, Meshes: new[] { mesh });
		var blank = new MdlSubModel(Index: 1, Name: "", VertexCount: 0, Meshes: Array.Empty<MdlMesh>());
		var bodyPart = new MdlBodyPart(Index: 0, Name: "body", Models: new[] { subModel, blank });

		var model = new MdlModel(
			SourcePath: "dummy.mdl",
			Header: header,
			Bones: Array.Empty<MdlBone>(),
			TexturePaths: Array.Empty<string>(),
			Textures: Array.Empty<MdlTexture>(),
			SkinFamilies: Array.Empty<MdlSkinFamily>(),
			BodyParts: new[] { bodyPart },
			FlexDescs: Array.Empty<MdlFlexDesc>(),
			FlexControllers: Array.Empty<MdlFlexController>(),
			FlexRules: Array.Empty<MdlFlexRule>(),
			AnimationDescs: Array.Empty<MdlAnimationDesc>(),
			SequenceDescs: Array.Empty<MdlSequenceDesc>());

		var vtx = new VtxFile(
			SourcePath: "dummy.vtx",
			Header: new VtxHeader(Version: 7, Checksum: 0, LodCount: 2, MaterialReplacementListOffset: 0, BodyPartCount: 1, BodyPartOffset: 0),
			UsesExtraStripGroupFields: false,
			BodyParts: new[]
			{
				new VtxBodyPart(
					ModelCount: 1,
					Models: new[]
					{
						new VtxModel(
							LodCount: 2,
							Lods: new[]
							{
								new VtxModelLod(MeshCount: 0, SwitchPoint: 0, Meshes: Array.Empty<VtxMesh>(), UsesFacial: false),
								new VtxModelLod(MeshCount: 0, SwitchPoint: 10, Meshes: Array.Empty<VtxMesh>(), UsesFacial: false)
							})
					})
			},
			MaterialReplacementLists: Array.Empty<VtxMaterialReplacementList>());

		var method = typeof(DecompileOptions).Assembly
			.GetType("Stunstick.App.Decompile.MdlDecompiler")!
			.GetMethod("WriteQcAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

		await ((Task)method.Invoke(null, new object?[]
		{
			modelOutput,
			"body",
			model,
			vtx,
			null, // physics
			null,  // proceduralBonesVrdFileName
			options,
			null,  // animationSmdRelativePathFileNames
			null,  // vtaResult
			CancellationToken.None
		})!);

		var qcPath = Path.Combine(modelOutput, "model.qc");
		Assert.True(File.Exists(qcPath));
		var qc = await File.ReadAllTextAsync(qcPath);

		Assert.Contains("$shadowlod", qc, StringComparison.OrdinalIgnoreCase);
		Assert.Single(qc.Split('\n'), line => line.Trim().Equals("$shadowlod", StringComparison.OrdinalIgnoreCase));
		Assert.Contains("replacemodel", qc, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("body", qc, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("    blank", qc, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("ref_bodypart0_model1_lod0.smd", qc, StringComparison.OrdinalIgnoreCase);
	}
}
