using System.Globalization;
using Avalonia.Data.Converters;

namespace FlowRunFinderV2.UI.Converters;

internal sealed class TriggerInputValueConverter : IValueConverter
{
    public static readonly TriggerInputValueConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Dictionary<string, string> triggerInputs &&
            parameter is string key &&
            triggerInputs.TryGetValue(key, out var triggerValue))
        {
            return triggerValue;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
