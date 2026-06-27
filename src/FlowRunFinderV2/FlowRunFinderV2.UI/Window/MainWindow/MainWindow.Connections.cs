using Avalonia.Threading;
using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Configuration;
using FlowRunFinderV2.UI;
using FlowRunFinderV2.UI.Dialog;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
    private async Task SelectStartupConnectionAsync()
    {
        await ShowConnectionSelectionAsync(forceSelection: true);
    }

    private async Task ShowConnectionSelectionAsync(bool forceSelection)
    {
        while (true)
        {
            var connections = await _appDataStore.LoadConnectionsAsync();
            if (connections.Count == 0)
            {
                var created = await CreateNewConnectionAsync();
                if (created || !forceSelection)
                {
                    return;
                }

                continue;
            }

            var dialog = new ConnectionSelectionDialog(connections);
            var result = await dialog.ShowDialog<ConnectionSelectionResult?>(this);
            if (result is null)
            {
                if (forceSelection)
                {
                    Close();
                }

                return;
            }

            if (result.CreateNew)
            {
                var created = await CreateNewConnectionAsync();
                if (created || !forceSelection)
                {
                    return;
                }

                continue;
            }

            if (result.Connection is not null)
            {
                await OpenConnectionAsync(result.Connection);
                return;
            }
        }
    }

    private async Task<bool> CreateNewConnectionAsync()
    {
        var dialog = new NewConnectionDialog();
        var request = await dialog.ShowDialog<NewConnectionRequest?>(this);
        if (request is null)
        {
            return false;
        }

        var connection = await _appDataStore.CreateConnectionAsync(request.Name, request.EnvironmentUrl);
        await OpenConnectionAsync(connection);
        return true;
    }

    private async Task OpenConnectionAsync(ConnectionProfile connection)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            _currentConnection = connection;
            _logger.SetConnection(connection);
            _logger.Info("Opening connection.");
            _environmentUrl = new Uri(connection.EnvironmentUrl);
            ConfigureAuthServices(connection);

            ConnectionTextBlock.Text = $"{connection.Name} - {connection.EnvironmentUrl}";
            await _settingsManager.SaveAsync(_settings, cancellationToken);

            _flows.Clear();
            _runs.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions(clearKnownKeys: true);
            ClearSelectedFlow();
            RefreshRunsButton.IsEnabled = false;
            AdvancedSearchButton.IsEnabled = false;
            DeviceCodePanel.IsVisible = false;

            SetStatus("Authenticating...");
            var token = await _authService.GetTokenAsync(
                _environmentUrl,
                ShowDeviceCodePrompt,
                cancellationToken);
            ClearDeviceCodePrompt();

            _client?.Dispose();
            _client = new DataverseClient(_environmentUrl, token.AccessToken);

            SetStatus($"Connected. Token expires {token.ExpiresOn.LocalDateTime:g}. Loading flows...");
            _logger.Info($"Dataverse authentication succeeded. TokenExpires={token.ExpiresOn:O}.");
            await LoadFlowsAsync(cancellationToken);
        });
    }

    private async Task LoadFlowsAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }

        _flows.Clear();
        _runs.Clear();
        ResetRunColumns();
        ResetTriggerColumnOptions(clearKnownKeys: true);
        ClearSelectedFlow();
        RefreshRunsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = false;

        var flows = await _client.GetCloudFlowsAsync(cancellationToken);
        _logger.Info($"Loaded flows. Count={flows.Count}.");
        foreach (var flow in flows)
        {
            _flows.Add(flow);
        }

        RefreshFilteredFlows();
        SetStatus($"Loaded {_flows.Count} cloud flows. Pick a flow to load the latest {_settings.DefaultRunCount} runs.");
    }

    private void ShowDeviceCodePrompt(DeviceCodePrompt prompt)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _deviceVerificationUrl = prompt.VerificationUrl;
            _deviceUserCode = prompt.UserCode;
            DeviceCodeMessageTextBlock.Text = "Open the login URL in your browser and enter the device code.";
            DeviceCodeUrlTextBlock.Text = prompt.VerificationUrl;
            DeviceCodeTextBlock.Text = prompt.UserCode;
            DeviceCodePanel.IsVisible = true;
        });
    }

    private void ClearDeviceCodePrompt()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _deviceVerificationUrl = null;
            _deviceUserCode = null;
            DeviceCodeMessageTextBlock.Text = string.Empty;
            DeviceCodeUrlTextBlock.Text = string.Empty;
            DeviceCodeTextBlock.Text = string.Empty;
            DeviceCodePanel.IsVisible = false;
        });
    }

    private void NormalizeSettings()
    {
        _settings.DefaultRunCount = Math.Clamp(_settings.DefaultRunCount, 1, 100);
        _settings.MaxRunsToQuery = Math.Max(_settings.MaxRunsToQuery, 1);
        if (!Guid.TryParse(_settings.DataverseClientId, out _))
        {
            _settings.DataverseClientId = AuthenticationClientIds.Dataverse;
        }

        if (!Guid.TryParse(_settings.PowerAutomateClientId, out _))
        {
            _settings.PowerAutomateClientId = AuthenticationClientIds.PowerAutomate;
        }

        if (!Enum.IsDefined(_settings.LogVerbosity))
        {
            _settings.LogVerbosity = LogVerbosity.Info;
        }
    }

    private void ConfigureAuthServices(ConnectionProfile connection)
    {
        var tokenCacheOptions = new TokenCacheOptions(_appDataStore.GetConnectionFolder(connection.Id));
        _authService = new DataverseAuthService(tokenCacheOptions, _settings.DataverseClientId);
        _powerAutomateAuthService = new PowerAutomateAuthService(tokenCacheOptions, _settings.PowerAutomateClientId);
    }
}
