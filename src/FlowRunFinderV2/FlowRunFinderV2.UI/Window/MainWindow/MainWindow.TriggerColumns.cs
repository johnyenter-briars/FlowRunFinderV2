using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using FlowRunFinderV2.Core.Model;
using FlowRunFinderV2.UI.Converters;
using FlowRunFinderV2.UI.Infrastructure;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Window;

public sealed partial class MainWindow
{
    private void ResetRunColumns()
    {
        while (RunsDataGrid.Columns.Count > FixedRunColumnCount)
        {
            RunsDataGrid.Columns.RemoveAt(RunsDataGrid.Columns.Count - 1);
        }
    }

    private void ResetTriggerColumnOptions(bool clearKnownKeys)
    {
        _triggerColumnOptions.Clear();
        _filteredTriggerColumnOptions.Clear();
        if (clearKnownKeys)
        {
            _knownTriggerKeys.Clear();
        }

        TriggerColumnsButton.IsEnabled = false;
        AdvancedSearchButton.IsEnabled = _knownTriggerKeys.Count > 0;
        TriggerColumnsPopup.IsOpen = false;
    }

    private void ClearSelectedFlow()
    {
        _selectedFlow = null;
        FlowPickerButton.Content = "Select a flow";
        FlowPickerPopup.IsOpen = false;
        FlowSearchTextBox.Text = string.Empty;
        FlowListBox.SelectedItem = null;
        _filteredFlows.Clear();
    }

    private void RefreshFilteredFlows()
    {
        var searchText = FlowSearchTextBox.Text?.Trim();
        _filteredFlows.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _flows
            : _flows.Where(flow =>
                flow.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                flow.WorkflowId.ToString("D").Contains(searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var flow in filtered.OrderBy(flow => flow.Name, StringComparer.OrdinalIgnoreCase))
        {
            _filteredFlows.Add(flow);
        }
    }

    private void RefreshFilteredTriggerColumnOptions()
    {
        var searchText = TriggerColumnsSearchTextBox.Text?.Trim();
        _filteredTriggerColumnOptions.Clear();

        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _triggerColumnOptions
            : _triggerColumnOptions.Where(option =>
                option.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var option in filtered.OrderBy(option => option.Name, AttributeNameComparer.Instance))
        {
            _filteredTriggerColumnOptions.Add(option);
        }
    }

    private void SetTriggerColumnOptions(CloudFlow flow, IEnumerable<string> triggerKeys)
    {
        _triggerColumnOptions.Clear();
        foreach (var triggerKey in triggerKeys)
        {
            _knownTriggerKeys.Add(triggerKey);
        }

        RestoreTriggerColumnOptions(flow);
    }

    private void RestoreTriggerColumnOptions(CloudFlow flow)
    {
        _triggerColumnOptions.Clear();
        _filteredTriggerColumnOptions.Clear();
        var flowId = flow.WorkflowId.ToString("D");
        var selectedColumns = _settings.SelectedTriggerColumnsByFlowId.TryGetValue(flowId, out var cachedColumns)
            ? new HashSet<string>(cachedColumns, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _knownTriggerKeys.OrderBy(key => key, AttributeNameComparer.Instance))
        {
            _triggerColumnOptions.Add(new TriggerColumnOption(key)
            {
                IsSelected = selectedColumns.Contains(key)
            });
        }

        RefreshFilteredTriggerColumnOptions();
        TriggerColumnsButton.IsEnabled = _triggerColumnOptions.Count > 0;
        AdvancedSearchButton.IsEnabled = _triggerColumnOptions.Count > 0;
        AddTriggerColumns(_triggerColumnOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Name));
    }

    private async void ApplySelectedTriggerColumns()
    {
        await ApplySelectedTriggerColumns(saveSelection: true);
    }

    private async Task ApplySelectedTriggerColumns(bool saveSelection)
    {
        var selectedColumns = _triggerColumnOptions
            .Where(option => option.IsSelected)
            .Select(option => option.Name)
            .ToList();

        AddTriggerColumns(selectedColumns);

        if (!saveSelection || _selectedFlow is not { } flow)
        {
            return;
        }

        await _settingsManager.UpdateAsync(settings =>
        {
            var flowId = flow.WorkflowId.ToString("D");
            if (selectedColumns.Count == 0)
            {
                settings.SelectedTriggerColumnsByFlowId.Remove(flowId);
            }
            else
            {
                settings.SelectedTriggerColumnsByFlowId[flowId] = selectedColumns;
            }
        });

        _settings = _settingsManager.Current;
    }

    private void AddTriggerColumns(IEnumerable<string> triggerKeys)
    {
        ResetRunColumns();

        foreach (var key in triggerKeys)
        {
            RunsDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = key,
                CellTemplate = CreateTriggerInputCellTemplate(key),
                Width = DataGridLength.Auto
            });
        }
    }

    private IDataTemplate CreateTriggerInputCellTemplate(string key)
    {
        return new FuncDataTemplate<FlowRun>((_, _) =>
        {
            var textBlock = new TextBlock();
            textBlock.Bind(
                TextBlock.TextProperty,
                new Binding(nameof(FlowRun.TriggerInputs))
                {
                    Mode = BindingMode.OneWay,
                    Converter = TriggerInputValueConverter.Instance,
                    ConverterParameter = key
                });
            textBlock.PointerPressed += OnCopyCellPointerPressed;
            return textBlock;
        });
    }
}
