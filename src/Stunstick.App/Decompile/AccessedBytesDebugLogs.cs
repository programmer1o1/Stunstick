using Stunstick.Core.IO;

namespace Stunstick.App.Decompile;

internal sealed class AccessedBytesDebugLogs
{
	public AccessedBytesDebugLogs(string modelName)
	{
		if (string.IsNullOrWhiteSpace(modelName))
		{
			throw new ArgumentException("Model name is required.", nameof(modelName));
		}

		_modelName = modelName;
	}

	private readonly string _modelName;
	private readonly object _lock = new();
	private readonly Dictionary<string, AccessedBytesLog> _logsByOutputFileName = new(StringComparer.OrdinalIgnoreCase);

	public AccessedBytesLog GetOrCreateLog(string outputFileName, string displayPath, string containerPath, long containerOffset, long length)
	{
		if (string.IsNullOrWhiteSpace(outputFileName))
		{
			throw new ArgumentException("Output file name is required.", nameof(outputFileName));
		}

		lock (_lock)
		{
			if (_logsByOutputFileName.TryGetValue(outputFileName, out var existing))
			{
				return existing;
			}

			var log = new AccessedBytesLog(displayPath, containerPath, containerOffset, length);
			_logsByOutputFileName.Add(outputFileName, log);
			return log;
		}
	}

	public string BuildFileName(string suffixFileName)
	{
		if (string.IsNullOrWhiteSpace(suffixFileName))
		{
			throw new ArgumentException("Suffix file name is required.", nameof(suffixFileName));
		}

		return _modelName + " " + suffixFileName;
	}

	public IReadOnlyList<AccessedBytesDebugFileWriter.DebugFile> GetDebugFiles()
	{
		lock (_lock)
		{
			return _logsByOutputFileName
				.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
				.Select(kvp => new AccessedBytesDebugFileWriter.DebugFile(kvp.Key, kvp.Value))
				.ToList();
		}
	}
}

