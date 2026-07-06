using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlowRunFinderV2.UI;

namespace FlowRunFinderV2.UI.Dialog;

public sealed partial class DateTimePickerDialog : Avalonia.Controls.Window
{
    private DatePicker _datePicker = null!;
    private TimePicker _timePicker = null!;
    private TextBlock _validationTextBlock = null!;

    public DateTimePickerDialog(DateTimeOffset initialValueUtc)
    {
        InitializeComponent();
        this.ApplyAppIcon();

        var localValue = initialValueUtc.ToLocalTime();
        _datePicker.SelectedDate = localValue;
        _timePicker.SelectedTime = localValue.TimeOfDay;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _datePicker = this.FindControl<DatePicker>("DatePicker")
            ?? throw new InvalidOperationException("DatePicker was not found.");
        _timePicker = this.FindControl<TimePicker>("TimePicker")
            ?? throw new InvalidOperationException("TimePicker was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        _validationTextBlock.Text = string.Empty;

        if (_datePicker.SelectedDate is not { } selectedDate)
        {
            _validationTextBlock.Text = "Select a date.";
            return;
        }

        var selectedTime = _timePicker.SelectedTime ?? TimeSpan.Zero;
        var localValue = new DateTimeOffset(
            selectedDate.Year,
            selectedDate.Month,
            selectedDate.Day,
            selectedTime.Hours,
            selectedTime.Minutes,
            0,
            TimeZoneInfo.Local.GetUtcOffset(selectedDate.DateTime));

        Close(localValue.ToUniversalTime());
    }
}
