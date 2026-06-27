using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FlowRunFinderV2.Core.Configuration;
using FlowRunFinderV2.UI;
using FlowRunFinderV2.UI.Model;

namespace FlowRunFinderV2.UI.Dialog;

public sealed partial class ConnectionSelectionDialog : Avalonia.Controls.Window
{
    private ListBox _connectionsListBox = null!;
    private TextBlock _validationTextBlock = null!;

    public ConnectionSelectionDialog(IEnumerable<ConnectionProfile> connections)
    {
        InitializeComponent();
        this.ApplyAppIcon();
        _connectionsListBox.ItemsSource = connections;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _connectionsListBox = this.FindControl<ListBox>("ConnectionsListBox")
            ?? throw new InvalidOperationException("ConnectionsListBox was not found.");
        _validationTextBlock = this.FindControl<TextBlock>("ValidationTextBlock")
            ?? throw new InvalidOperationException("ValidationTextBlock was not found.");
    }

    private void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        Close(ConnectionSelectionResult.New);
    }

    private void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (_connectionsListBox.SelectedItem is ConnectionProfile connection)
        {
            Close(ConnectionSelectionResult.Open(connection));
            return;
        }

        _validationTextBlock.Text = "Select a connection.";
    }

    private void OnOpenClicked(object? sender, TappedEventArgs e)
    {
        OnOpenClicked(sender, new RoutedEventArgs());
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
