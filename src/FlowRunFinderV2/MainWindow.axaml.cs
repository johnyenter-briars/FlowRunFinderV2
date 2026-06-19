using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowRunFinderV2.Models;
using FlowRunFinderV2.Services;

namespace FlowRunFinderV2;

public sealed partial class MainWindow : Window
{
    private readonly AppDataStore _appDataStore = new();
    private readonly DataverseAuthService _authService = new();
    private readonly ObservableCollection<CloudFlow> _flows = new();
    private readonly ObservableCollection<FlowRun> _runs = new();
    private DataverseClient? _client;
    private string? _deviceVerificationUrl;
    private string? _deviceUserCode;

    public MainWindow()
    {
        InitializeComponent();

        FlowComboBox.ItemsSource = _flows;
        RunsDataGrid.ItemsSource = _runs;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            var settings = await _appDataStore.LoadSettingsAsync();
            EnvironmentUrlTextBox.Text = settings.LastEnvironmentUrl;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load cached settings: {ex.Message}");
        }
    }

    private async void OnConnectClicked(object? sender, RoutedEventArgs e)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            var environmentUrl = ParseEnvironmentUrl();
            await _appDataStore.SaveSettingsAsync(new AppSettings
            {
                LastEnvironmentUrl = environmentUrl.GetLeftPart(UriPartial.Authority)
            }, cancellationToken);

            SetStatus("Authenticating...");
            DeviceCodePanel.IsVisible = false;

            var token = await _authService.GetTokenAsync(
                environmentUrl,
                ShowDeviceCodePrompt,
                cancellationToken);

            _client?.Dispose();
            _client = new DataverseClient(environmentUrl, token.AccessToken);

            SetStatus($"Connected. Token expires {token.ExpiresOn.LocalDateTime:g}. Loading flows...");
            await LoadFlowsAsync(cancellationToken);
        });
    }

    private async void OnFlowSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FlowComboBox.SelectedItem is CloudFlow)
        {
            await LoadSelectedFlowRunsAsync();
        }
    }

    private async void OnRefreshRunsClicked(object? sender, RoutedEventArgs e)
    {
        await LoadSelectedFlowRunsAsync();
    }

    private async void OnCopyDeviceUrlClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_deviceVerificationUrl) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_deviceVerificationUrl);
            SetStatus("Device login URL copied.");
        }
    }

    private async void OnCopyDeviceCodeClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_deviceUserCode) && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(_deviceUserCode);
            SetStatus("Device code copied.");
        }
    }

    private async Task LoadFlowsAsync(CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return;
        }

        _flows.Clear();
        _runs.Clear();
        FlowComboBox.SelectedItem = null;
        RefreshRunsButton.IsEnabled = false;

        var flows = await _client.GetCloudFlowsAsync(cancellationToken);
        foreach (var flow in flows)
        {
            _flows.Add(flow);
        }

        SetStatus($"Loaded {_flows.Count} cloud flows. Pick a flow to load the latest 50 runs.");
    }

    private async Task LoadSelectedFlowRunsAsync()
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            if (_client is null || FlowComboBox.SelectedItem is not CloudFlow flow)
            {
                return;
            }

            RefreshRunsButton.IsEnabled = false;
            _runs.Clear();
            SetStatus($"Loading latest 50 runs for {flow.Name}...");

            var runs = await _client.GetLatestRunsAsync(flow.WorkflowId, cancellationToken);
            foreach (var run in runs)
            {
                _runs.Add(run);
            }

            RefreshRunsButton.IsEnabled = true;
            SetStatus($"Loaded {_runs.Count} runs for {flow.Name}.");
        });
    }

    private async Task RunUiActionAsync(Func<CancellationToken, Task> action)
    {
        ConnectButton.IsEnabled = false;
        RefreshRunsButton.IsEnabled = false;

        try
        {
            using var cancellationTokenSource = new CancellationTokenSource();
            await action(cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            RefreshRunsButton.IsEnabled = _client is not null && FlowComboBox.SelectedItem is CloudFlow;
        }
    }

    private Uri ParseEnvironmentUrl()
    {
        var text = EnvironmentUrlTextBox.Text?.Trim();
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

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }
}
