namespace FlowRunFinderV2.Core.Query;

public sealed class LatestFlowRunsRequest
{
    public LatestFlowRunsRequest(
        string environmentId,
        Guid flowId,
        int top,
        bool useDataverseHistory)
    {
        EnvironmentId = environmentId;
        FlowId = flowId;
        Top = top;
        UseDataverseHistory = useDataverseHistory;
    }

    public string EnvironmentId { get; }
    public Guid FlowId { get; }
    public int Top { get; }
    public bool UseDataverseHistory { get; }
}

public sealed class FlowRunSearchRequest
{
    public FlowRunSearchRequest(
        string environmentId,
        Guid flowId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AdvancedSearchGroup filter,
        int maxRunsToQuery,
        bool useDataverseHistory)
    {
        EnvironmentId = environmentId;
        FlowId = flowId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Filter = filter;
        MaxRunsToQuery = maxRunsToQuery;
        UseDataverseHistory = useDataverseHistory;
    }

    public string EnvironmentId { get; }
    public Guid FlowId { get; }
    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
    public AdvancedSearchGroup Filter { get; }
    public int MaxRunsToQuery { get; }
    public bool UseDataverseHistory { get; }
}
