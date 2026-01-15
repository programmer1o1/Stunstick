using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace Stunstick.Desktop;

internal sealed class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		if (!SingleInstanceIpc.TryStartOrForward(args))
		{
			return;
		}

		var builder = BuildAvaloniaApp();
		builder.StartWithClassicDesktopLifetime(args);

		SingleInstanceIpc.Stop();
	}

	public static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder
			.Configure<App>()
			.UsePlatformDetect()
			.LogToTrace();
	}
}
