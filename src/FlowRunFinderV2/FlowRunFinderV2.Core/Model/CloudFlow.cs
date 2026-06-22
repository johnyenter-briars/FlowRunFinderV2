namespace FlowRunFinderV2.Core.Model;

public sealed class CloudFlow
{
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? UniqueName { get; set; }
    public DateTimeOffset? ModifiedOn { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(UniqueName) ? Name : $"{Name} ({UniqueName})";
    }
}
