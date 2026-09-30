namespace ISLAMU.Event.SetupAssistant.Desktop.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

    private void DirectionClicked(object? sender, RoutedEventArgs args) =>
        FlowDirection = sender is CheckBox { IsChecked: true }
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
}
