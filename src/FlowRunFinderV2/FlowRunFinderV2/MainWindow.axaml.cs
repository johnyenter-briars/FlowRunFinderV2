using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FlowRunFinderV2.Models;
using FlowRunFinderV2.Services;

namespace FlowRunFinderV2;

public sealed partial class MainWindow : Window
{
    private const int FixedRunColumnCount = 4;

    private readonly AppDataStore _appDataStore = new();
    private readonly AppLogger _logger;
    private readonly ObservableCollection<CloudFlow> _flows = new();
    private readonly ObservableCollection<FlowRun> _runs = new();
    private readonly ObservableCollection<TriggerColumnOption> _triggerColumnOptions = new();
    private readonly SortedSet<string> _knownTriggerKeys = new(AttributeNameComparer.Instance);
    private readonly Dictionary<Guid, AdvancedSearchState> _advancedSearchStateByFlowId = new();
    private AppSettings _settings = new();
    private ConnectionProfile? _currentConnection;
    private DataverseAuthService? _authService;
    private PowerAutomateAuthService? _powerAutomateAuthService;
    private DataverseClient? _client;
    private Uri? _environmentUrl;
    private string? _deviceVerificationUrl;
    private string? _deviceUserCode;
    private int _busyDepth;

    public MainWindow()
    {
        InitializeComponent();
        SetWindowIcon();
        _logger = new AppLogger(_appDataStore.LogsFolder);

        FlowComboBox.ItemsSource = _flows;
        RunsDataGrid.ItemsSource = _runs;
        TriggerColumnsItemsControl.ItemsSource = _triggerColumnOptions;

        Opened += OnOpened;
    }

