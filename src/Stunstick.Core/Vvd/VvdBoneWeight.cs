namespace Stunstick.Core.Vvd;

public readonly record struct VvdBoneWeight(
	float Weight0,
	float Weight1,
	float Weight2,
	byte Bone0,
	byte Bone1,
	byte Bone2,
	byte BoneCount
);

