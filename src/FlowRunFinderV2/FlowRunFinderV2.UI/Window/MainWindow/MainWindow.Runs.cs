using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.Core.Query;
using FlowRunFinderV2.UI.Infrastructure;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
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

            using var queryEngine = new FlowRunQueryEngine(paToken.AccessToken, _client, _logger);
            var environmentId = await queryEngine.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            var runs = await queryEngine.GetLatestRunsAsync(
                new LatestFlowRunsRequest(
                    environmentId,
                    flow.WorkflowId,
                    _settings.DefaultRunCount,
                    _settings.UseFlowRunHistoryTable),
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

            using var queryEngine = new FlowRunQueryEngine(paToken.AccessToken, _client, _logger);
            var environmentId = await queryEngine.DetectEnvironmentIdAsync(_environmentUrl, cancellationToken);
            _logger.Debug($"Detected Power Automate environment for advanced search. EnvironmentId={environmentId ?? "<null>"}.");
            if (string.IsNullOrWhiteSpace(environmentId))
            {
                SetStatus("Could not detect the matching Power Automate environment id.");
                return;
            }

            var runs = await queryEngine.SearchRunsAsync(
                new FlowRunSearchRequest(
                    environmentId,
                    flow.WorkflowId,
                    request.StartUtc,
                    request.EndUtc,
                    request.Filter,
                    _settings.MaxRunsToQuery,
                    _settings.UseFlowRunHistoryTable),
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
        }, canCancel: true);
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
}
