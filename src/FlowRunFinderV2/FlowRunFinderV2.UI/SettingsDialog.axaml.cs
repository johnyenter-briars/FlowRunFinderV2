using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinderV2.UI;

public sealed partial class SettingsDialog : Window
{
    private NumericUpDown _defaultRunCountNumeric = null!;
    private NumericUpDown _maxRunsToQueryNumeric = null!;
    private CheckBox _useFlowRunHistoryTableCheckBox = null!;
    private ComboBox _logVerbosityComboBox = null!;
    private TextBlock _validationTextBlock = null!;

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        this.ApplyAppIcon();
        _defaultRunCountNumeric.Value = settings.DefaultRunCount;
        _maxRunsToQueryNumeric.Value = settings.MaxRunsToQuery;
        _useFlowRunHistoryTableCheckBox.IsChecked = settings.UseFlowRunHistoryTable;
        _logVerbosityComboBox.ItemsSource = Enum.GetValues<LogVerbosity>();
        _logVerbosityComboBox.SelectedItem = settings.LogVerbosity;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _defaultRunCountNumeric = this.FindControl<NumericUpDown>("DefaultRunCountNumeric")
            ?? throw new InvalidOperationException("DefaultRunCountNumeric was not found.");
        _maxRunsToQueryNumeric = this.FindControl<NumericUpDown>("MaxRunsToQueryNumeric")
            ?? throw new InvalidOperationException("MaxRunsToQueryNumeric was not found.");
        _useFlowRunHistoryTableCheckBox = this.FindControl<CheckBox>("UseFlowRunHistoryTableCheckBox")
            ?? throw new InvalidOperationException("UseFlowRunHistoryTableCheckBox was not found.");
        _logVerbosityComboBox = this.FindControl<ComboBox>("LogVerbosityComboBox")
            ?? throw new InvalidOperationException("LogVerbosityComboBox was not found.");
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
        var defaultRunCount = _defaultRunCountNumeric.Value;
        if (defaultRunCount is null or < 1 or > 100)
        {
            _validationTextBlock.Text = "Default runs must be between 1 and 100.";
            return;
        }

        var maxRunsToQuery = _maxRunsToQueryNumeric.Value;
        if (maxRunsToQuery is null or < 1)
        {
            _validationTextBlock.Text = "Max runs to query must be at least 1.";
            return;
        }

        var logVerbosity = _logVerbosityComboBox.SelectedItem is LogVerbosity selectedVerbosity
            ? selectedVerbosity
            : LogVerbosity.Info;

        Close(new SettingsDialogResult(
            (int)defaultRunCount.Value,
            (int)maxRunsToQuery.Value,
            _useFlowRunHistoryTableCheckBox.IsChecked == true,
            logVerbosity));
    }
}

public sealed record SettingsDialogResult(
    int DefaultRunCount,
    int MaxRunsToQuery,
    bool UseFlowRunHistoryTable,
    LogVerbosity LogVerbosity);
