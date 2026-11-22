using System.Globalization;
using Stunstick.Core.Mdl;

namespace Stunstick.App.Decompile;

internal static class MdlJiggleBoneQcWriter
{
	public static async Task WriteAsync(StreamWriter writer, IReadOnlyList<MdlBone> bones, DecompileOptions options)
	{
		var keyword = options.QcUseMixedCaseForKeywords ? "$JiggleBone" : "$jigglebone";
		foreach (var bone in bones)
		{
			if (bone.Jiggle is not { } rule)
			{
				continue;
			}

			using var output = new StringWriter(CultureInfo.InvariantCulture);
			output.WriteLine();
			output.WriteLine($"{keyword} \"{bone.Name.Replace('"', '\'')}\"");
			output.WriteLine("{");
			if (rule.Flags.HasFlag(MdlJiggleFlags.Flexible))
			{
				WriteMotion(output, rule, flexible: true);
			}
			if (rule.Flags.HasFlag(MdlJiggleFlags.Rigid))
			{
				WriteMotion(output, rule, flexible: false);
			}
			if (rule.Flags.HasFlag(MdlJiggleFlags.BaseSpring))
			{
				output.WriteLine("    has_base_spring {");
				WriteValue(output, "base_mass", rule.BaseMass);
				WriteValue(output, "stiffness", rule.BaseStiffness);
				WriteValue(output, "damping", rule.BaseDamping);
				WriteConstraint(output, "left", rule.Left, angular: false);
				WriteConstraint(output, "up", rule.Up, angular: false);
				WriteConstraint(output, "forward", rule.Forward, angular: false);
				output.WriteLine("    }");
			}
			if (rule.Boing is { } boing)
			{
				output.WriteLine("    is_boing {");
				WriteValue(output, "impact_speed", boing.ImpactSpeed);
				WriteValue(output, "impact_angle", ToDegrees(Math.Acos(Math.Clamp(boing.ImpactAngleCosine, -1, 1))));
				WriteValue(output, "damping_rate", boing.DampingRate);
				WriteValue(output, "frequency", boing.Frequency);
				WriteValue(output, "amplitude", boing.Amplitude);
				output.WriteLine("    }");
			}
			output.WriteLine("}");
			await writer.WriteAsync(output.ToString());
		}
	}

	private static void WriteMotion(TextWriter output, MdlJiggleBone rule, bool flexible)
	{
		output.WriteLine(flexible ? "    is_flexible {" : "    is_rigid {");
		WriteValue(output, "length", rule.Length);
		WriteValue(output, "tip_mass", rule.TipMass);
		if (flexible)
		{
			WriteValue(output, "pitch_stiffness", rule.PitchStiffness);
			WriteValue(output, "pitch_damping", rule.PitchDamping);
			WriteValue(output, "yaw_stiffness", rule.YawStiffness);
			WriteValue(output, "yaw_damping", rule.YawDamping);
			if (!rule.Flags.HasFlag(MdlJiggleFlags.LengthConstraint))
			{
				output.WriteLine("        allow_length_flex");
			}
			WriteValue(output, "along_stiffness", rule.AlongStiffness);
			WriteValue(output, "along_damping", rule.AlongDamping);
		}
		if (rule.Flags.HasFlag(MdlJiggleFlags.PitchConstraint))
		{
			WriteConstraint(output, "pitch", rule.Pitch, angular: true);
		}
		if (rule.Flags.HasFlag(MdlJiggleFlags.YawConstraint))
		{
			WriteConstraint(output, "yaw", rule.Yaw, angular: true);
		}
		if (rule.Flags.HasFlag(MdlJiggleFlags.AngleConstraint))
		{
			WriteValue(output, "angle_constraint", ToDegrees(rule.AngleLimit));
		}
		output.WriteLine("    }");
	}

	private static void WriteConstraint(TextWriter output, string axis, MdlJiggleConstraint constraint, bool angular)
	{
		var min = angular ? ToDegrees(constraint.Min) : constraint.Min;
		var max = angular ? ToDegrees(constraint.Max) : constraint.Max;
		output.WriteLine(FormattableString.Invariant($"        {axis}_constraint {min:0.######} {max:0.######}"));
		WriteValue(output, $"{axis}_friction", constraint.Friction);
		if (angular)
		{
			WriteValue(output, $"{axis}_bounce", constraint.Bounce);
		}
	}

	private static void WriteValue(TextWriter output, string name, double value) =>
		output.WriteLine(FormattableString.Invariant($"        {name} {value:0.######}"));

	private static double ToDegrees(double radians) => radians * 180 / Math.PI;
}
