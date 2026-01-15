using System.Numerics;

namespace Stunstick.Core.Vvd;

public readonly record struct VvdVertex(
	VvdBoneWeight BoneWeight,
	Vector3 Position,
	Vector3 Normal,
	Vector2 TexCoord
);

