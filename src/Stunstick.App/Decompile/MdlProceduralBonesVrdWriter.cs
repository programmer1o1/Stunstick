using Stunstick.Core.IO;
using Stunstick.Core.Mdl;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Stunstick.App.Decompile;

internal static class MdlProceduralBonesVrdWriter
{
	private const int AttachmentStructSizeBytes = 92;

	private const int StudioProcQuatInterp = 2;
	private const int StudioProcAimAtBone = 3;
	private const int StudioProcAimAtAttach = 4;

	public static async Task<string?> TryWriteProceduralBonesVrdAsync(
		string mdlPath,
		string modelOutputFolder,
		string modelFileNamePrefix,
		MdlModel model,
		DecompileOptions options,
		AccessedBytesLog? mdlAccessedBytesLog,
		CancellationToken cancellationToken)
	{
		if (!options.WriteProceduralBonesVrdFile)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(mdlPath) || !File.Exists(mdlPath))
		{
			return null;
		}

		if (model.Bones.Count == 0 || model.Header.BoneCount <= 0 || model.Header.BoneOffset <= 0)
		{
			return null;
		}

		if (string.IsNullOrWhiteSpace(modelFileNamePrefix))
		{
			return null;
		}

		var vrdFileName = modelFileNamePrefix + ".vrd";
		var vrdPathFileName = Path.Combine(modelOutputFolder, vrdFileName);

		using var fileStream = new FileStream(mdlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		Stream stream = fileStream;
		if (mdlAccessedBytesLog is not null)
		{
			stream = new AccessLoggedStream(fileStream, mdlAccessedBytesLog);
		}

		using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

		var boneSize = DetermineBoneStructSize(stream, reader, model.Header.BoneOffset, model.Header.BoneCount);
		if (boneSize is null)
		{
			return null;
		}

		if (!TryGetProceduralRuleFieldOffsets(boneSize.Value, out var procTypeOffset, out var procIndexOffset))
		{
			return null;
		}

		var attachments = ReadAttachmentNames(stream, reader, model.Header);

		var wroteAnyCommands = false;

		using var textWriter = new StringWriter(new StringBuilder(capacity: 1024), CultureInfo.InvariantCulture);

		DecompileFormat.WriteHeaderComment(textWriter, options);

		for (var boneIndex = 0; boneIndex < model.Header.BoneCount; boneIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();

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

			if (procOffset < 0 || boneStart + procOffset >= stream.Length)
			{
				continue;
			}

			if (procType == StudioProcQuatInterp)
			{
				if (TryReadQuatInterp(stream, reader, boneStart, procOffset, out var quatInterp) &&
					TryWriteQuatInterp(textWriter, model.Bones, boneIndex, quatInterp, options))
				{
					wroteAnyCommands = true;
				}
			}
			else if (procType is StudioProcAimAtBone or StudioProcAimAtAttach)
			{
				if (TryReadAimAt(stream, reader, boneStart, procOffset, out var aimAt) &&
					TryWriteAimAt(textWriter, model.Bones, attachments, boneIndex, procType, aimAt))
				{
					wroteAnyCommands = true;
				}
			}
		}

		if (!wroteAnyCommands)
		{
			return null;
		}

		var text = textWriter.ToString();
		await File.WriteAllTextAsync(vrdPathFileName, text, Encoding.UTF8, cancellationToken);
		return vrdFileName;
	}

