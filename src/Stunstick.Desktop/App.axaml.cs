using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Stunstick.Desktop;

public sealed partial class App : Application
{
	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var window = new MainWindow();
			desktop.MainWindow = window;

			SingleInstanceIpc.SetHandler(args =>
			{
				Dispatcher.UIThread.Post(() => window.HandleActivationArgsFromIpc(args));
			});
		}

		base.OnFrameworkInitializationCompleted();
	}
}
