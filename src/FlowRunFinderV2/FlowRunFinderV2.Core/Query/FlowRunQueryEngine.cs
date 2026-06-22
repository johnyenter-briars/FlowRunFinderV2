using FlowRunFinderV2.Core.Client;
using FlowRunFinderV2.Core.Logging;
using FlowRunFinderV2.Core.Model;

namespace FlowRunFinderV2.Core.Query;

public sealed class FlowRunQueryEngine
{
    private const int AdvancedSearchRunLimit = 1000;

    private readonly PowerAutomateClient _client;
    private readonly AppLogger? _logger;

    public FlowRunQueryEngine(PowerAutomateClient client, AppLogger? logger = null)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FlowRun>> GetLatestRunsAsync(
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

    public async Task<IReadOnlyList<FlowRun>> SearchRunsAsync(
        string environmentId,
        Guid flowId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AdvancedSearchGroup filter,
        CancellationToken cancellationToken)
    {
        var page = 0;
        var inspected = 0;
        var reachedOlderThanStart = false;
        var nextPage = await _client.GetRunsPageAsync(environmentId, flowId, cancellationToken).ConfigureAwait(false);
        var result = new List<FlowRun>();

        _logger?.Info($"Advanced search query started. FlowId={flowId}; StartUtc={startUtc:O}; EndUtc={endUtc:O}; Filter={FormatFilter(filter)}; Limit={AdvancedSearchRunLimit}.");

        while (inspected < AdvancedSearchRunLimit && !reachedOlderThanStart)
        {
            page++;
            var pageRows = 0;

            foreach (var run in nextPage.Runs)
            {
                pageRows++;
                inspected++;
                if (inspected > AdvancedSearchRunLimit)
                {
                    _logger?.Info($"Advanced search inspection limit reached. FlowId={flowId}; Limit={AdvancedSearchRunLimit}.");
                    break;
                }

                var flowRun = _client.CreateFlowRun(environmentId, flowId, run);
                if (flowRun.StartedOn is null)
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
                actualValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
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
}
