namespace Stunstick.Core;

public static class StunstickFileClassifier
{
	public static StunstickFileType FromPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return StunstickFileType.Unknown;
		}

		var extension = Path.GetExtension(path).ToLowerInvariant();
		return extension switch
		{
			".mdl" => StunstickFileType.Mdl,
			".phy" => StunstickFileType.Phy,
			".vpk" => StunstickFileType.Vpk,
			".fpx" => StunstickFileType.Vpk,
			".gma" => StunstickFileType.Gma,
			".apk" => StunstickFileType.Apk,
			".hfs" => StunstickFileType.Hfs,
			".qc" => StunstickFileType.Qc,
			_ => StunstickFileType.Unknown
		};
	}
}
