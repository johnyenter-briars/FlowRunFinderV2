using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FlowRunFinderV2.UI;
using FlowRunFinderV2.Core.Query;
using FlowRunFinderV2.UI.Infrastructure;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Dialog;

public sealed partial class AdvancedSearchDialog : Avalonia.Controls.Window
{
    private static readonly IReadOnlyList<AdvancedSearchOperatorOption> OperatorOptions =
    [
        new("Equals", AdvancedSearchComparisonOperator.Equals),
        new("Contains", AdvancedSearchComparisonOperator.Contains)
    ];

    private readonly IReadOnlyList<string> _fieldNames;
    private readonly AdvancedSearchGroup _rootGroup;
    private StackPanel _filterBuilderPanel = null!;
    private TextBox _startUtcTextBox = null!;
    private TextBox _endUtcTextBox = null!;
    private TextBlock _validationTextBlock = null!;

    public AdvancedSearchDialog(
        IEnumerable<string> triggerFieldNames,
        AdvancedSearchState? initialState = null)
    {
        InitializeComponent();
        this.ApplyAppIcon();

        _fieldNames = triggerFieldNames
            .OrderBy(name => name, AttributeNameComparer.Instance)
            .ToList();

        _rootGroup = initialState?.Filter.Clone() as AdvancedSearchGroup ?? new AdvancedSearchGroup();
        if (_rootGroup.Children.Count == 0)
        {
            _rootGroup.Children.Add(new AdvancedSearchCondition());
        }

        if (initialState is not null)
        {
            _startUtcTextBox.Text = initialState.StartUtc?.ToString("O", CultureInfo.InvariantCulture);
            _endUtcTextBox.Text = initialState.EndUtc?.ToString("O", CultureInfo.InvariantCulture);
        }

        RenderFilterBuilder();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _filterBuilderPanel = this.FindControl<StackPanel>("FilterBuilderPanel")
            ?? throw new InvalidOperationException("FilterBuilderPanel was not found.");
        _startUtcTextBox = this.FindControl<TextBox>("StartUtcTextBox")
            ?? throw new InvalidOperationException("StartUtcTextBox was not found.");
        _endUtcTextBox = this.FindControl<TextBox>("EndUtcTextBox")
            ?? throw new InvalidOperationException("EndUtcTextBox was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
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

        var filter = _rootGroup.Clone() as AdvancedSearchGroup ?? new AdvancedSearchGroup();
        NormalizeGroup(filter);
        if (!TryValidateGroup(filter, out var validationMessage))
        {
            _validationTextBlock.Text = validationMessage;
            return;
        }

        Close(new AdvancedSearchRequest(startUtc, endUtc, filter));
    }

    private void RenderFilterBuilder()
    {
        _filterBuilderPanel.Children.Clear();
        _filterBuilderPanel.Children.Add(CreateGroupControl(_rootGroup, null, 0));
    }

    private Control CreateGroupControl(AdvancedSearchGroup group, AdvancedSearchGroup? parent, int depth)
    {
        var border = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(depth == 0 ? 0 : 18, depth == 0 ? 0 : 8, 0, 8),
            Background = GetBrush("AppPanelAltBrush", "#23272E"),
            BorderBrush = GetBrush("AppBorderBrush", "#343A44"),
            BorderThickness = new Thickness(1)
        };

        var panel = new StackPanel { Spacing = 8 };
        border.Child = panel;

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var logicalOperatorBox = new ComboBox
        {
            Width = 82,
            ItemsSource = Enum.GetValues<AdvancedSearchLogicalOperator>(),
            SelectedItem = group.LogicalOperator
        };
        logicalOperatorBox.SelectionChanged += (_, _) =>
        {
            if (logicalOperatorBox.SelectedItem is AdvancedSearchLogicalOperator selected)
            {
                group.LogicalOperator = selected;
            }
        };
        header.Children.Add(logicalOperatorBox);

        header.Children.Add(new TextBlock
        {
            Text = depth == 0 ? "Root group" : "Nested group",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        });

        if (parent is not null)
        {
            var removeGroupButton = new Button { Content = "Remove group" };
            removeGroupButton.Click += (_, _) =>
            {
                parent.Children.Remove(group);
                RenderFilterBuilder();
            };
            header.Children.Add(removeGroupButton);
        }

        panel.Children.Add(header);

        foreach (var child in group.Children.ToList())
        {
            if (child is AdvancedSearchCondition condition)
            {
                panel.Children.Add(CreateConditionControl(group, condition));
            }
            else if (child is AdvancedSearchGroup childGroup)
            {
                panel.Children.Add(CreateGroupControl(childGroup, group, depth + 1));
            }
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        var addConditionButton = new Button { Content = "Add filter" };
        addConditionButton.Click += (_, _) =>
        {
            group.Children.Add(new AdvancedSearchCondition());
            RenderFilterBuilder();
        };
        actions.Children.Add(addConditionButton);

        var addGroupButton = new Button { Content = "Add group" };
        addGroupButton.Click += (_, _) =>
        {
            var childGroup = new AdvancedSearchGroup();
            childGroup.Children.Add(new AdvancedSearchCondition());
            group.Children.Add(childGroup);
            RenderFilterBuilder();
        };
        actions.Children.Add(addGroupButton);

        panel.Children.Add(actions);
        return border;
    }

    private Control CreateConditionControl(AdvancedSearchGroup parent, AdvancedSearchCondition condition)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("2*,160,3*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var fieldSelector = CreateFieldSelector(condition);
        row.Children.Add(fieldSelector);

        var operatorBox = new ComboBox
        {
            ItemsSource = OperatorOptions,
            SelectedItem = OperatorOptions.First(option => option.Operator == condition.Operator)
        };
        Grid.SetColumn(operatorBox, 1);
        operatorBox.SelectionChanged += (_, _) =>
        {
            if (operatorBox.SelectedItem is AdvancedSearchOperatorOption selected)
            {
                condition.Operator = selected.Operator;
            }
        };
        row.Children.Add(operatorBox);

        var valueTextBox = new TextBox
        {
            Text = condition.Value,
            Watermark = "Value"
        };
        Grid.SetColumn(valueTextBox, 2);
        valueTextBox.TextChanged += (_, _) =>
        {
            condition.Value = valueTextBox.Text ?? string.Empty;
        };
        row.Children.Add(valueTextBox);

        var removeButton = new Button
        {
            Content = "Remove"
        };
        Grid.SetColumn(removeButton, 3);
        removeButton.Click += (_, _) =>
        {
            parent.Children.Remove(condition);
            if (_rootGroup.Children.Count == 0)
            {
                _rootGroup.Children.Add(new AdvancedSearchCondition());
            }

            RenderFilterBuilder();
        };
        row.Children.Add(removeButton);

        return row;
    }

    private Control CreateFieldSelector(AdvancedSearchCondition condition)
    {
        var filteredFieldNames = new ObservableCollection<string>();
        var root = new Grid();
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = string.IsNullOrWhiteSpace(condition.FieldName) ? "Select field" : condition.FieldName
        };

        var searchTextBox = new TextBox
        {
            Watermark = "Search attributes"
        };

        var listBox = new ListBox
        {
            ItemsSource = filteredFieldNames,
            MaxHeight = 300
        };

        var popupBorder = new Border
        {
            Width = 320,
            MaxHeight = 360,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Background = GetBrush("AppPanelAltBrush", "#23272E"),
            BorderBrush = GetBrush("AppBorderBrush", "#343A44"),
            BorderThickness = new Thickness(1),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 8,
                Children =
                {
                    searchTextBox,
                    listBox
                }
            }
        };

