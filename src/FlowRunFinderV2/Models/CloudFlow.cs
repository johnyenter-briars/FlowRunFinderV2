namespace FlowRunFinderV2.Models;

public sealed class CloudFlow
{
    public required Guid WorkflowId { get; init; }
    public required string Name { get; init; }
    public string? UniqueName { get; init; }
    public DateTimeOffset? ModifiedOn { get; init; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(UniqueName) ? Name : $"{Name} ({UniqueName})";
    }
}
