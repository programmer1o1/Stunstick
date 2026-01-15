namespace Stunstick.Core.Vpk;

public static class VpkConstants
{
	public const uint VpkSignature = 0x55AA1234;
	public const uint ValveSignature = VpkSignature;
	public const uint FpxSignature = 0x33FF4132;
	public const ushort DirectoryArchiveIndex = 0x7FFF;

	public static bool IsSupportedSignature(uint signature)
	{
		return signature == VpkSignature || signature == FpxSignature;
	}
}
