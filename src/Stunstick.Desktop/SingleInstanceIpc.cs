using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Stunstick.Desktop;

internal static class SingleInstanceIpc
{
	private static Mutex? instanceMutex;
	private static CancellationTokenSource? serverCancellation;
	private static readonly ConcurrentQueue<string[]> Pending = new();
	private static Action<string[]>? handler;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver()
	};

	/// <summary>
	/// Returns true if this is the first instance (and the app should continue),
	/// or false if args were forwarded to an existing instance (and this process should exit).
	/// </summary>
	public static bool TryStartOrForward(string[]? args)
	{
		var key = GetInstanceKey();
		var mutexName = $"Stunstick.Desktop.{key}";
		var pipeName = $"stunstick.desktop.{key}";

		var isFirstInstance = true;
		try
		{
			instanceMutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
			isFirstInstance = createdNew;
		}
		catch
		{
			// If named mutex is unavailable for some reason, fall back to "no single-instance".
			isFirstInstance = true;
		}

		if (!isFirstInstance)
		{
			TrySendArgs(pipeName, args ?? Array.Empty<string>());
			return false;
		}

		serverCancellation = new CancellationTokenSource();
		_ = Task.Run(() => RunServerLoopAsync(pipeName, serverCancellation.Token));
		return true;
	}

	public static void SetHandler(Action<string[]> onArgs)
	{
		handler = onArgs;
		while (Pending.TryDequeue(out var args))
		{
			onArgs(args);
		}
	}

	public static void Stop()
	{
		try
		{
			serverCancellation?.Cancel();
		}
		catch
		{
		}

		try
		{
			if (instanceMutex is not null)
			{
				instanceMutex.ReleaseMutex();
			}
		}
		catch
		{
		}

		try
		{
			instanceMutex?.Dispose();
		}
		catch
		{
		}
	}

	private static async Task RunServerLoopAsync(string pipeName, CancellationToken cancellationToken)
	{
		var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await using var server = new NamedPipeServerStream(
					pipeName,
					PipeDirection.In,
					maxNumberOfServerInstances: 1,
					transmissionMode: PipeTransmissionMode.Byte,
					options: PipeOptions.Asynchronous);

				await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

				using var reader = new StreamReader(server, utf8NoBom, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
				var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				var received = JsonSerializer.Deserialize<string[]>(line, JsonOptions);
				if (received is null)
				{
					continue;
				}

				Dispatch(received);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch
			{
				// Ignore IPC failures; continue accepting future activations.
			}
		}
	}

	private static void Dispatch(string[] args)
	{
		var localHandler = handler;
		if (localHandler is null)
		{
			Pending.Enqueue(args);
			return;
		}

		localHandler(args);
	}

	private static void TrySendArgs(string pipeName, string[] args)
	{
		try
		{
			using var client = new NamedPipeClientStream(
				serverName: ".",
				pipeName,
				direction: PipeDirection.Out,
				options: PipeOptions.Asynchronous);

			client.Connect(timeout: 2000);

			var json = JsonSerializer.Serialize(args, JsonOptions);
			using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
			writer.WriteLine(json);
		}
		catch
		{
		}
	}

	private static string GetInstanceKey()
	{
		var user = Environment.UserName ?? string.Empty;
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) ?? string.Empty;
		var baseDir = AppContext.BaseDirectory ?? string.Empty;
		var raw = $"{user}|{home}|{baseDir}";

		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}
