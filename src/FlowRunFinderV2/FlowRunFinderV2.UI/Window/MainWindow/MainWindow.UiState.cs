using Avalonia.Threading;
using FlowRunFinderV2.Core.Query;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
    private async Task RunUiActionAsync(Func<CancellationToken, Task> action, bool canCancel = false)
    {
        NewConnectionButton.IsEnabled = false;
        SwitchConnectionButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        RefreshRunsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = false;
        BeginBusy();

        FlowRunQuerySession? querySession = null;
        try
        {
            if (canCancel)
            {
                querySession = new FlowRunQuerySession();
                _activeQuerySession = querySession;
                CancelBusyActionButton.IsVisible = true;
                CancelBusyActionButton.IsEnabled = true;
            }

            await action(querySession?.CancellationToken ?? CancellationToken.None);
        }
        catch (OperationCanceledException) when (querySession?.IsCancellationRequested == true)
        {
            SetStatus("Query canceled.");
            _logger.Info("Query canceled by user.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            _logger.Error("UI action failed.", ex);
        }
        finally
        {
            EndBusy();
            NewConnectionButton.IsEnabled = true;
            SwitchConnectionButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            if (_triggerColumnOptions.Count == 0 &&
                _knownTriggerKeys.Count > 0 &&
                _selectedFlow is { } selectedFlow)
            {
                RestoreTriggerColumnOptions(selectedFlow);
            }

            RefreshRunsButton.IsEnabled = _client is not null && _selectedFlow is not null;
            AdvancedSearchButton.IsEnabled = _knownTriggerKeys.Count > 0;
            CancelBusyActionButton.IsVisible = false;
            CancelBusyActionButton.IsEnabled = false;
            if (ReferenceEquals(_activeQuerySession, querySession))
            {
                _activeQuerySession = null;
            }

            querySession?.Dispose();
        }
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
        if (_busyDepth > 0)
        {
            BusyTextBlock.Text = message;
        }
    }

    private void BeginBusy()
    {
        _busyDepth++;
        BusyTextBlock.Text = StatusTextBlock.Text ?? "Working...";
        BusyOverlay.IsVisible = true;
    }

    private void EndBusy()
    {
        _busyDepth = Math.Max(0, _busyDepth - 1);
        BusyOverlay.IsVisible = _busyDepth > 0;
    }

    private void ShowToast(string message)
    {
        _toastCancellationTokenSource?.Cancel();
        _toastCancellationTokenSource = new CancellationTokenSource();
        var token = _toastCancellationTokenSource.Token;

        ToastTextBlock.Text = message;
        ToastOverlay.IsVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                if (!token.IsCancellationRequested)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => ToastOverlay.IsVisible = false);
                }
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }
}
