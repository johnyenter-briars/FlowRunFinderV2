using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FlowRunFinderV2;

public sealed partial class NewConnectionDialog : Window
{
    private TextBox _nameTextBox = null!;
    private TextBox _environmentUrlTextBox = null!;
    private TextBlock _validationTextBlock = null!;

    public NewConnectionDialog()
    {
        InitializeComponent();
        this.ApplyAppIcon();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _nameTextBox = this.FindControl<TextBox>("NameTextBox")
            ?? throw new InvalidOperationException("NameTextBox was not found.");
        _environmentUrlTextBox = this.FindControl<TextBox>("EnvironmentUrlTextBox")
            ?? throw new InvalidOperationException("EnvironmentUrlTextBox was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        _validationTextBlock.Text = string.Empty;
        var name = _nameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _validationTextBlock.Text = "Enter a connection name.";
            return;
        }

        try
        {
            var environmentUrl = ParseEnvironmentUrl(_environmentUrlTextBox.Text);
            Close(new NewConnectionRequest(name, environmentUrl));
        }
        catch (Exception ex)
        {
            _validationTextBlock.Text = ex.Message;
        }
    }

    private static Uri ParseEnvironmentUrl(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Enter a Dataverse environment URL.");
        }

        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            text = $"https://{text}";
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Enter a valid Dataverse environment URL.");
        }

        return uri;
    }
}

public sealed record NewConnectionRequest(string Name, Uri EnvironmentUrl);
