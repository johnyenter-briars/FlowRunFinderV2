using FlowRunFinderV2.Core.Query;

namespace FlowRunFinderV2.UI.Model;

public sealed record AdvancedSearchOperatorOption(
    string Label,
    AdvancedSearchComparisonOperator Operator)
{
    public override string ToString() => Label;
}
