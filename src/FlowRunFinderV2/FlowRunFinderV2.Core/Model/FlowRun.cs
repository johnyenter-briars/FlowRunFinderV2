namespace FlowRunFinderV2.Core.Model;

public sealed class FlowRun
{
    public string? RunId { get; init; }
    public string? Name { get; init; }
    public string? RunUrl { get; init; }
    public string? Status { get; init; }
    public DateTimeOffset? StartedOn { get; init; }
    public DateTimeOffset? EndedOn { get; init; }
    public Dictionary<string, string> TriggerInputs { get; } = new(StringComparer.OrdinalIgnoreCase);
}
