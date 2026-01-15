using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Stunstick.App.Workshop;

namespace Stunstick.Desktop;

public sealed partial class SteamCmdPromptWindow : Window
{
	public SteamCmdPromptWindow()
	{
		InitializeComponent();
	}

	public SteamCmdPromptWindow(SteamCmdPrompt prompt)
	{
		InitializeComponent();

		GetRequiredControl<TextBlock>("MessageTextBlock").Text = prompt?.Message ?? "SteamCMD input required.";

		var responseTextBox = GetRequiredControl<TextBox>("ResponseTextBox");
		if (prompt?.Kind == SteamCmdPromptKind.Password)
		{
			responseTextBox.PasswordChar = '•';
		}

		Opened += (_, _) => responseTextBox.Focus();
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void OnOkClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		var response = GetRequiredControl<TextBox>("ResponseTextBox").Text;
		Close(response);
	}

	private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		Close(null);
	}

	private T GetRequiredControl<T>(string name) where T : Control
	{
		return this.FindControl<T>(name) ?? throw new InvalidOperationException($"Control not found: {name}");
	}
}

