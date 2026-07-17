namespace FlowRunFinderV2.Core.Model;

public sealed class FlowRun
{
    public string? RunId { get; set; }
    public string? Name { get; set; }
    public string? RunUrl { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? StartedOn { get; set; }
    public DateTimeOffset? EndedOn { get; set; }
    public Dictionary<string, string> TriggerInputs { get; } = new(StringComparer.OrdinalIgnoreCase);
}
