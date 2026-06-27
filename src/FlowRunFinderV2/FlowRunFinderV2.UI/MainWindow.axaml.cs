using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Configuration;
using FlowRunFinderV2.Core.Logging;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Query;

namespace FlowRunFinderV2.UI;

public sealed partial class MainWindow : Window
{
    private const int FixedRunColumnCount = 4;

    private readonly AppDataStore _appDataStore = new();
    private readonly SettingsManager _settingsManager;
    private readonly AppLogger _logger;
    private readonly ObservableCollection<CloudFlow> _flows = new();
    private readonly ObservableCollection<CloudFlow> _filteredFlows = new();
    private readonly ObservableCollection<FlowRun> _runs = new();
    private readonly ObservableCollection<TriggerColumnOption> _triggerColumnOptions = new();
    private readonly ObservableCollection<TriggerColumnOption> _filteredTriggerColumnOptions = new();
    private readonly SortedSet<string> _knownTriggerKeys = new(AttributeNameComparer.Instance);
    private readonly Dictionary<Guid, AdvancedSearchState> _advancedSearchStateByFlowId = new();
    private AppSettings _settings = new();
    private ConnectionProfile? _currentConnection;
    private DataverseAuthService? _authService;
    private PowerAutomateAuthService? _powerAutomateAuthService;
    private DataverseClient? _client;
    private CloudFlow? _selectedFlow;
    private Uri? _environmentUrl;
    private string? _deviceVerificationUrl;
    private string? _deviceUserCode;
    private int _busyDepth;
    private CancellationTokenSource? _toastCancellationTokenSource;

