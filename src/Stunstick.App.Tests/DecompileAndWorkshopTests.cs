using System.IO.Compression;
using System.Reflection;
using Stunstick.App.Decompile;
using Stunstick.App.Workshop;
using Stunstick.Core.Mdl;
using Stunstick.Core.Vtx;

namespace Stunstick.App.Tests;

public class DecompileAndWorkshopTests
{
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
		var bodyPart = new MdlBodyPart(Index: 0, Name: "body", Models: new[] { subModel });

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
			false, // hasPhysics
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
		Assert.Contains("replacemodel", qc, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("body", qc, StringComparison.OrdinalIgnoreCase);
	}
}
