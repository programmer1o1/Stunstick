using Stunstick.Core.IO;
using Stunstick.Core.Mdl;
using Stunstick.Core.Phy;
using Stunstick.Core.Vtx;
using Stunstick.Core.Vvd;
using Stunstick.App.Decompile;
using System.Numerics;

namespace Stunstick.App.Viewer;

public sealed record MdlPreviewResult(
	string MdlPath,
	MdlModel Model,
	int LodIndex,
	int MaxLodCount,
	ModelGeometry? MeshGeometry,
	string? MeshError,
	ModelGeometry? PhysicsGeometry,
	string? PhysicsError,
	PhyHeader? PhysicsHeader,
	int PhysicsSolidsRead,
	int PhysicsConvexMeshesRead);

public static class MdlPreviewLoader
{
	private const uint IdstId = 0x54534449; // "IDST"
	private const uint MdlzId = 0x5A4C444D; // "MDLZ"

	public static Task<MdlPreviewResult> LoadAsync(string mdlPath, int lodIndex = 0, bool includePhysics = true, CancellationToken cancellationToken = default)
	{
		return Task.Run(() => Load(mdlPath, lodIndex, includePhysics, cancellationToken), cancellationToken);
	}

	public static MdlPreviewResult Load(string mdlPath, int lodIndex = 0, bool includePhysics = true, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(mdlPath))
		{
			throw new ArgumentException("MDL path is required.", nameof(mdlPath));
		}

		if (!File.Exists(mdlPath))
		{
			throw new FileNotFoundException("MDL file not found.", mdlPath);
		}

		var fullPath = Path.GetFullPath(mdlPath);
		var (id, version) = ReadMdlSignature(fullPath);
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

		var model = MdlReader.Read(fullPath);

		ModelGeometry? meshGeometry = null;
		string? meshError = null;
		var maxLodCount = 0;

		try
		{
			var vvd = MdlModelGeometryLoader.LoadVvd(model, fullPath);
			var vtx = MdlModelGeometryLoader.LoadVtx(model, fullPath);
			maxLodCount = GetMaxLodCount(vtx);

			meshGeometry = MdlModelGeometryLoader.BuildMeshGeometry(model, vvd, vtx, lodIndex, cancellationToken);
		}
		catch (Exception ex)
		{
			meshError = ex.Message;
		}

		ModelGeometry? physicsGeometry = null;
		string? physicsError = null;
		PhyHeader? physicsHeader = null;
		var physicsSolidsRead = 0;
		var physicsConvexMeshesRead = 0;

		if (includePhysics)
		{
			try
			{
				var phy = TryLoadPhy(model, fullPath);
				if (phy is null)
				{
					physicsError = "PHY file not found.";
				}
				else
				{
					physicsHeader = phy.Header;
					physicsSolidsRead = phy.Solids.Count;
					foreach (var solid in phy.Solids)
					{
						physicsConvexMeshesRead += solid.ConvexMeshes.Count;
					}

					physicsGeometry = BuildPhysicsGeometry(model, phy, cancellationToken);
				}
			}
			catch (Exception ex)
			{
				physicsError = ex.Message;
			}
		}

