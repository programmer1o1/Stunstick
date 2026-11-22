namespace Stunstick.Core.Mdl;

public sealed record MdlSubModel(
	int Index,
	string Name,
	int VertexCount,
	IReadOnlyList<MdlMesh> Meshes
)
{
	public bool IsBlank => string.IsNullOrWhiteSpace(Name) && VertexCount == 0 && Meshes.Count == 0;
	public IReadOnlyList<MdlEyeball> Eyeballs { get; init; } = Array.Empty<MdlEyeball>();
}

