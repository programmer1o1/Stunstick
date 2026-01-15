using Stunstick.Core.GoldSrc;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stunstick.App.Decompile;

internal static class GoldSrcMdlDecompileDebugInfoWriter
{
	public static async Task WriteDebugInfoAsync(
		string mdlPath,
		string textureMdlPath,
		string modelOutputFolder,
		GoldSrcMdlFile model,
		DecompileOptions options,
		IReadOnlyList<AccessedBytesDebugFileWriter.DebugFile>? accessedBytesDebugFiles,
		CancellationToken cancellationToken)
	{
		if (!options.WriteDebugInfoFiles)
		{
			return;
		}

		var debugFolder = Path.Combine(modelOutputFolder, "debug");
		Directory.CreateDirectory(debugFolder);

		var info = new GoldSrcDebugInfo(
			GeneratedAt: DateTimeOffset.Now,
			SourceMdlPath: Path.GetFullPath(mdlPath),
			TextureMdlPath: Path.GetFullPath(textureMdlPath),
			OutputFolder: Path.GetFullPath(modelOutputFolder),
			Options: options,
			Header: model.Header,
			BoneCount: model.Bones.Count,
			BodyPartCount: model.BodyParts.Count,
			TextureCount: model.Textures.Count);

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

	private sealed record GoldSrcDebugInfo(
		DateTimeOffset GeneratedAt,
		string SourceMdlPath,
		string TextureMdlPath,
		string OutputFolder,
		DecompileOptions Options,
		GoldSrcMdlHeader Header,
		int BoneCount,
		int BodyPartCount,
		int TextureCount);
}
