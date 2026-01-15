using System.Collections.Immutable;

namespace Stunstick.Core.Compiler.Qc;

/// <summary>
/// Root QC file representation (Source SDK 2013 target).
/// </summary>
public sealed record QcFile(IReadOnlyList<QcCommand> Commands)
{
	public static readonly QcFile Empty = new(Array.Empty<QcCommand>());
}

/// <summary>
/// Base QC command with directive name and raw arguments (typed commands derive from this).
/// </summary>
public abstract record QcCommand(string Name)
{
	public ImmutableArray<string> RawArgs { get; init; } = ImmutableArray<string>.Empty;
}

public sealed record QcModelName(string Path) : QcCommand("$modelname");

public sealed record QcBody(string BodyName, string SmdPath) : QcCommand("$body");

public sealed record QcBodyGroup(string GroupName, ImmutableArray<QcBodyGroupChoice> Choices) : QcCommand("$bodygroup");

public sealed record QcBodyGroupChoice(string Name, string SmdPath);

public sealed record QcCd(string Directory) : QcCommand("$cd");

public sealed record QcCdMaterials(ImmutableArray<string> Paths) : QcCommand("$cdmaterials");

public sealed record QcSequence(string SequenceName, ImmutableArray<string> SmdPaths, float Fps, bool Loop) : QcCommand("$sequence");

public sealed record QcCollisionModel(string SmdPath) : QcCommand("$collisionmodel");

public sealed record QcSurfaceProp(string Value) : QcCommand("$surfaceprop");

public sealed record QcStaticProp(bool IsStatic) : QcCommand("$staticprop");

public sealed record QcOrigin(float X, float Y, float Z) : QcCommand("$origin");

public sealed record QcScale(float Value) : QcCommand("$scale");

/// <summary>
/// Unrecognized or passthrough directive; keeps original text for future handling.
/// </summary>
public sealed record QcUnknown(string Directive, ImmutableArray<string> Arguments) : QcCommand(Directive);
