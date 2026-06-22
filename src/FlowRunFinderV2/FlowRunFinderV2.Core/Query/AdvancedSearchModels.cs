using System.Collections.ObjectModel;

namespace FlowRunFinderV2.Core.Query;

public enum AdvancedSearchLogicalOperator
{
    And,
    Or
}

public enum AdvancedSearchComparisonOperator
{
    Equals,
    Contains
}

public abstract class AdvancedSearchFilterNode
{
    public abstract AdvancedSearchFilterNode Clone();
}

public sealed class AdvancedSearchGroup : AdvancedSearchFilterNode
{
    public AdvancedSearchLogicalOperator LogicalOperator { get; set; } = AdvancedSearchLogicalOperator.And;
    public ObservableCollection<AdvancedSearchFilterNode> Children { get; } = new();

    public override AdvancedSearchFilterNode Clone()
    {
        var clone = new AdvancedSearchGroup
        {
            LogicalOperator = LogicalOperator
        };

        foreach (var child in Children)
        {
            clone.Children.Add(child.Clone());
        }

        return clone;
    }
}

public sealed class AdvancedSearchCondition : AdvancedSearchFilterNode
{
    public string FieldName { get; set; } = string.Empty;
    public AdvancedSearchComparisonOperator Operator { get; set; } = AdvancedSearchComparisonOperator.Equals;
    public string Value { get; set; } = string.Empty;

    public override AdvancedSearchFilterNode Clone()
    {
        return new AdvancedSearchCondition
        {
            FieldName = FieldName,
            Operator = Operator,
            Value = Value
        };
    }
}

public sealed class AdvancedSearchRequest
{
    public AdvancedSearchRequest(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        AdvancedSearchGroup filter)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
        Filter = filter;
    }

    public DateTimeOffset StartUtc { get; }
    public DateTimeOffset EndUtc { get; }
    public AdvancedSearchGroup Filter { get; }
}

public sealed class AdvancedSearchState
{
    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public AdvancedSearchGroup Filter { get; set; } = new();
}
