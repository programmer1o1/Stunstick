using System.Numerics;

namespace Stunstick.Core.Mdl;

public sealed record MdlIkChain(string Name, int EndBoneIndex, Vector3 KneeDirection);