	private static IReadOnlyList<string> ReadAttachmentNames(Stream stream, BinaryReader reader, MdlHeader header)
	{
		if (header.LocalAttachmentCount <= 0 || header.LocalAttachmentOffset <= 0)
		{
			return Array.Empty<string>();
		}

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

	private static int? DetermineBoneStructSize(Stream stream, BinaryReader reader, int boneOffset, int boneCount)
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
		// v53-like layout (adds extra vectors before poseToBone).
		if (boneStructSizeBytes >= 244)
		{
			procTypeOffset = 188;
			procIndexOffset = 192;
			return true;
		}

		// Common Source-era mstudiobone_t layout.
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

	private static string ReadNullTerminatedStringAt(Stream stream, long offset, int maxBytes)
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

	private readonly record struct QuatInterpData(int ControlBoneIndex, IReadOnlyList<QuatInterpTrigger> Triggers);
	private readonly record struct QuatInterpTrigger(float InverseToleranceAngle, Quaternion Trigger, Vector3 Pos, Quaternion Quat);
	private readonly record struct AimAtData(int ParentBoneIndex, int AimBoneOrAttachmentIndex, Vector3 Aim, Vector3 Up, Vector3 BasePos);

	private static bool TryReadQuatInterp(Stream stream, BinaryReader reader, long boneStart, int procOffset, out QuatInterpData data)
	{
		data = default;
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

			if (triggerCount <= 0 || triggerOffset == 0)
			{
				data = new QuatInterpData(controlBoneIndex, Array.Empty<QuatInterpTrigger>());
				return true;
			}

			if (triggerCount > 10000)
			{
				return false;
			}

			var triggerStart = procStart + triggerOffset;
			const int triggerSizeBytes = 48;
			var requiredBytes = (long)triggerCount * triggerSizeBytes;
			if (triggerStart < 0 || triggerStart + requiredBytes > stream.Length)
			{
				return false;
			}

			stream.Seek(triggerStart, SeekOrigin.Begin);
			var triggers = new List<QuatInterpTrigger>(capacity: triggerCount);
			for (var i = 0; i < triggerCount; i++)
			{
				var inverseToleranceAngle = reader.ReadSingle();

				var trigger = new Quaternion(
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle());

				var pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

				var quat = new Quaternion(
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle());

				triggers.Add(new QuatInterpTrigger(inverseToleranceAngle, trigger, pos, quat));
			}

			data = new QuatInterpData(controlBoneIndex, triggers);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryReadAimAt(Stream stream, BinaryReader reader, long boneStart, int procOffset, out AimAtData data)
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
			var aimBoneOrAttachmentIndex = reader.ReadInt32();

			var aim = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var up = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			var basePos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			data = new AimAtData(parentBoneIndex, aimBoneOrAttachmentIndex, aim, up, basePos);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryWriteQuatInterp(TextWriter writer, IReadOnlyList<MdlBone> bones, int boneIndex, QuatInterpData data, DecompileOptions options)
	{
		if (boneIndex < 0 || boneIndex >= bones.Count)
		{
			return false;
		}

		var bone = bones[boneIndex];
		if (bone.ParentIndex < 0 || bone.ParentIndex >= bones.Count)
		{
			return false;
		}

		var controlBoneIndex = data.ControlBoneIndex;
		if (controlBoneIndex < 0 || controlBoneIndex >= bones.Count)
		{
			return false;
		}

		var controlBone = bones[controlBoneIndex];
		if (controlBone.ParentIndex < 0 || controlBone.ParentIndex >= bones.Count)
		{
			return false;
		}

		var parentBoneName = StripVrdName(bones[bone.ParentIndex].Name);
		var parentControlBoneName = StripVrdName(bones[controlBone.ParentIndex].Name);
		var controlBoneName = StripVrdName(controlBone.Name);
		var boneName = StripVrdName(bone.Name);

		writer.WriteLine();
		writer.WriteLine($"<helper> {boneName} {parentBoneName} {parentControlBoneName} {controlBoneName}");
		writer.WriteLine("<basepos> 0 0 0");

		var format = CultureInfo.InvariantCulture;
		var degrees = 180.0 / Math.PI;

		for (var i = 0; i < data.Triggers.Count; i++)
		{
			var trigger = data.Triggers[i];
			var toleranceDeg = trigger.InverseToleranceAngle != 0
				? (1.0 / trigger.InverseToleranceAngle) * degrees
				: 0.0;

			var triggerEuler = ToEulerAngles(trigger.Trigger);
			var quatEuler = ToEulerAngles(trigger.Quat);

			writer.WriteLine(string.Format(
				format,
				"<trigger> {0:0.######} {1:0.######} {2:0.######} {3:0.######} {4:0.######} {5:0.######} {6:0.######} {7:0.######} {8:0.######} {9:0.######}",
				toleranceDeg,
				triggerEuler.X * degrees, triggerEuler.Y * degrees, triggerEuler.Z * degrees,
				quatEuler.X * degrees, quatEuler.Y * degrees, quatEuler.Z * degrees,
				trigger.Pos.X, trigger.Pos.Y, trigger.Pos.Z));
		}

		_ = options; // reserved for future formatting toggles
		return true;
	}

	private static bool TryWriteAimAt(
		TextWriter writer,
		IReadOnlyList<MdlBone> bones,
		IReadOnlyList<string> attachments,
		int boneIndex,
		int procType,
		AimAtData data)
	{
		if (boneIndex < 0 || boneIndex >= bones.Count)
		{
			return false;
		}

		var boneName = StripVrdName(bones[boneIndex].Name);

		if (data.ParentBoneIndex < 0 || data.ParentBoneIndex >= bones.Count)
		{
			return false;
		}

		var parentBoneName = StripVrdName(bones[data.ParentBoneIndex].Name);

		string? aimName = null;
		if (procType == StudioProcAimAtBone)
		{
			if (data.AimBoneOrAttachmentIndex >= 0 && data.AimBoneOrAttachmentIndex < bones.Count)
			{
				aimName = StripVrdName(bones[data.AimBoneOrAttachmentIndex].Name);
			}
		}
		else if (procType == StudioProcAimAtAttach)
		{
			if (data.AimBoneOrAttachmentIndex >= 0 && data.AimBoneOrAttachmentIndex < attachments.Count)
			{
				aimName = StripVrdName(attachments[data.AimBoneOrAttachmentIndex]);
			}
		}

		if (string.IsNullOrWhiteSpace(aimName))
		{
			return false;
		}

		var format = CultureInfo.InvariantCulture;

		writer.WriteLine($"<aimconstraint> {boneName} {parentBoneName} {aimName}");
		writer.WriteLine(string.Format(format, "<aimvector> {0:0.######} {1:0.######} {2:0.######}", data.Aim.X, data.Aim.Y, data.Aim.Z));
		writer.WriteLine(string.Format(format, "<upvector> {0:0.######} {1:0.######} {2:0.######}", data.Up.X, data.Up.Y, data.Up.Z));
		writer.WriteLine(string.Format(format, "<basepos> {0:0.######} {1:0.######} {2:0.######}", data.BasePos.X, data.BasePos.Y, data.BasePos.Z));

		return true;
	}

	private static string StripVrdName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return string.Empty;
		}

		var dot = name.IndexOf('.', StringComparison.Ordinal);
		if (dot < 0)
		{
			return name;
		}

		return dot + 1 < name.Length ? name[(dot + 1)..] : string.Empty;
	}

