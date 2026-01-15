namespace Stunstick.Core.Compiler.Smd;

public sealed record SmdVertex(
	int Bone,
	float Px, float Py, float Pz,
	float Nx, float Ny, float Nz,
	float U, float V);

public sealed record SmdTriangle(
	string Material,
	SmdVertex V0,
	SmdVertex V1,
	SmdVertex V2);

public sealed record SmdModel(
	IReadOnlyList<SmdTriangle> Triangles)
{
	public int VertexCount => Triangles.Count * 3;
	public int TriangleCount => Triangles.Count;
}
