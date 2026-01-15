using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Stunstick.Desktop;

public sealed partial class ConfirmWindow : Window
{
	public ConfirmWindow()
	{
		InitializeComponent();
	}

	public ConfirmWindow(string message)
	{
		InitializeComponent();
		GetRequiredControl<TextBlock>("MessageTextBlock").Text = message ?? string.Empty;
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		Close(true);
	}

	private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		Close(false);
	}

	private T GetRequiredControl<T>(string name) where T : Control
	{
		return this.FindControl<T>(name) ?? throw new InvalidOperationException($"Control not found: {name}");
	}
}

