namespace FlowRunFinderV2.UI.Model;

public sealed class TriggerColumnOption
{
    public TriggerColumnOption(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public bool IsSelected { get; set; }
}