    private void SetWindowIcon()
    {
        var iconUri = new Uri("avares://FlowRunFinderV2/Assets/FlowRunFinderV2.ico");
        using var stream = AssetLoader.Open(iconUri);
        Icon = new WindowIcon(stream);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            _settings = await _appDataStore.LoadSettingsAsync();
            NormalizeSettings();
            _logger.SetVerbosity(_settings.LogVerbosity);
            _logger.Info("Application opened.");
            await SelectStartupConnectionAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load cached settings: {ex.Message}");
            _logger.Error("Startup failed.", ex);
        }
    }

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

        _settings.DefaultRunCount = result.DefaultRunCount;
        _settings.LogVerbosity = result.LogVerbosity;
        _logger.SetVerbosity(_settings.LogVerbosity);
        await _appDataStore.SaveSettingsAsync(_settings);
        _logger.Info($"Settings saved. DefaultRunCount={_settings.DefaultRunCount}; LogVerbosity={_settings.LogVerbosity}.");
        SetStatus($"Settings saved. Default run query count is {_settings.DefaultRunCount}.");
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

    private async void OnAdvancedSearchClicked(object? sender, RoutedEventArgs e)
    {
        if (FlowComboBox.SelectedItem is not CloudFlow flow)
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
        try
        {
            await OpenConnectionAsync(connection);
            return true;
        }
        catch
        {
            throw;
        }
    }

    private async Task OpenConnectionAsync(ConnectionProfile connection)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            _currentConnection = connection;
            _logger.SetConnection(connection);
            _logger.Info("Opening connection.");
            _environmentUrl = new Uri(connection.EnvironmentUrl);
            _authService = new DataverseAuthService(_appDataStore.GetDataverseTokenCachePath(connection.Id));
            _powerAutomateAuthService = new PowerAutomateAuthService(_appDataStore.GetPowerAutomateTokenCachePath(connection.Id));

            ConnectionTextBlock.Text = $"{connection.Name} - {connection.EnvironmentUrl}";
            await _appDataStore.SaveSettingsAsync(_settings, cancellationToken);

            _flows.Clear();
            _runs.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions(clearKnownKeys: true);
            FlowComboBox.SelectedItem = null;
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
        FlowComboBox.SelectedItem = null;
        RefreshRunsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = false;

        var flows = await _client.GetCloudFlowsAsync(cancellationToken);
        _logger.Info($"Loaded flows. Count={flows.Count}.");
        foreach (var flow in flows)
        {
            _flows.Add(flow);
        }

        SetStatus($"Loaded {_flows.Count} cloud flows. Pick a flow to load the latest {_settings.DefaultRunCount} runs.");
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
            ResetTriggerColumnOptions(clearKnownKeys: true);
            SetStatus($"Loading latest {_settings.DefaultRunCount} runs for {flow.Name}...");
            _logger.Info($"Loading latest runs. FlowId={flow.WorkflowId}; FlowName={flow.Name}; Top={_settings.DefaultRunCount}.");

            if (_powerAutomateAuthService is null)
            {
                return;
            }

            var paToken = await _powerAutomateAuthService.GetTokenAsync(
                ShowDeviceCodePrompt,
                cancellationToken);
            ClearDeviceCodePrompt();

            using var paClient = new PowerAutomateClient(paToken.AccessToken, _logger);
            var environmentId = await paClient.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            var runs = await paClient.GetLatestRunsFromPowerPlatformApiAsync(
                environmentId,
                flow.WorkflowId,
                _settings.DefaultRunCount,
                cancellationToken);

            var triggerKeys = new SortedSet<string>(AttributeNameComparer.Instance);
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
            _logger.Info($"Latest runs loaded. FlowId={flow.WorkflowId}; Runs={_runs.Count}; TriggerKeys={triggerKeys.Count}.");
            SetStatus($"Loaded {_runs.Count} runs for {flow.Name}.");
        });
    }

    private async Task RunAdvancedSearchAsync(CloudFlow flow, AdvancedSearchRequest request)
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            if (_environmentUrl is null)
            {
                return;
            }

            _runs.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions(clearKnownKeys: false);
            SetStatus($"Searching runs for {flow.Name}...");
            _logger.Info($"Advanced search started. FlowId={flow.WorkflowId}; FlowName={flow.Name}; StartUtc={request.StartUtc:O}; EndUtc={request.EndUtc:O}; Criteria={FormatCriteria(request.Criteria)}.");

            if (_powerAutomateAuthService is null)
            {
                return;
            }

            var paToken = await _powerAutomateAuthService.GetTokenAsync(
                ShowDeviceCodePrompt,
                cancellationToken);
            ClearDeviceCodePrompt();

            using var paClient = new PowerAutomateClient(paToken.AccessToken, _logger);
            var environmentId = await paClient.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment for advanced search. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            var runs = await paClient.SearchRunsFromPowerPlatformApiAsync(
                environmentId,
                flow.WorkflowId,
                request.StartUtc,
                request.EndUtc,
                request.Criteria,
                cancellationToken);

            var triggerKeys = new SortedSet<string>(AttributeNameComparer.Instance);
            foreach (var run in runs)
            {
                _runs.Add(run);
                foreach (var triggerKey in run.TriggerInputs.Keys)
                {
                    triggerKeys.Add(triggerKey);
                }
            }

            if (triggerKeys.Count > 0)
            {
                SetTriggerColumnOptions(flow, triggerKeys);
            }
            else
            {
                RestoreTriggerColumnOptions(flow);
            }

            RefreshRunsButton.IsEnabled = true;
            _logger.Info($"Advanced search finished. FlowId={flow.WorkflowId}; ResultCount={_runs.Count}; TriggerKeys={triggerKeys.Count}.");
            SetStatus($"Found {_runs.Count} runs for {flow.Name}.");
        });
    }

    private void CacheAdvancedSearchState(Guid workflowId, AdvancedSearchRequest request)
    {
        var state = new AdvancedSearchState
        {
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc
        };

        foreach (var criterion in request.Criteria)
        {
            state.Criteria[criterion.Key] = criterion.Value;
        }

        _advancedSearchStateByFlowId[workflowId] = state;
    }

    private void ResetRunColumns()
    {
        while (RunsDataGrid.Columns.Count > FixedRunColumnCount)
        {
            RunsDataGrid.Columns.RemoveAt(RunsDataGrid.Columns.Count - 1);
        }
    }

    private void ResetTriggerColumnOptions(bool clearKnownKeys)
    {
        _triggerColumnOptions.Clear();
        if (clearKnownKeys)
        {
            _knownTriggerKeys.Clear();
        }

        TriggerColumnsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = _knownTriggerKeys.Count > 0;
        TriggerColumnsPopup.IsOpen = false;
    }

    private void SetTriggerColumnOptions(CloudFlow flow, IEnumerable<string> triggerKeys)
    {
        _triggerColumnOptions.Clear();
        foreach (var triggerKey in triggerKeys)
        {
            _knownTriggerKeys.Add(triggerKey);
        }

        RestoreTriggerColumnOptions(flow);
    }

    private void RestoreTriggerColumnOptions(CloudFlow flow)
    {
        _triggerColumnOptions.Clear();
        var flowId = flow.WorkflowId.ToString("D");
        var selectedColumns = _settings.SelectedTriggerColumnsByFlowId.TryGetValue(flowId, out var cachedColumns)
            ? new HashSet<string>(cachedColumns, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _knownTriggerKeys.OrderBy(key => key, AttributeNameComparer.Instance))
        {
            _triggerColumnOptions.Add(new TriggerColumnOption(key)
            {
                IsSelected = selectedColumns.Contains(key)
            });
        }

        TriggerColumnsButton.IsEnabled = _triggerColumnOptions.Count > 0;
        AdvancedSearchButton.IsEnabled = _triggerColumnOptions.Count > 0;
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
                Width = DataGridLength.Auto
            });
        }
    }

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
            RefreshRunsButton.IsEnabled = _client is not null && FlowComboBox.SelectedItem is CloudFlow;
            AdvancedSearchButton.IsEnabled = _triggerColumnOptions.Count > 0;
        }
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
        if (!Enum.IsDefined(_settings.LogVerbosity))
        {
            _settings.LogVerbosity = LogVerbosity.Info;
        }
    }

    private static string FormatCriteria(IReadOnlyDictionary<string, string> criteria)
    {
        return criteria.Count == 0
            ? "<none>"
            : string.Join("; ", criteria.Select(criterion => $"{criterion.Key}={criterion.Value}"));
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

internal sealed class AttributeNameComparer : IComparer<string>
{
    public static readonly AttributeNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var normalizedCompare = StringComparer.OrdinalIgnoreCase.Compare(Normalize(x), Normalize(y));
        return normalizedCompare != 0
            ? normalizedCompare
            : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).TrimStart('_');
    }
}
