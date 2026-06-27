using Avalonia.Threading;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
    private async Task RunUiActionAsync(Func<CancellationToken, Task> action)
    {
        NewConnectionButton.IsEnabled = false;
        SwitchConnectionButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        RefreshRunsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = false;
        BeginBusy();

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            await action(cancellationTokenSource.Token);
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
            RefreshRunsButton.IsEnabled = _client is not null && _selectedFlow is not null;
            AdvancedSearchButton.IsEnabled = _triggerColumnOptions.Count > 0;
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
