using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlowRunFinderV2.Services;

namespace FlowRunFinderV2;

public sealed partial class SettingsDialog : Window
{
    private NumericUpDown _defaultRunCountNumeric = null!;
    private TextBlock _validationTextBlock = null!;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        _defaultRunCountNumeric.Value = settings.DefaultRunCount;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _defaultRunCountNumeric = this.FindControl<NumericUpDown>("DefaultRunCountNumeric")
            ?? throw new InvalidOperationException("DefaultRunCountNumeric was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        _validationTextBlock.Text = string.Empty;
        var value = _defaultRunCountNumeric.Value;
        if (value is null or < 1 or > 100)
        {
            _validationTextBlock.Text = "Default runs must be between 1 and 100.";
            return;
        }

        Close(new SettingsDialogResult((int)value.Value));
    }
}

public sealed record SettingsDialogResult(int DefaultRunCount);
