using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace FlowRunFinderV2;

public sealed partial class AdvancedSearchDialog : Window
{
    private readonly ObservableCollection<AdvancedSearchField> _fields;
    private readonly ObservableCollection<AdvancedSearchField> _selectedFields = new();
    private ItemsControl _fieldsItemsControl = null!;
    private ItemsControl _criteriaItemsControl = null!;
    private TextBox _startUtcTextBox = null!;
    private TextBox _endUtcTextBox = null!;
    private TextBlock _validationTextBlock = null!;

    public AdvancedSearchDialog(IEnumerable<string> triggerFieldNames)
    {
        InitializeComponent();

        _fields = new ObservableCollection<AdvancedSearchField>(
            triggerFieldNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(name => new AdvancedSearchField(name)));

        _fieldsItemsControl.ItemsSource = _fields;
        _criteriaItemsControl.ItemsSource = _selectedFields;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _fieldsItemsControl = this.FindControl<ItemsControl>("FieldsItemsControl")
            ?? throw new InvalidOperationException("FieldsItemsControl was not found.");
        _criteriaItemsControl = this.FindControl<ItemsControl>("CriteriaItemsControl")
            ?? throw new InvalidOperationException("CriteriaItemsControl was not found.");
        _startUtcTextBox = this.FindControl<TextBox>("StartUtcTextBox")
            ?? throw new InvalidOperationException("StartUtcTextBox was not found.");
        _endUtcTextBox = this.FindControl<TextBox>("EndUtcTextBox")
            ?? throw new InvalidOperationException("EndUtcTextBox was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
    }

    private void OnFieldSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AdvancedSearchField field } checkBox)
        {
            field.IsSelected = checkBox.IsChecked == true;
        }

        RebuildSelectedFields();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnSearchClicked(object? sender, RoutedEventArgs e)
    {
        _validationTextBlock.Text = string.Empty;

        if (!TryParseUtc(_startUtcTextBox.Text, out var startUtc))
        {
            _validationTextBlock.Text = "Enter a valid Start UTC value.";
            return;
        }

        if (!TryParseUtc(_endUtcTextBox.Text, out var endUtc))
        {
            _validationTextBlock.Text = "Enter a valid End UTC value.";
            return;
        }

        if (endUtc < startUtc)
        {
            _validationTextBlock.Text = "End UTC must be greater than or equal to Start UTC.";
            return;
        }

        var criteria = _selectedFields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .ToDictionary(
                field => field.Name,
                field => field.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        Close(new AdvancedSearchRequest(startUtc, endUtc, criteria));
    }

    private void RebuildSelectedFields()
    {
        var existingValues = _selectedFields.ToDictionary(
            field => field.Name,
            field => field.Value,
            StringComparer.OrdinalIgnoreCase);

        _selectedFields.Clear();
        foreach (var field in _fields.Where(field => field.IsSelected))
        {
            if (existingValues.TryGetValue(field.Name, out var value))
            {
                field.Value = value;
            }

            _selectedFields.Add(field);
        }
    }

    private static bool TryParseUtc(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(
                text.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return false;
        }

        value = parsed.ToUniversalTime();
        return true;
    }
}

public sealed class AdvancedSearchField
{
    public AdvancedSearchField(string name)
    {
        Name = name;
    }

    public string Name { get; }
    public bool IsSelected { get; set; }
    public string Value { get; set; } = string.Empty;
}

public sealed record AdvancedSearchRequest(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyDictionary<string, string> Criteria);
