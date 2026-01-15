namespace Stunstick.Core.Vpk;

public sealed record VpkSignatureSection(
	ReadOnlyMemory<byte> PublicKey,
	ReadOnlyMemory<byte> Signature
);