	private static Vector3 ToEulerAngles(Quaternion q)
	{
		// Mirrors Crowbar MathModule.ToEulerAngles(): Eul_FromQuat(q, 0, 1, 2, 0, Even, No, S)
		var n = (double)q.X * q.X + (double)q.Y * q.Y + (double)q.Z * q.Z + (double)q.W * q.W;
		var s = n > 0.0 ? 2.0 / n : 0.0;

		var xs = q.X * s;
		var ys = q.Y * s;
		var zs = q.Z * s;

		var wx = q.W * xs;
		var wy = q.W * ys;
		var wz = q.W * zs;
		var xx = q.X * xs;
		var xy = q.X * ys;
		var xz = q.X * zs;
		var yy = q.Y * ys;
		var yz = q.Y * zs;
		var zz = q.Z * zs;

		var m00 = 1.0 - (yy + zz);
		var m10 = xy + wz;
		var m20 = xz - wy;
		var m21 = yz + wx;
		var m22 = 1.0 - (xx + yy);

		var cy = Math.Sqrt(m00 * m00 + m10 * m10);
		const double epsilon = 0.00001;

		double x;
		double y;
		double z;

		if (cy > 16.0 * epsilon)
		{
			x = Math.Atan2(m21, m22);
			y = Math.Atan2(-m20, cy);
			z = Math.Atan2(m10, m00);
		}
		else
		{
			var m11 = 1.0 - (xx + zz);
			var m12 = yz - wx;
			x = Math.Atan2(-m12, m11);
			y = Math.Atan2(-m20, cy);
			z = 0.0;
		}

		return new Vector3((float)x, (float)y, (float)z);
	}
}
