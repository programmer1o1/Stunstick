using System.Diagnostics;
using System.Text;

namespace Stunstick.App.Workshop;

internal static class SteamCmdRunner
{
	private const int ChunkSizeChars = 4096;
	private const int RecentTextLimit = 16 * 1024;

	public static async Task<int> RunAsync(
		string steamCmdPath,
		IReadOnlyList<string> arguments,
		string workingDirectory,
		IProgress<string>? output,
		Func<SteamCmdPrompt, CancellationToken, Task<string?>>? promptAsync,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(steamCmdPath))
		{
			throw new ArgumentException("SteamCMD path is required.", nameof(steamCmdPath));
		}

		var startInfo = new ProcessStartInfo
		{
			FileName = steamCmdPath,
			UseShellExecute = false,
			WorkingDirectory = workingDirectory ?? string.Empty,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		foreach (var arg in arguments)
		{
			startInfo.ArgumentList.Add(arg);
		}

		using var process = new Process { StartInfo = startInfo };
		if (!process.Start())
		{
			throw new InvalidOperationException("Failed to start SteamCMD.");
		}

		using var cancellationRegistration = cancellationToken.Register(() =>
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
		});

		var detector = new PromptDetector(process.StandardInput, output, promptAsync);

		var stdoutTask = PumpAsync(process.StandardOutput, output, detector, cancellationToken);
		var stderrTask = PumpAsync(process.StandardError, output, detector, cancellationToken);

		await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdoutTask, stderrTask).ConfigureAwait(false);
		return process.ExitCode;
	}

	private static async Task PumpAsync(
		StreamReader reader,
		IProgress<string>? output,
		PromptDetector detector,
		CancellationToken cancellationToken)
	{
		var buffer = new char[ChunkSizeChars];
		var pending = new StringBuilder();

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
			if (read <= 0)
			{
				break;
			}

			var chunk = new string(buffer, 0, read);
			detector.Append(chunk);

			var start = 0;
			for (var i = 0; i < chunk.Length; i++)
			{
				if (chunk[i] != '\n')
				{
					continue;
				}

				var length = i - start;
				if (length > 0)
				{
					pending.Append(chunk, start, length);
				}

				var line = pending.ToString().TrimEnd('\r');
				pending.Clear();
				start = i + 1;

				if (!string.IsNullOrWhiteSpace(line))
				{
					output?.Report(line);
				}
			}

			if (start < chunk.Length)
			{
				pending.Append(chunk, start, chunk.Length - start);
			}

			await detector.CheckPromptsAsync(cancellationToken).ConfigureAwait(false);
		}

		var last = pending.ToString().TrimEnd('\r', '\n');
		if (!string.IsNullOrWhiteSpace(last))
		{
			output?.Report(last);
		}
	}

	private sealed class PromptDetector
	{
		private const int MaxPasswordPrompts = 3;
		private const int MaxGuardPrompts = 3;

		private readonly StreamWriter stdin;
		private readonly IProgress<string>? output;
		private readonly Func<SteamCmdPrompt, CancellationToken, Task<string?>>? promptAsync;

		private readonly StringBuilder recent = new();
		private readonly object recentLock = new();
		private readonly SemaphoreSlim promptLock = new(1, 1);
		private readonly SemaphoreSlim writeLock = new(1, 1);

		private int passwordPromptCount;
		private int guardPromptCount;

		public PromptDetector(
			StreamWriter stdin,
			IProgress<string>? output,
			Func<SteamCmdPrompt, CancellationToken, Task<string?>>? promptAsync)
		{
			this.stdin = stdin ?? throw new ArgumentNullException(nameof(stdin));
			this.output = output;
			this.promptAsync = promptAsync;
			this.stdin.AutoFlush = true;
		}

		public void Append(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			lock (recentLock)
			{
				recent.Append(text);
				if (recent.Length > RecentTextLimit)
				{
					recent.Remove(0, recent.Length - RecentTextLimit);
				}
			}
		}

		public async Task CheckPromptsAsync(CancellationToken cancellationToken)
		{
			if (promptAsync is null)
			{
				return;
			}

			await promptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				string current;
				lock (recentLock)
				{
					current = recent.ToString();
				}

				if (LooksLikePasswordPrompt(current))
				{
					if (passwordPromptCount >= MaxPasswordPrompts)
					{
						throw new InvalidDataException("SteamCMD requested a password too many times.");
					}

					passwordPromptCount++;
					ClearRecent();
					await PromptAndSendAsync(
						new SteamCmdPrompt(SteamCmdPromptKind.Password, "Steam password:"),
						cancellationToken).ConfigureAwait(false);
					return;
				}

				if (LooksLikeSteamGuardPrompt(current))
				{
					if (guardPromptCount >= MaxGuardPrompts)
					{
						throw new InvalidDataException("SteamCMD requested a Steam Guard code too many times.");
					}

					guardPromptCount++;
					ClearRecent();
					await PromptAndSendAsync(
						new SteamCmdPrompt(SteamCmdPromptKind.SteamGuardCode, "Steam Guard code:"),
						cancellationToken).ConfigureAwait(false);
				}
			}
			finally
			{
				promptLock.Release();
			}
		}

		private void ClearRecent()
		{
			lock (recentLock)
			{
				recent.Clear();
			}
		}

		private static bool LooksLikePasswordPrompt(string text)
		{
			return text.Contains("password", StringComparison.OrdinalIgnoreCase);
		}

		private static bool LooksLikeSteamGuardPrompt(string text)
		{
			return text.Contains("steam guard", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("steamguard", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("two-factor", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("two factor", StringComparison.OrdinalIgnoreCase) ||
				text.Contains("authenticator", StringComparison.OrdinalIgnoreCase);
		}

		private async Task PromptAndSendAsync(SteamCmdPrompt prompt, CancellationToken cancellationToken)
		{
			var response = await promptAsync!(prompt, cancellationToken).ConfigureAwait(false);
			if (response is null)
			{
				throw new OperationCanceledException("SteamCMD input canceled.");
			}

			var trimmed = response.Trim();
			if (trimmed.Length == 0)
			{
				throw new InvalidDataException("SteamCMD input was empty.");
			}

			await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				await stdin.WriteLineAsync(trimmed).ConfigureAwait(false);
			}
			finally
			{
				writeLock.Release();
			}

			if (prompt.Kind == SteamCmdPromptKind.Password)
			{
				output?.Report("SteamCMD: password entered.");
			}
			else if (prompt.Kind == SteamCmdPromptKind.SteamGuardCode)
			{
				output?.Report("SteamCMD: Steam Guard code entered.");
			}
		}
	}
}
