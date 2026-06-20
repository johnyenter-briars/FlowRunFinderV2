using FlowRunFinderV2.Models;

namespace FlowRunFinderV2.Services;

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
        IReadOnlyDictionary<string, string> criteria,
        CancellationToken cancellationToken)
    {
        var page = 0;
        var inspected = 0;
        var reachedOlderThanStart = false;
        var nextPage = await _client.GetRunsPageAsync(environmentId, flowId, cancellationToken).ConfigureAwait(false);
        var result = new List<FlowRun>();

        _logger?.Info($"Advanced search query started. FlowId={flowId}; StartUtc={startUtc:O}; EndUtc={endUtc:O}; Criteria={FormatCriteria(criteria)}; Limit={AdvancedSearchRunLimit}.");

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

                if (MatchesCriteria(flowRun, criteria, out var criteriaDiagnostic))
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

    private static bool MatchesCriteria(
        FlowRun run,
        IReadOnlyDictionary<string, string> criteria,
        out string diagnostic)
    {
        foreach (var criterion in criteria)
        {
            if (!run.TriggerInputs.TryGetValue(criterion.Key, out var actualValue))
            {
                diagnostic = $"MissingField:{criterion.Key}";
                return false;
            }

            if (!string.Equals(actualValue, criterion.Value, StringComparison.OrdinalIgnoreCase))
            {
                diagnostic = $"ValueMismatch:{criterion.Key}; Expected={criterion.Value}; Actual={actualValue}";
                return false;
            }
        }

        diagnostic = "Matched";
        return true;
    }

    private static string FormatCriteria(IReadOnlyDictionary<string, string> criteria)
    {
        return criteria.Count == 0
            ? "<none>"
            : string.Join("; ", criteria.Select(criterion => $"{criterion.Key}={criterion.Value}"));
    }
}
