using System.Collections.ObjectModel;
using Avalonia.Controls;
using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Configuration;
using FlowRunFinderV2.Core.Logging;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Query;
using FlowRunFinderV2.UI;
using FlowRunFinderV2.UI.Infrastructure;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow : Avalonia.Controls.Window
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
    private FlowRunQuerySession? _activeQuerySession;

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
}
