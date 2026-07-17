using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Query;
using FlowRunFinderV2.UI;
using FlowRunFinderV2.UI.Dialog;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
    private async void OnNewConnectionClicked(object? sender, RoutedEventArgs e)
    {
        await CreateNewConnectionAsync();
    }

    private async void OnSwitchConnectionClicked(object? sender, RoutedEventArgs e)
    {
        await ShowConnectionSelectionAsync(forceSelection: false);
    }

    private async void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_settings);
        var result = await dialog.ShowDialog<SettingsDialogResult?>(this);
        if (result is null)
        {
            return;
        }

        await _settingsManager.UpdateAsync(settings =>
        {
            settings.DefaultRunCount = result.DefaultRunCount;
            settings.MaxRunsToQuery = result.MaxRunsToQuery;
            settings.UseFlowRunHistoryTable = result.UseFlowRunHistoryTable;
            settings.AuthenticationFlow = result.AuthenticationFlow;
            settings.DataverseClientId = result.DataverseClientId;
            settings.PowerAutomateClientId = result.PowerAutomateClientId;
            settings.LogVerbosity = result.LogVerbosity;
        });
        _settings = _settingsManager.Current;
        NormalizeSettings();
        if (_currentConnection is not null)
        {
            ConfigureAuthServices(_currentConnection);
        }

        _logger.SetVerbosity(_settings.LogVerbosity);
        _logger.Info($"Settings saved. DefaultRunCount={_settings.DefaultRunCount}; MaxRunsToQuery={_settings.MaxRunsToQuery}; UseFlowRunHistoryTable={_settings.UseFlowRunHistoryTable}; AuthenticationFlow={_settings.AuthenticationFlow}; DataverseClientId={_settings.DataverseClientId}; PowerAutomateClientId={_settings.PowerAutomateClientId}; LogVerbosity={_settings.LogVerbosity}.");
        SetStatus($"Settings saved. Default run count is {_settings.DefaultRunCount}; max runs to query is {_settings.MaxRunsToQuery}.");
    }

    private void OnFlowPickerClicked(object? sender, RoutedEventArgs e)
    {
        FlowSearchTextBox.Text = string.Empty;
        RefreshFilteredFlows();
        FlowPickerPopup.IsOpen = true;
        Dispatcher.UIThread.Post(() => FlowSearchTextBox.Focus());
    }

    private void OnFlowSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshFilteredFlows();
    }

    private async void OnFlowListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FlowListBox.SelectedItem is not CloudFlow flow)
        {
            return;
        }

        _selectedFlow = flow;
        FlowPickerButton.Content = flow.Name;
        FlowPickerPopup.IsOpen = false;
        FlowListBox.SelectedItem = null;
        await LoadSelectedFlowRunsAsync();
    }

    private async void OnRefreshRunsClicked(object? sender, RoutedEventArgs e)
    {
        await LoadSelectedFlowRunsAsync();
    }

    private async void OnAdvancedSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedFlow is not { } flow)
        {
            return;
        }

        _advancedSearchStateByFlowId.TryGetValue(flow.WorkflowId, out var initialState);
        var dialog = new AdvancedSearchDialog(_knownTriggerKeys, initialState);
        var request = await dialog.ShowDialog<AdvancedSearchRequest?>(this);
        if (request is null)
        {
            return;
        }

        CacheAdvancedSearchState(flow.WorkflowId, request);
        await RunAdvancedSearchAsync(flow, request);
    }

    private void OnCancelBusyActionClicked(object? sender, RoutedEventArgs e)
    {
        var querySession = _activeQuerySession;
        if (querySession is null || querySession.IsCancellationRequested)
        {
            return;
        }

        CancelBusyActionButton.IsEnabled = false;
        querySession.Cancel();
        SetStatus("Canceling query...");
        _logger.Info("Cancel requested for active query.");
    }

    private void OnTriggerColumnsClicked(object? sender, RoutedEventArgs e)
    {
        if (!TriggerColumnsPopup.IsOpen)
        {
            TriggerColumnsSearchTextBox.Text = string.Empty;
            RefreshFilteredTriggerColumnOptions();
            Dispatcher.UIThread.Post(() => TriggerColumnsSearchTextBox.Focus());
        }

        TriggerColumnsPopup.IsOpen = !TriggerColumnsPopup.IsOpen;
    }

    private void OnTriggerColumnsSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshFilteredTriggerColumnOptions();
    }

    private void OnTriggerColumnSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: TriggerColumnOption option } checkBox)
        {
            option.IsSelected = checkBox.IsChecked == true;
        }

        ApplySelectedTriggerColumns();
    }

    private async void OnCopyDeviceUrlClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_deviceVerificationUrl) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_deviceVerificationUrl);
            ShowToast("Copied to clipboard");
            SetStatus("Device login URL copied.");
        }
    }

    private async void OnCopyDeviceCodeClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_deviceUserCode) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_deviceUserCode);
            ShowToast("Copied to clipboard");
            SetStatus("Device code copied.");
        }
    }

    private async void OnRunLinkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock { DataContext: FlowRun { RunUrl: { } runUrl } })
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(runUrl);
                ShowToast("Copied to clipboard");
                SetStatus("Run URL copied.");
            }

            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = runUrl,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }

    private async void OnCopyCellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not TextBlock { Text: { } text } ||
            string.IsNullOrWhiteSpace(text) ||
            Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
        ShowToast("Copied to clipboard");
        SetStatus("Cell value copied.");
        e.Handled = true;
    }
}
