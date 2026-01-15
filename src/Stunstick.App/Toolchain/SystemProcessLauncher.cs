using System.Diagnostics;

namespace Stunstick.App.Toolchain;

public sealed class SystemProcessLauncher : IProcessLauncher
{
	public async Task<int> LaunchAsync(ProcessLaunchRequest request, CancellationToken cancellationToken)
	{
		var captureOutput = request.WaitForExit && (request.StandardOutput is not null || request.StandardError is not null);

		var startInfo = new ProcessStartInfo
		{
			FileName = request.FileName,
			UseShellExecute = false,
			WorkingDirectory = request.WorkingDirectory ?? string.Empty,
			RedirectStandardOutput = captureOutput && request.StandardOutput is not null,
			RedirectStandardError = captureOutput && request.StandardError is not null
		};

		foreach (var argument in request.Arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		if (request.EnvironmentVariables is not null)
		{
			foreach (var (key, value) in request.EnvironmentVariables)
			{
				startInfo.Environment[key] = value;
			}
		}

		using var process = new Process { StartInfo = startInfo };
		if (!process.Start())
		{
			throw new InvalidOperationException("Failed to start process.");
		}

		if (!request.WaitForExit)
		{
			return 0;
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

		Task? stdoutTask = null;
		if (startInfo.RedirectStandardOutput && request.StandardOutput is not null)
		{
			stdoutTask = PumpLinesAsync(process.StandardOutput, request.StandardOutput);
		}

		Task? stderrTask = null;
		if (startInfo.RedirectStandardError && request.StandardError is not null)
		{
			stderrTask = PumpLinesAsync(process.StandardError, request.StandardError);
		}

		try
		{
			await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			try
			{
				if (stdoutTask is not null)
				{
					await stdoutTask.ConfigureAwait(false);
				}
			}
			catch
			{
			}

			try
			{
				if (stderrTask is not null)
				{
					await stderrTask.ConfigureAwait(false);
				}
			}
			catch
			{
			}
		}

		return process.ExitCode;
	}

	private static async Task PumpLinesAsync(StreamReader reader, IProgress<string> sink)
	{
		while (true)
		{
			var line = await reader.ReadLineAsync().ConfigureAwait(false);
			if (line is null)
			{
				return;
			}

			sink.Report(line);
		}
	}
}