		return new MdlPreviewResult(
			MdlPath: fullPath,
			Model: model,
			LodIndex: lodIndex,
			MaxLodCount: maxLodCount,
			MeshGeometry: meshGeometry,
			MeshError: meshError,
			PhysicsGeometry: physicsGeometry,
			PhysicsError: physicsError,
			PhysicsHeader: physicsHeader,
			PhysicsSolidsRead: physicsSolidsRead,
			PhysicsConvexMeshesRead: physicsConvexMeshesRead);
	}

	private static int GetMaxLodCount(VtxFile vtx)
	{
		var max = 0;
		foreach (var bodyPart in vtx.BodyParts)
		{
			foreach (var model in bodyPart.Models)
			{
				max = Math.Max(max, model.Lods.Count);
			}
		}

		return max;
	}

	private static PhyFile? TryLoadPhy(MdlModel model, string mdlPath)
	{
		if (model.Header.Version == 53)
		{
			using var mdlStream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (MdlV53EmbeddedSectionsReader.TryRead(mdlStream, out var embedded))
			{
				if (embedded.PhyOffset > 0 && embedded.PhySize > 0)
				{
					using var phyStream = new BoundedReadOnlyStream(mdlStream, embedded.PhyOffset, embedded.PhySize);
					return PhyReader.Read(phyStream, sourcePath: mdlPath + "#phy");
				}
			}
		}

		var directoryPath = Path.GetDirectoryName(mdlPath) ?? ".";
		var baseName = Path.GetFileNameWithoutExtension(mdlPath);
		var phyPath = Path.Combine(directoryPath, baseName + ".phy");
		if (!File.Exists(phyPath))
		{
			return null;
		}

		return PhyReader.Read(phyPath);
	}

	private static ModelGeometry BuildPhysicsGeometry(MdlModel model, PhyFile phy, CancellationToken cancellationToken)
	{
		var triangles = new List<ModelTriangle>(capacity: Math.Min(1024 * 16, phy.Solids.Count * 2048));

		var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
		var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

		var bones = model.Bones;

		var singleSolid = phy.Header.SolidCount == 1;
		var needsWorldToPose = singleSolid && model.Header.Version is < 44 or > 47;
		var worldToPose = needsWorldToPose
			? BuildWorldToPoseForPhysics(model, cancellationToken)
			: Matrix4x4.Identity;

		for (var solidIndex = 0; solidIndex < phy.Solids.Count; solidIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var solid = phy.Solids[solidIndex];
			var vertices = solid.Vertices;

			for (var meshIndex = 0; meshIndex < solid.ConvexMeshes.Count; meshIndex++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var mesh = solid.ConvexMeshes[meshIndex];

				// Match old Crowbar behavior: skip convex meshes that have children.
				if ((mesh.Flags & 3) > 0)
				{
					continue;
				}

				var boneIndex = GetPhysicsBoneIndex(bones, mesh.BoneIndex);
				var bone = (uint)boneIndex < (uint)bones.Count ? bones[boneIndex] : bones[0];

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

					var normal = Vector3.Cross(p1 - p0, p2 - p0);
					if (normal.LengthSquared() > 0)
					{
						normal = Vector3.Normalize(normal);
					}

					triangles.Add(new ModelTriangle(p0, p1, p2, normal, MaterialIndex: -1));

					min = Vector3.Min(min, p0);
					min = Vector3.Min(min, p1);
					min = Vector3.Min(min, p2);
					max = Vector3.Max(max, p0);
					max = Vector3.Max(max, p1);
					max = Vector3.Max(max, p2);
				}
			}
		}

		if (triangles.Count == 0)
		{
			throw new InvalidDataException("No physics triangles found in PHY.");
		}

		return new ModelGeometry(triangles, min, max);
	}

	private static int GetPhysicsBoneIndex(IReadOnlyList<MdlBone> bones, int boneIndexFromPhy)
	{
		if (bones.Count <= 1)
		{
			return 0;
		}

		// Match decompile: PHY stores a bone index that (after read adjustment) can be used directly when valid.
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

		// Match decompile's PHY vertex transforms so the preview aligns with physics.smd output.
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

			var toWorld = new Vector3(
				metersIvp.X * sourceUnitsPerMeter,
				-metersIvp.Y * sourceUnitsPerMeter,
				-metersIvp.Z * sourceUnitsPerMeter);

			var poseSpace2 = VectorTransform(toWorld, worldToPose);
			return VectorITransform(poseSpace2, bone.PoseToBone);
		}

		var position = new Vector3(
			metersIvp.X * sourceUnitsPerMeter,
			metersIvp.Z * sourceUnitsPerMeter,
			-metersIvp.Y * sourceUnitsPerMeter);

		return VectorITransform(position, bone.PoseToBone);
	}

	private static Matrix4x4 BuildWorldToPoseForPhysics(MdlModel model, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		// Match decompile: prefer first animDesc frame 0, fall back to root bone base pose.
		var rootPosition = model.Bones.Count > 0 ? model.Bones[0].Position : Vector3.Zero;
		var rootRotation = model.Bones.Count > 0 ? model.Bones[0].RotationRadians : Vector3.Zero;

		if (MdlAnimationSmdWriter.TryGetFirstAnimationDescFrame0RootTransform(model.SourcePath, model, out var animPosition, out var animRotation))
		{
			rootPosition = animPosition;
			rootRotation = animRotation;
		}

		rootRotation = new Vector3(rootRotation.X, rootRotation.Y, rootRotation.Z - MathF.PI / 2f);
		var poseToWorld = AngleMatrix(
			pitchRadians: rootRotation.X,
			yawRadians: rootRotation.Y,
			rollRadians: rootRotation.Z,
			translation: new Vector3(rootPosition.Y, -rootPosition.X, rootPosition.Z));

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

		// Store as row-major 3x4 (translation in M14/M24/M34).
		return new Matrix4x4(
			c0.X, c1.X, c2.X, translation.X,
			c0.Y, c1.Y, c2.Y, translation.Y,
			c0.Z, c1.Z, c2.Z, translation.Z,
			0, 0, 0, 1);
	}

	private static Matrix4x4 Matrix3x4Invert(Matrix4x4 matrix)
	{
		// Invert orthonormal matrix3x4_t (Crowbar's approach).
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
