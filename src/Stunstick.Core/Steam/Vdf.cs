using System.Diagnostics.CodeAnalysis;

namespace Stunstick.Core.Steam;

public abstract record VdfValue;

public sealed record VdfString(string Value) : VdfValue;

public sealed record VdfObject(IReadOnlyDictionary<string, VdfValue> Properties) : VdfValue
{
	public bool TryGetString(string key, [NotNullWhen(true)] out string? value)
	{
		value = null;
		if (!Properties.TryGetValue(key, out var raw))
		{
			return false;
		}

		if (raw is not VdfString s)
		{
			return false;
		}

		value = s.Value;
		return true;
	}

	public bool TryGetObject(string key, [NotNullWhen(true)] out VdfObject? value)
	{
		value = null;
		if (!Properties.TryGetValue(key, out var raw))
		{
			return false;
		}

		if (raw is not VdfObject obj)
		{
			return false;
		}

		value = obj;
		return true;
	}
}
