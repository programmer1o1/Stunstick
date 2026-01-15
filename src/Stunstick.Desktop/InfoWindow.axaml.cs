using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Stunstick.Desktop;

public sealed partial class InfoWindow : Window
{
	public InfoWindow()
	{
		InitializeComponent();
	}

	public InfoWindow(string title, string message)
	{
		InitializeComponent();
		if (!string.IsNullOrWhiteSpace(title))
		{
			Title = title;
		}

		GetRequiredControl<TextBlock>("MessageTextBlock").Text = message ?? string.Empty;
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		Close();
	}

	private T GetRequiredControl<T>(string name) where T : Control
	{
		return this.FindControl<T>(name) ?? throw new InvalidOperationException($"Control not found: {name}");
	}
}