    public MainWindow()
    {
        InitializeComponent();
        this.ApplyAppIcon();
        _settingsManager = new SettingsManager(_appDataStore.AppDataFolder);
        _logger = new AppLogger(_appDataStore.LogsFolder);

        FlowListBox.ItemsSource = _filteredFlows;
        RunsDataGrid.ItemsSource = _runs;
        TriggerColumnsItemsControl.ItemsSource = _filteredTriggerColumnOptions;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            _settings = await _settingsManager.LoadAsync();
            NormalizeSettings();
            await _settingsManager.SaveAsync(_settings);
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

        await _settingsManager.UpdateAsync(settings =>
        {
            settings.DefaultRunCount = result.DefaultRunCount;
            settings.MaxRunsToQuery = result.MaxRunsToQuery;
            settings.UseFlowRunHistoryTable = result.UseFlowRunHistoryTable;
            settings.LogVerbosity = result.LogVerbosity;
        });
        _settings = _settingsManager.Current;
        _logger.SetVerbosity(_settings.LogVerbosity);
        _logger.Info($"Settings saved. DefaultRunCount={_settings.DefaultRunCount}; MaxRunsToQuery={_settings.MaxRunsToQuery}; UseFlowRunHistoryTable={_settings.UseFlowRunHistoryTable}; LogVerbosity={_settings.LogVerbosity}.");
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
            var tokenCacheOptions = new TokenCacheOptions(_appDataStore.GetConnectionFolder(connection.Id));
            _authService = new DataverseAuthService(tokenCacheOptions);
            _powerAutomateAuthService = new PowerAutomateAuthService(tokenCacheOptions);

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

    private async Task LoadSelectedFlowRunsAsync()
    {
        await RunUiActionAsync(async cancellationToken =>
        {
            if (_environmentUrl is null || _selectedFlow is not { } flow)
            {
                return;
            }

            RefreshRunsButton.IsEnabled = false;
            _runs.Clear();
            ResetRunColumns();
            ResetTriggerColumnOptions(clearKnownKeys: true);
            var sourceDescription = _settings.UseFlowRunHistoryTable
                ? "Dataverse flow run history table"
                : "Power Platform API";
            SetStatus($"Loading latest {_settings.DefaultRunCount} runs for {flow.Name}...");
            _logger.Info($"Loading latest runs. FlowId={flow.WorkflowId}; FlowName={flow.Name}; Top={_settings.DefaultRunCount}; Source={sourceDescription}.");

            if (_powerAutomateAuthService is null)
            {
                return;
            }

            var paToken = await _powerAutomateAuthService.GetTokenAsync(
                ShowDeviceCodePrompt,
                cancellationToken);
            ClearDeviceCodePrompt();

            using var paClient = new PowerAutomateClient(paToken.AccessToken, _logger);
            var queryEngine = new FlowRunQueryEngine(paClient, _logger);
            var environmentId = await paClient.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            IReadOnlyList<FlowRun> runs;
            if (_settings.UseFlowRunHistoryTable)
            {
                if (_client is null)
                {
                    return;
                }

                runs = await _client.GetLatestFlowRunsAsync(
                    flow.WorkflowId,
                    _settings.DefaultRunCount,
                    cancellationToken);

                foreach (var run in runs)
                {
                    run.RunUrl = BuildRunUrl(environmentId, flow.WorkflowId, run.Name);
                    await paClient.LoadTriggerOutputsAsync(environmentId, flow.WorkflowId, run, cancellationToken);
                }
            }
            else
            {
                runs = await queryEngine.GetLatestRunsAsync(
                    environmentId,
                    flow.WorkflowId,
                    _settings.DefaultRunCount,
                    cancellationToken);
            }

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
            var sourceDescription = _settings.UseFlowRunHistoryTable
                ? "Dataverse flow run history table"
                : "Power Platform API";
            SetStatus($"Searching runs for {flow.Name}...");
            _logger.Info($"Advanced search started. FlowId={flow.WorkflowId}; FlowName={flow.Name}; StartUtc={request.StartUtc:O}; EndUtc={request.EndUtc:O}; Filter={FormatFilter(request.Filter)}; Source={sourceDescription}.");

            if (_powerAutomateAuthService is null)
            {
                return;
            }

            var paToken = await _powerAutomateAuthService.GetTokenAsync(
                ShowDeviceCodePrompt,
                cancellationToken);
            ClearDeviceCodePrompt();

            using var paClient = new PowerAutomateClient(paToken.AccessToken, _logger);
            var queryEngine = new FlowRunQueryEngine(paClient, _logger);
            var environmentId = await paClient.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment for advanced search. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            IReadOnlyList<FlowRun> runs;
            if (_settings.UseFlowRunHistoryTable)
            {
                if (_client is null)
                {
                    return;
                }

                var candidateRuns = await _client.SearchFlowRunsAsync(
                    flow.WorkflowId,
                    request.StartUtc,
                    request.EndUtc,
                    _settings.MaxRunsToQuery,
                    cancellationToken);

                var matchedRuns = new List<FlowRun>();
                foreach (var run in candidateRuns)
                {
                    run.RunUrl = BuildRunUrl(environmentId, flow.WorkflowId, run.Name);
                    await paClient.LoadTriggerOutputsAsync(environmentId, flow.WorkflowId, run, cancellationToken);

                    if (FlowRunQueryEngine.MatchesFilter(run, request.Filter))
                    {
                        matchedRuns.Add(run);
                    }
                }

                runs = matchedRuns;
            }
            else
            {
                runs = await queryEngine.SearchRunsAsync(
                    environmentId,
                    flow.WorkflowId,
                    request.StartUtc,
                    request.EndUtc,
                    request.Filter,
                    _settings.MaxRunsToQuery,
                    cancellationToken);
            }

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

        state.Filter = request.Filter.Clone() as AdvancedSearchGroup ?? new AdvancedSearchGroup();

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
        _filteredTriggerColumnOptions.Clear();
        if (clearKnownKeys)
        {
            _knownTriggerKeys.Clear();
        }

        TriggerColumnsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = _knownTriggerKeys.Count > 0;
        TriggerColumnsPopup.IsOpen = false;
    }

    private void ClearSelectedFlow()
    {
        _selectedFlow = null;
        FlowPickerButton.Content = "Select a flow";
        FlowPickerPopup.IsOpen = false;
        FlowSearchTextBox.Text = string.Empty;
        FlowListBox.SelectedItem = null;
        _filteredFlows.Clear();
    }

    private void RefreshFilteredFlows()
    {
        var searchText = FlowSearchTextBox.Text?.Trim();
        _filteredFlows.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _flows
            : _flows.Where(flow =>
                flow.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                flow.WorkflowId.ToString("D").Contains(searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var flow in filtered.OrderBy(flow => flow.Name, StringComparer.OrdinalIgnoreCase))
        {
            _filteredFlows.Add(flow);
        }
    }

    private void RefreshFilteredTriggerColumnOptions()
    {
        var searchText = TriggerColumnsSearchTextBox.Text?.Trim();
        _filteredTriggerColumnOptions.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _triggerColumnOptions
            : _triggerColumnOptions.Where(option =>
                option.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var option in filtered.OrderBy(option => option.Name, AttributeNameComparer.Instance))
        {
            _filteredTriggerColumnOptions.Add(option);
        }
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
        _filteredTriggerColumnOptions.Clear();
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

        RefreshFilteredTriggerColumnOptions();
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

        if (!saveSelection || _selectedFlow is not { } flow)
        {
            return;
        }

        await _settingsManager.UpdateAsync(settings =>
        {
            var flowId = flow.WorkflowId.ToString("D");
            if (selectedColumns.Count == 0)
            {
                settings.SelectedTriggerColumnsByFlowId.Remove(flowId);
            }
            else
            {
                settings.SelectedTriggerColumnsByFlowId[flowId] = selectedColumns;
            }
        });

        _settings = _settingsManager.Current;
    }

    private void AddTriggerColumns(IEnumerable<string> triggerKeys)
    {
        ResetRunColumns();

        foreach (var key in triggerKeys)
        {
            RunsDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = key,
                CellTemplate = CreateTriggerInputCellTemplate(key),
                Width = DataGridLength.Auto
            });
        }
    }

    private IDataTemplate CreateTriggerInputCellTemplate(string key)
    {
        return new FuncDataTemplate<FlowRun>((_, _) =>
        {
            var textBlock = new TextBlock();
            textBlock.Bind(
                TextBlock.TextProperty,
                new Binding(nameof(FlowRun.TriggerInputs))
                {
                    Mode = BindingMode.OneWay,
                    Converter = TriggerInputValueConverter.Instance,
                    ConverterParameter = key
                });
            textBlock.PointerPressed += OnCopyCellPointerPressed;
            return textBlock;
        });
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
            RefreshRunsButton.IsEnabled = _client is not null && _selectedFlow is not null;
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
        _settings.MaxRunsToQuery = Math.Max(_settings.MaxRunsToQuery, 1);
        if (!Enum.IsDefined(_settings.LogVerbosity))
        {
            _settings.LogVerbosity = LogVerbosity.Info;
        }
    }

    private static string FormatFilter(AdvancedSearchGroup filter)
    {
        if (filter.Children.Count == 0)
        {
            return "<none>";
        }

        var parts = filter.Children.Select(child => child switch
        {
            AdvancedSearchCondition condition => $"{condition.FieldName} {condition.Operator} {condition.Value}",
            AdvancedSearchGroup group => $"({FormatFilter(group)})",
            _ => child.GetType().Name
        });

        return string.Join($" {filter.LogicalOperator.ToString().ToUpperInvariant()} ", parts);
    }

    private static string? BuildRunUrl(string environmentId, Guid flowId, string? runName)
    {
        return string.IsNullOrWhiteSpace(runName)
            ? null
            : $"https://make.powerautomate.com/environments/{environmentId}/flows/{flowId:D}/runs/{runName}";
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
