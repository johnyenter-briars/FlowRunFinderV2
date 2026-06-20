using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowRunFinderV2.Models;
using FlowRunFinderV2.Services;

namespace FlowRunFinderV2;

public sealed partial class MainWindow : Window
{
    private const int FixedRunColumnCount = 5;

    private readonly AppDataStore _appDataStore = new();
    private readonly DataverseAuthService _authService = new();
    private readonly PowerAutomateAuthService _powerAutomateAuthService = new();
    private readonly ObservableCollection<CloudFlow> _flows = new();
    private readonly ObservableCollection<FlowRun> _runs = new();
    private readonly ObservableCollection<TriggerColumnOption> _triggerColumnOptions = new();
    private AppSettings _settings = new();
    private DataverseClient? _client;
    private Uri? _environmentUrl;
    private string? _deviceVerificationUrl;
    private string? _deviceUserCode;
    private int _busyDepth;

    public MainWindow()
    {
        InitializeComponent();

        FlowComboBox.ItemsSource = _flows;
        RunsDataGrid.ItemsSource = _runs;
        TriggerColumnsItemsControl.ItemsSource = _triggerColumnOptions;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            _settings = await _appDataStore.LoadSettingsAsync();
            EnvironmentUrlTextBox.Text = _settings.LastEnvironmentUrl;
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
            _settings.LastEnvironmentUrl = environmentUrl.GetLeftPart(UriPartial.Authority);
            await _appDataStore.SaveSettingsAsync(_settings, cancellationToken);

            SetStatus("Authenticating...");
            DeviceCodePanel.IsVisible = false;

            var token = await _authService.GetTokenAsync(
                environmentUrl,
                ShowDeviceCodePrompt,
                cancellationToken);

            _client?.Dispose();
            _client = new DataverseClient(environmentUrl, token.AccessToken);
            _environmentUrl = environmentUrl;

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

    private void OnTriggerColumnsClicked(object? sender, RoutedEventArgs e)
    {
        TriggerColumnsPopup.IsOpen = !TriggerColumnsPopup.IsOpen;
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
        ResetRunColumns();
        ResetTriggerColumnOptions();
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
            if (_environmentUrl is null || FlowComboBox.SelectedItem is not CloudFlow flow)
            {
                return;
            }

            RefreshRunsButton.IsEnabled = false;
            _runs.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions();
            SetStatus($"Loading latest 50 runs for {flow.Name}...");

            var paToken = await _powerAutomateAuthService.GetTokenAsync(
                ShowDeviceCodePrompt,
                cancellationToken);

            using var paClient = new PowerAutomateClient(paToken.AccessToken);
            var environmentId = await paClient.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            var runs = await paClient.GetLatestRunsFromPowerPlatformApiAsync(
                environmentId,
                flow.WorkflowId,
                50,
                cancellationToken);

            var triggerKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var run in runs)
            {
                _runs.Add(run);

                foreach (var triggerKey in run.TriggerInputs.Keys)
                {
                    triggerKeys.Add(triggerKey);
                }
            }

            SetTriggerColumnOptions(flow, triggerKeys);

            RefreshRunsButton.IsEnabled = true;
            SetStatus($"Loaded {_runs.Count} runs for {flow.Name}.");
        });
    }

    private void ResetRunColumns()
    {
        while (RunsDataGrid.Columns.Count > FixedRunColumnCount)
        {
            RunsDataGrid.Columns.RemoveAt(RunsDataGrid.Columns.Count - 1);
        }
    }

    private void ResetTriggerColumnOptions()
    {
        _triggerColumnOptions.Clear();
        TriggerColumnsButton.IsEnabled = false;
        TriggerColumnsPopup.IsOpen = false;
    }

    private void SetTriggerColumnOptions(CloudFlow flow, IEnumerable<string> triggerKeys)
    {
        _triggerColumnOptions.Clear();
        var flowId = flow.WorkflowId.ToString("D");
        var selectedColumns = _settings.SelectedTriggerColumnsByFlowId.TryGetValue(flowId, out var cachedColumns)
            ? new HashSet<string>(cachedColumns, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in triggerKeys)
        {
            _triggerColumnOptions.Add(new TriggerColumnOption(key)
            {
                IsSelected = selectedColumns.Contains(key)
            });
        }

        TriggerColumnsButton.IsEnabled = _triggerColumnOptions.Count > 0;
        AddTriggerColumns(_triggerColumnOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Name));
    }

    private async void ApplySelectedTriggerColumns()
    {
        await ApplySelectedTriggerColumns(saveSelection: true);
    }

    private async Task ApplySelectedTriggerColumns(bool saveSelection)
    {
        var selectedColumns = _triggerColumnOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Name)
            .ToList();

        AddTriggerColumns(selectedColumns);

        if (!saveSelection || FlowComboBox.SelectedItem is not CloudFlow flow)
        {
            return;
        }

        var flowId = flow.WorkflowId.ToString("D");
        if (selectedColumns.Count == 0)
        {
            _settings.SelectedTriggerColumnsByFlowId.Remove(flowId);
        }
        else
        {
            _settings.SelectedTriggerColumnsByFlowId[flowId] = selectedColumns;
        }

        await _appDataStore.SaveSettingsAsync(_settings);
    }

    private void AddTriggerColumns(IEnumerable<string> triggerKeys)
    {
        ResetRunColumns();

        foreach (var key in triggerKeys)
        {
            RunsDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = key,
                Binding = new Binding(nameof(FlowRun.TriggerInputs))
                {
                    Mode = BindingMode.OneWay,
                    Converter = TriggerInputValueConverter.Instance,
                    ConverterParameter = key
                },
                Width = new DataGridLength(180)
            });
        }
    }

    private async Task RunUiActionAsync(Func<CancellationToken, Task> action)
    {
        ConnectButton.IsEnabled = false;
        RefreshRunsButton.IsEnabled = false;
        BeginBusy();

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
            EndBusy();
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
}

internal sealed class TriggerInputValueConverter : IValueConverter
{
    public static readonly TriggerInputValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Dictionary<string, string> triggerInputs &&
            parameter is string key &&
            triggerInputs.TryGetValue(key, out var triggerValue))
        {
            return triggerValue;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}

public sealed class TriggerColumnOption
{
    public TriggerColumnOption(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public bool IsSelected { get; set; }
}