        var popup = new Popup
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child = popupBorder
        };

        Grid.SetRow(listBox, 1);

        void RefreshFields()
        {
            var searchText = searchTextBox.Text?.Trim();
            filteredFieldNames.Clear();

            var filtered = string.IsNullOrWhiteSpace(searchText)
                ? _fieldNames
                : _fieldNames.Where(fieldName =>
                    fieldName.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            foreach (var fieldName in filtered.OrderBy(fieldName => fieldName, AttributeNameComparer.Instance))
            {
                filteredFieldNames.Add(fieldName);
            }
        }

        button.Click += (_, _) =>
        {
            searchTextBox.Text = string.Empty;
            RefreshFields();
            popup.IsOpen = true;
            searchTextBox.Focus();
        };

        searchTextBox.TextChanged += (_, _) => RefreshFields();
        listBox.SelectionChanged += (_, _) =>
        {
            if (listBox.SelectedItem is not string fieldName)
            {
                return;
            }

            condition.FieldName = fieldName;
            button.Content = fieldName;
            popup.IsOpen = false;
            listBox.SelectedItem = null;
        };

        root.Children.Add(button);
        root.Children.Add(popup);
        RefreshFields();
        return root;
    }

    private static void NormalizeGroup(AdvancedSearchGroup group)
    {
        foreach (var childGroup in group.Children.OfType<AdvancedSearchGroup>())
        {
            NormalizeGroup(childGroup);
        }

        var emptyConditions = group.Children
            .OfType<AdvancedSearchCondition>()
            .Where(condition =>
                string.IsNullOrWhiteSpace(condition.FieldName) &&
                string.IsNullOrWhiteSpace(condition.Value))
            .Cast<AdvancedSearchFilterNode>()
            .ToList();

        foreach (var emptyCondition in emptyConditions)
        {
            group.Children.Remove(emptyCondition);
        }

        var emptyGroups = group.Children
            .OfType<AdvancedSearchGroup>()
            .Where(childGroup => childGroup.Children.Count == 0)
            .Cast<AdvancedSearchFilterNode>()
            .ToList();

        foreach (var emptyGroup in emptyGroups)
        {
            group.Children.Remove(emptyGroup);
        }
    }

    private static bool TryValidateGroup(AdvancedSearchGroup group, out string validationMessage)
    {
        foreach (var condition in group.Children.OfType<AdvancedSearchCondition>())
        {
            if (string.IsNullOrWhiteSpace(condition.FieldName))
            {
                validationMessage = "Every filter needs a field.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(condition.Value))
            {
                validationMessage = "Every filter needs a value.";
                return false;
            }
        }

        foreach (var childGroup in group.Children.OfType<AdvancedSearchGroup>())
        {
            if (!TryValidateGroup(childGroup, out validationMessage))
            {
                return false;
            }
        }

        validationMessage = string.Empty;
        return true;
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

    private static IBrush GetBrush(string resourceKey, string fallbackColor)
    {
        _ = resourceKey;
        return SolidColorBrush.Parse(fallbackColor);
    }
}
