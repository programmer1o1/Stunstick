using System.Numerics;

namespace Stunstick.App.Viewer;

public readonly record struct ModelTriangle(
	Vector3 A,
	Vector3 B,
	Vector3 C,
	Vector3 Normal,
	int MaterialIndex);

public sealed record ModelGeometry(
	IReadOnlyList<ModelTriangle> Triangles,
	Vector3 Min,
	Vector3 Max);
