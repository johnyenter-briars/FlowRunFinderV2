namespace FlowRunFinderV2.UI.Infrastructure;

internal sealed class AttributeNameComparer : IComparer<string>
{
    public static readonly AttributeNameComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        var normalizedCompare = StringComparer.OrdinalIgnoreCase.Compare(Normalize(x), Normalize(y));
        return normalizedCompare != 0
            ? normalizedCompare
            : StringComparer.OrdinalIgnoreCase.Compare(x, y);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).TrimStart('_');
    }
}
