using System.Globalization;
using Stunstick.Core.Phy;

namespace Stunstick.App.Decompile;

internal static class MdlCollisionQcWriter
{
	public static async Task WriteAsync(StreamWriter writer, PhyFile physics, float mdlMass, DecompileOptions options)
	{
		var metadata = physics.Collision;
		var jointed = physics.Header.SolidCount > 1;
		var keyword = jointed
			? (options.QcUseMixedCaseForKeywords ? "$CollisionJoints" : "$collisionjoints")
			: (options.QcUseMixedCaseForKeywords ? "$CollisionModel" : "$collisionmodel");
		var mass = metadata?.TotalMass is > 0 ? metadata.TotalMass.Value
			: !jointed && metadata?.Solids.FirstOrDefault()?.Mass is double solidMass && solidMass > 0 ? solidMass
			: float.IsFinite(mdlMass) && mdlMass > 0 ? mdlMass
			: 1;

		await writer.WriteLineAsync();
		await writer.WriteLineAsync($"{keyword} \"physics.smd\"");
		await writer.WriteLineAsync("{");
		await WriteNumberAsync(options.QcUseMixedCaseForKeywords ? "$Mass" : "$mass", mass);

		if (metadata is not null)
		{
			var inertia = MostCommon(metadata.Solids.Select(solid => solid.Inertia));
			var damping = MostCommon(metadata.Solids.Select(solid => solid.Damping));
			var rotationDamping = MostCommon(metadata.Solids.Select(solid => solid.RotationDamping));
			await WriteNumberAsync("$inertia", inertia);
			await WriteNumberAsync("$damping", damping);
			await WriteNumberAsync("$rotdamping", rotationDamping);

			if (!string.IsNullOrWhiteSpace(metadata.RootBone))
			{
				await writer.WriteLineAsync($"    $rootbone {Quote(metadata.RootBone)}");
			}

			var maxConvexPieces = physics.Solids.Max(solid => solid.ConvexMeshes.Count);
			if (metadata.Concave)
			{
				await writer.WriteLineAsync("    $concave");
				if (maxConvexPieces > 0)
				{
					await writer.WriteLineAsync($"    $maxconvexpieces {maxConvexPieces}");
				}
			}

			if (jointed)
			{
				if (maxConvexPieces > 1)
				{
					await writer.WriteLineAsync("    $concaveperjoint");
				}

				var bones = new Dictionary<int, string>();
				foreach (var solid in metadata.Solids)
				{
					if (string.IsNullOrWhiteSpace(solid.Name) || !bones.TryAdd(solid.Index, Quote(solid.Name)))
					{
						continue;
					}

					var bone = bones[solid.Index];
					await WriteNumberAsync($"$jointmassbias {bone}", solid.MassBias);
					await WriteNumberAsync($"$jointinertia {bone}", solid.Inertia != inertia ? solid.Inertia : null);
					await WriteNumberAsync($"$jointdamping {bone}", solid.Damping != damping ? solid.Damping : null);
					await WriteNumberAsync($"$jointrotdamping {bone}", solid.RotationDamping != rotationDamping ? solid.RotationDamping : null);
				}

				foreach (var limit in metadata.JointLimits)
				{
					if (bones.TryGetValue(limit.ChildIndex, out var bone))
					{
						// StudioMDL stores friction at one fifth of its QC value.
						await writer.WriteLineAsync($"    $jointconstrain {bone} {limit.Axis} limit {Format(limit.Minimum)} {Format(limit.Maximum)} {Format(limit.Friction * 5)}");
					}
				}

				if (metadata.SelfCollisions is false)
				{
					await writer.WriteLineAsync("    $noselfcollisions");
				}
				else
				{
					foreach (var (first, second) in metadata.CollisionPairs)
					{
						if (bones.TryGetValue(first, out var firstBone) && bones.TryGetValue(second, out var secondBone))
						{
							await writer.WriteLineAsync($"    $jointcollide {firstBone} {secondBone}");
						}
					}
				}
			}
		}

		await writer.WriteLineAsync("}");

		Task WriteNumberAsync(string option, double? value) =>
			value is double number ? writer.WriteLineAsync($"    {option} {Format(number)}") : Task.CompletedTask;
	}

	private static double? MostCommon(IEnumerable<double?> values) =>
		values.Where(value => value.HasValue).GroupBy(value => value)
			.OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();

	private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

	private static string Quote(string value) => $"\"{value.Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ')}\"";
}
