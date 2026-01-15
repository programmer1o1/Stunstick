using Stunstick.Core.IO;
using Stunstick.Core.Mdl;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;
using System.Numerics;

namespace Stunstick.App.Viewer;

public static class MdlModelGeometryLoader
{
	private const uint IdstId = 0x54534449; // "IDST"
	private const uint MdlzId = 0x5A4C444D; // "MDLZ"

	public static ModelGeometry Load(string mdlPath, int lodIndex = 0, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(mdlPath));
		}

		if (!File.Exists(mdlPath))
		{
			throw new FileNotFoundException("MDL file not found.", mdlPath);
		}

		var (id, version) = ReadMdlSignature(mdlPath);
		if (id == MdlzId && version == 14)
		{
			throw new NotSupportedException("MDLZ (compressed GoldSrc) is not supported for preview yet.");
		}

		if (id != IdstId)
		{
			throw new NotSupportedException("Unsupported MDL (expected IDST signature).");
		}

		if (version == 10)
		{
			throw new NotSupportedException("GoldSrc MDL preview is not supported yet.");
		}

		var model = MdlReader.Read(mdlPath);

		var vvd = LoadVvd(model, mdlPath);
		var vtx = LoadVtx(model, mdlPath);

		return BuildMeshGeometry(model, vvd, vtx, lodIndex, cancellationToken);
	}

	internal static ModelGeometry BuildMeshGeometry(MdlModel model, VvdFile vvd, VtxFile vtx, int lodIndex, CancellationToken cancellationToken)
	{
		var triangles = new List<ModelTriangle>(capacity: Math.Min(1024 * 16, vtx.BodyParts.Count * 1024));

		var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
		var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

		var globalVertexIndexStart = 0;

		for (var bodyPartIndex = 0; bodyPartIndex < model.BodyParts.Count; bodyPartIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var mdlBodyPart = model.BodyParts[bodyPartIndex];
			var vtxBodyPart = bodyPartIndex < vtx.BodyParts.Count ? vtx.BodyParts[bodyPartIndex] : null;

			for (var modelIndex = 0; modelIndex < mdlBodyPart.Models.Count; modelIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var mdlSubModel = mdlBodyPart.Models[modelIndex];
				var vtxModel = (vtxBodyPart is not null && modelIndex < vtxBodyPart.Models.Count)
					? vtxBodyPart.Models[modelIndex]
					: null;

				if (vtxModel is null || vtxModel.Lods.Count <= lodIndex)
				{
					globalVertexIndexStart += Math.Max(0, mdlSubModel.VertexCount);
					continue;
				}

				var vtxLod = vtxModel.Lods[lodIndex];

				var meshCount = Math.Min(vtxLod.Meshes.Count, mdlSubModel.Meshes.Count);
				for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();

					var vtxMesh = vtxLod.Meshes[meshIndex];
					var mdlMesh = mdlSubModel.Meshes[meshIndex];

					var materialIndex = mdlMesh.MaterialIndex;
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

							if (!TryGetVertex(model.Header.Flags, vvd, vertexes, v0, meshVertexIndexStart, globalVertexIndexStart, out var p0, out _))
							{
								continue;
							}

							if (!TryGetVertex(model.Header.Flags, vvd, vertexes, v1, meshVertexIndexStart, globalVertexIndexStart, out var p1, out _))
							{
								continue;
							}

							if (!TryGetVertex(model.Header.Flags, vvd, vertexes, v2, meshVertexIndexStart, globalVertexIndexStart, out var p2, out _))
							{
								continue;
							}

							var normal = Vector3.Cross(p1 - p0, p2 - p0);
							if (normal.LengthSquared() > 0)
							{
								normal = Vector3.Normalize(normal);
							}

							triangles.Add(new ModelTriangle(p0, p1, p2, normal, materialIndex));

							min = Vector3.Min(min, p0);
							min = Vector3.Min(min, p1);
							min = Vector3.Min(min, p2);
							max = Vector3.Max(max, p0);
							max = Vector3.Max(max, p1);
							max = Vector3.Max(max, p2);
						}
					}
				}

				globalVertexIndexStart += Math.Max(0, mdlSubModel.VertexCount);
			}
		}

		if (triangles.Count == 0)
		{
			throw new InvalidDataException("No triangles found (missing or unsupported VTX/VVD data).");
		}

		if (float.IsInfinity(min.X) || float.IsInfinity(max.X))
		{
			throw new InvalidDataException("Failed to compute bounds for model preview.");
		}

		return new ModelGeometry(triangles, min, max);
	}

	public static Task<ModelGeometry> LoadAsync(string mdlPath, int lodIndex = 0, CancellationToken cancellationToken = default)
	{
		return Task.Run(() => Load(mdlPath, lodIndex, cancellationToken), cancellationToken);
	}

	internal static VvdFile LoadVvd(MdlModel model, string mdlPath)
	{
		if (model.Header.Version == 53)
		{
			using var mdlStream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (MdlV53EmbeddedSectionsReader.TryRead(mdlStream, out var embedded))
			{
				using var vvdStream = new BoundedReadOnlyStream(mdlStream, embedded.VvdOffset, embedded.VvdSize);
				return VvdReader.Read(vvdStream, sourcePath: mdlPath + "#vvd", mdlVersion: model.Header.Version);
			}
		}

		var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
		var baseName = Path.GetFileNameWithoutExtension(mdlPath);
		var vvdPath = Path.Combine(directoryPath, baseName + ".vvd");
		if (!File.Exists(vvdPath))
		{
			throw new FileNotFoundException("VVD file not found next to MDL.", vvdPath);
		}

		return VvdReader.Read(vvdPath, model.Header.Version);
	}

	internal static VtxFile LoadVtx(MdlModel model, string mdlPath)
	{
		if (model.Header.Version == 53)
		{
			using var mdlStream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (MdlV53EmbeddedSectionsReader.TryRead(mdlStream, out var embedded))
			{
				using var vtxStream = new BoundedReadOnlyStream(mdlStream, embedded.VtxOffset, embedded.VtxSize);
				return VtxReader.Read(vtxStream, sourcePath: mdlPath + "#vtx");
			}
		}

		var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
		var baseName = Path.GetFileNameWithoutExtension(mdlPath);
		var vtxPath = FindVtxPath(directoryPath, baseName);
		if (vtxPath is null || !File.Exists(vtxPath))
		{
			throw new FileNotFoundException("VTX file not found next to MDL.", vtxPath ?? Path.Combine(directoryPath, baseName + ".vtx"));
		}

		return VtxReader.Read(vtxPath);
	}

	internal static string? FindVtxPath(string directoryPath, string baseName)
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

		return Directory.EnumerateFiles(directoryPath, baseName + ".*.vtx").FirstOrDefault();
	}

	private static bool TryGetVertex(
		int mdlFlags,
		VvdFile vvd,
		IReadOnlyList<VtxVertex> stripGroupVertexes,
		ushort stripGroupVertexIndex,
		int meshVertexIndexStart,
		int globalVertexIndexStart,
		out Vector3 position,
		out Vector3 normal)
	{
		position = default;
		normal = default;

		if ((uint)stripGroupVertexIndex >= (uint)stripGroupVertexes.Count)
		{
			return false;
		}

		var vtxVertex = stripGroupVertexes[stripGroupVertexIndex];
		var vertexIndex = vtxVertex.OriginalMeshVertexIndex + globalVertexIndexStart + meshVertexIndexStart;

		Stunstick.Core.Vvd.VvdVertex vertex;
		if (vvd.Header.FixupCount <= 0 || vvd.FixedVertexesByLod.Count == 0)
		{
			if ((uint)vertexIndex >= (uint)vvd.Vertexes.Count)
			{
				return false;
			}

			vertex = vvd.Vertexes[vertexIndex];
		}
		else
		{
			var fixedVertexes = vvd.FixedVertexesByLod[0];
			if ((uint)vertexIndex >= (uint)fixedVertexes.Count)
			{
				return false;
			}

			vertex = fixedVertexes[vertexIndex];
		}

		position = vertex.Position;
		normal = vertex.Normal;

		if ((mdlFlags & MdlConstants.StudioHdrFlagsStaticProp) != 0)
		{
			position = new Vector3(position.Y, -position.X, position.Z);
			normal = new Vector3(normal.Y, -normal.X, normal.Z);
		}

		return true;
	}

	private static (uint Id, int Version) ReadMdlSignature(string mdlPath)
	{
		using var stream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		if (stream.Length < 8)
		{
			return (0, 0);
		}

		using var reader = new BinaryReader(stream);
		stream.Seek(0, SeekOrigin.Begin);
		var id = reader.ReadUInt32();
		var version = reader.ReadInt32();
		return (id, version);
	}
}
