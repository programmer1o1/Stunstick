using System.Globalization;

namespace Stunstick.Core.Compiler.Smd;

/// <summary>
/// Minimal SMD reader that supports the "triangles" section for static props.
/// </summary>
public static class SmdReader
{
	public static SmdModel Read(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException("Path is required.", nameof(path));
		}

		if (!File.Exists(path))
		{
			throw new FileNotFoundException("SMD file not found.", path);
		}

		using var stream = File.OpenRead(path);
		using var reader = new StreamReader(stream);
		return Read(reader);
	}

	public static SmdModel Read(TextReader reader)
	{
		var triangles = new List<SmdTriangle>();

		var line = reader.ReadLine();
		if (line is null || !line.Trim().Equals("version 1", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Unsupported or missing SMD header (expected \"version 1\").");
		}

		// Skip nodes/skeleton; seek to triangles
		while (true)
		{
			line = reader.ReadLine() ?? throw new InvalidDataException("No triangles section found.");
			if (line.Trim().Equals("triangles", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
		}

		while (true)
		{
			var read = reader.ReadLine();
			if (read is null)
			{
				break;
			}

			line = read.Trim();
			if (line.Equals("end", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}

			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var material = line;
			var v0 = ReadVertex(reader);
			var v1 = ReadVertex(reader);
			var v2 = ReadVertex(reader);

			triangles.Add(new SmdTriangle(material, v0, v1, v2));
		}

		return new SmdModel(triangles);
	}

	private static SmdVertex ReadVertex(TextReader reader)
	{
		var line = reader.ReadLine() ?? throw new InvalidDataException("Unexpected end of SMD while reading triangle.");

		var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 9)
		{
			throw new InvalidDataException("Malformed SMD vertex line.");
		}

		int bone = int.Parse(parts[0], CultureInfo.InvariantCulture);
		float px = float.Parse(parts[1], CultureInfo.InvariantCulture);
		float py = float.Parse(parts[2], CultureInfo.InvariantCulture);
		float pz = float.Parse(parts[3], CultureInfo.InvariantCulture);
		float nx = float.Parse(parts[4], CultureInfo.InvariantCulture);
		float ny = float.Parse(parts[5], CultureInfo.InvariantCulture);
		float nz = float.Parse(parts[6], CultureInfo.InvariantCulture);
		float u = float.Parse(parts[7], CultureInfo.InvariantCulture);
		float v = float.Parse(parts[8], CultureInfo.InvariantCulture);

		return new SmdVertex(bone, px, py, pz, nx, ny, nz, u, v);
	}
}
