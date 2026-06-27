using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Logging;
using FlowRunFinderV2.Core.Model;

namespace FlowRunFinderV2.Core.Query;

public sealed class FlowRunQueryEngine : IDisposable
{
    private readonly PowerAutomateClient _client;
    private readonly DataverseClient? _dataverseClient;
    private readonly AppLogger? _logger;
    private readonly bool _ownsPowerAutomateClient;

    public FlowRunQueryEngine(PowerAutomateClient client, AppLogger? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public FlowRunQueryEngine(
        string powerAutomateAccessToken,
        DataverseClient? dataverseClient = null,
        AppLogger? logger = null)
    {
        _client = new PowerAutomateClient(powerAutomateAccessToken, logger);
        _dataverseClient = dataverseClient;
        _logger = logger;
        _ownsPowerAutomateClient = true;
    }

    public async Task<IReadOnlyList<FlowRun>> GetLatestRunsAsync(
        LatestFlowRunsRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return request.UseDataverseHistory
            ? await GetLatestRunsFromDataverseHistoryAsync(
                    RequireDataverseClient(),
                    request.EnvironmentId,
                    request.FlowId,
                    request.Top,
                    cancellationToken)
                .ConfigureAwait(false)
            : await GetLatestRunsFromPowerAutomateAsync(
                    request.EnvironmentId,
                    request.FlowId,
                    request.Top,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FlowRun>> SearchRunsAsync(
        FlowRunSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        return request.UseDataverseHistory
            ? await SearchRunsFromDataverseHistoryAsync(
                    RequireDataverseClient(),
                    request.EnvironmentId,
                    request.FlowId,
                    request.StartUtc,
                    request.EndUtc,
                    request.Filter,
                    request.MaxRunsToQuery,
                    cancellationToken)
                .ConfigureAwait(false)
            : await SearchRunsFromPowerAutomateAsync(
                    request.EnvironmentId,
                    request.FlowId,
                    request.StartUtc,
                    request.EndUtc,
                    request.Filter,
                    request.MaxRunsToQuery,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    public async Task<string?> DetectEnvironmentIdAsync(Uri environmentUrl, CancellationToken cancellationToken)
    {
        return await _client.DetectEnvironmentIdAsync(environmentUrl, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<FlowRun>> GetLatestRunsFromPowerAutomateAsync(
        string environmentId,
        Guid flowId,
        int top,
        CancellationToken cancellationToken)
    {
        _logger?.Debug($"Latest run query started. FlowId={flowId}; Top={top}.");
        var page = await _client.GetRunsPageAsync(environmentId, flowId, cancellationToken).ConfigureAwait(false);
        var result = new List<FlowRun>();

        foreach (var run in page.Runs.Take(top))
        {
            var flowRun = _client.CreateFlowRun(environmentId, flowId, run);
            await _client.LoadTriggerOutputsAsync(environmentId, flowId, run, flowRun, cancellationToken)
                .ConfigureAwait(false);
            _logger?.Trace($"Latest run loaded. RunName={flowRun.Name}; Status={flowRun.Status}; Started={flowRun.StartedOn:O}; TriggerKeys={flowRun.TriggerInputs.Count}.");
            result.Add(flowRun);
        }

        _logger?.Debug($"Latest run query finished. FlowId={flowId}; Runs={result.Count}.");
        return result;
    }

    private async Task<IReadOnlyList<FlowRun>> GetLatestRunsFromDataverseHistoryAsync(
        DataverseClient dataverseClient,
        string environmentId,
        Guid flowId,
        int top,
        CancellationToken cancellationToken)
    {
        if (dataverseClient is null)
        {
            throw new ArgumentNullException(nameof(dataverseClient));
        }

        _logger?.Debug($"Dataverse latest run query started. FlowId={flowId}; Top={top}.");
        var runs = await dataverseClient.GetLatestFlowRunsAsync(flowId, top, cancellationToken)
            .ConfigureAwait(false);

        foreach (var run in runs)
        {
            run.RunUrl = BuildRunUrl(environmentId, flowId, run.Name);
            _logger?.Debug($"Loading trigger inputs for Dataverse run. FlowId={flowId}; RunName={run.Name ?? "<null>"}; StartedUtc={run.StartedOn?.ToString("O") ?? "<null>"}; Status={run.Status ?? "<null>"}.");
            await _client.LoadTriggerOutputsAsync(environmentId, flowId, run, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger?.Debug($"Dataverse latest run query finished. FlowId={flowId}; Runs={runs.Count}.");
        return runs;
    }

    private async Task<IReadOnlyList<FlowRun>> SearchRunsFromPowerAutomateAsync(
        string environmentId,
        Guid flowId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AdvancedSearchGroup filter,
        int maxRunsToQuery,
        CancellationToken cancellationToken)
    {
        if (maxRunsToQuery < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRunsToQuery), "Max runs to query must be at least 1.");
        }

        var page = 0;
        var inspected = 0;
        var reachedOlderThanStart = false;
        var nextPage = await _client.GetRunsPageAsync(environmentId, flowId, cancellationToken).ConfigureAwait(false);
        var result = new List<FlowRun>();

        _logger?.Info($"Advanced search query started. FlowId={flowId}; StartUtc={startUtc:O}; EndUtc={endUtc:O}; Filter={FormatFilter(filter)}; Limit={maxRunsToQuery}.");

        while (inspected < maxRunsToQuery && !reachedOlderThanStart)
        {
            page++;
            var pageRows = 0;

            foreach (var run in nextPage.Runs)
            {
                pageRows++;
                inspected++;
                if (inspected > maxRunsToQuery)
                {
                    _logger?.Info($"Advanced search inspection limit reached. FlowId={flowId}; Limit={maxRunsToQuery}.");
                    break;
                }

                var flowRun = _client.CreateFlowRun(environmentId, flowId, run);
                if (flowRun.StartedOn == null)
                {
                    _logger?.Debug($"Advanced search skipped run with no start time. FlowId={flowId}; RunName={flowRun.Name}; RunId={flowRun.RunId}.");
                    continue;
                }

                var startedUtc = flowRun.StartedOn.Value.ToUniversalTime();
                if (startedUtc > endUtc)
                {
                    _logger?.Trace($"Advanced search skipped run newer than end UTC. FlowId={flowId}; RunName={flowRun.Name}; StartedUtc={startedUtc:O}; EndUtc={endUtc:O}.");
                    continue;
                }

                if (startedUtc < startUtc)
                {
                    reachedOlderThanStart = true;
                    _logger?.Info($"Advanced search reached run older than start UTC; stopping scan. FlowId={flowId}; RunName={flowRun.Name}; StartedUtc={startedUtc:O}; StartUtc={startUtc:O}; Inspected={inspected}; Matches={result.Count}.");
                    break;
                }

                await _client.LoadTriggerOutputsAsync(environmentId, flowId, run, flowRun, cancellationToken)
                    .ConfigureAwait(false);

                if (MatchesFilter(flowRun, filter, out var criteriaDiagnostic))
                {
                    result.Add(flowRun);
                    _logger?.Debug($"Advanced search matched run. FlowId={flowId}; RunName={flowRun.Name}; StartedUtc={startedUtc:O}; TriggerKeys={flowRun.TriggerInputs.Count}.");
                }
                else
                {
                    _logger?.Debug($"Advanced search rejected run by criteria. FlowId={flowId}; RunName={flowRun.Name}; StartedUtc={startedUtc:O}; Reason={criteriaDiagnostic}; TriggerKeys={flowRun.TriggerInputs.Count}.");
                }
            }

            _logger?.Debug($"Advanced search page processed. FlowId={flowId}; Page={page}; PageRows={pageRows}; Inspected={inspected}; Matches={result.Count}; HasNext={!string.IsNullOrWhiteSpace(nextPage.NextLink)}.");

            if (string.IsNullOrWhiteSpace(nextPage.NextLink))
            {
                break;
            }

            nextPage = await _client.GetRunsPageAsync(nextPage.NextLink, cancellationToken).ConfigureAwait(false);
        }

        _logger?.Info($"Advanced search query finished. FlowId={flowId}; Inspected={inspected}; Matches={result.Count}; StoppedOlderThanStart={reachedOlderThanStart}.");
        return result;
    }

    private async Task<IReadOnlyList<FlowRun>> SearchRunsFromDataverseHistoryAsync(
        DataverseClient dataverseClient,
        string environmentId,
        Guid flowId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AdvancedSearchGroup filter,
        int maxRunsToQuery,
        CancellationToken cancellationToken)
    {
        if (dataverseClient is null)
        {
            throw new ArgumentNullException(nameof(dataverseClient));
        }

        if (maxRunsToQuery < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRunsToQuery), "Max runs to query must be at least 1.");
        }

        _logger?.Info($"Dataverse advanced search query started. FlowId={flowId}; StartUtc={startUtc:O}; EndUtc={endUtc:O}; Filter={FormatFilter(filter)}; Limit={maxRunsToQuery}.");
        var candidateRuns = await dataverseClient.SearchFlowRunsAsync(
                flowId,
                startUtc,
                endUtc,
                maxRunsToQuery,
                cancellationToken)
            .ConfigureAwait(false);

        _logger?.Info($"Dataverse advanced search returned candidate runs. FlowId={flowId}; StartUtc={startUtc:O}; EndUtc={endUtc:O}; CandidateCount={candidateRuns.Count}; Limit={maxRunsToQuery}.");

        var result = new List<FlowRun>();
        foreach (var run in candidateRuns)
        {
            run.RunUrl = BuildRunUrl(environmentId, flowId, run.Name);
            _logger?.Debug($"Loading trigger inputs for Dataverse run candidate. FlowId={flowId}; RunName={run.Name ?? "<null>"}; StartedUtc={run.StartedOn?.ToString("O") ?? "<null>"}; Status={run.Status ?? "<null>"}.");
            await _client.LoadTriggerOutputsAsync(environmentId, flowId, run, cancellationToken)
                .ConfigureAwait(false);

            if (MatchesFilter(run, filter, out var criteriaDiagnostic))
            {
                result.Add(run);
                _logger?.Debug($"Dataverse advanced search matched run. FlowId={flowId}; RunName={run.Name}; StartedUtc={FormatDateTimeOffset(run.StartedOn)}; TriggerKeys={run.TriggerInputs.Count}.");
            }
            else
            {
                _logger?.Debug($"Dataverse advanced search rejected run by criteria. FlowId={flowId}; RunName={run.Name}; StartedUtc={FormatDateTimeOffset(run.StartedOn)}; Reason={criteriaDiagnostic}; TriggerKeys={run.TriggerInputs.Count}.");
            }
        }

        _logger?.Info($"Dataverse advanced search query finished. FlowId={flowId}; Candidates={candidateRuns.Count}; Matches={result.Count}.");
        return result;
    }

    private static bool MatchesFilter(
        FlowRun run,
        AdvancedSearchGroup filter,
        out string diagnostic)
    {
        if (filter.Children.Count == 0)
        {
            diagnostic = "NoFilter";
            return true;
        }

        var childDiagnostics = new List<string>();
        foreach (var child in filter.Children)
        {
            bool matched;
            string childDiagnostic;

            switch (child)
            {
                case AdvancedSearchCondition condition:
                    matched = MatchesCondition(run, condition, out childDiagnostic);
                    break;
                case AdvancedSearchGroup group:
                    matched = MatchesFilter(run, group, out childDiagnostic);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported advanced search node: {child.GetType().Name}.");
            }

            childDiagnostics.Add(childDiagnostic);

            if (filter.LogicalOperator == AdvancedSearchLogicalOperator.And && !matched)
            {
                diagnostic = $"AndFailed({childDiagnostic})";
                return false;
            }

            if (filter.LogicalOperator == AdvancedSearchLogicalOperator.Or && matched)
            {
                diagnostic = "OrMatched";
                return true;
            }
        }

        if (filter.LogicalOperator == AdvancedSearchLogicalOperator.And)
        {
            diagnostic = "AndMatched";
            return true;
        }

        diagnostic = $"OrFailed({string.Join(" | ", childDiagnostics)})";
        return false;
    }

    public static bool MatchesFilter(FlowRun run, AdvancedSearchGroup filter)
    {
        return MatchesFilter(run, filter, out _);
    }

    private static bool MatchesCondition(
        FlowRun run,
        AdvancedSearchCondition condition,
        out string diagnostic)
    {
        if (!run.TriggerInputs.TryGetValue(condition.FieldName, out var actualValue))
        {
            diagnostic = $"MissingField:{condition.FieldName}";
            return false;
        }

        var matched = condition.Operator switch
        {
            AdvancedSearchComparisonOperator.Equals =>
                string.Equals(actualValue, condition.Value, StringComparison.OrdinalIgnoreCase),
            AdvancedSearchComparisonOperator.Contains =>
                actualValue.IndexOf(condition.Value, StringComparison.OrdinalIgnoreCase) >= 0,
            _ => false
        };

        diagnostic = matched
            ? $"Matched:{condition.FieldName}"
            : $"ValueMismatch:{condition.FieldName}; Operator={condition.Operator}; Expected={condition.Value}; Actual={actualValue}";
        return matched;
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

    private DataverseClient RequireDataverseClient()
    {
        return _dataverseClient
               ?? throw new InvalidOperationException("Dataverse history queries require a DataverseClient.");
    }

    private static string? BuildRunUrl(string environmentId, Guid flowId, string? runName)
    {
        return string.IsNullOrWhiteSpace(runName)
            ? null
            : $"https://make.powerautomate.com/environments/{environmentId}/flows/{flowId:D}/runs/{runName}";
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.ToUniversalTime().ToString("O")
            : "<null>";
    }

    public void Dispose()
    {
        if (_ownsPowerAutomateClient)
        {
            _client.Dispose();
        }
    }
}
