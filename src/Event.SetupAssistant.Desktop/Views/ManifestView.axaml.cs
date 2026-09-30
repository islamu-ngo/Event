namespace ISLAMU.Event.SetupAssistant.Desktop.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ISLAMU.Event.SetupAssistant.Desktop.ViewModels;

public sealed partial class ManifestView : UserControl
{
    public ManifestView() => AvaloniaXamlLoader.Load(this);

    private SetupDesktopShellViewModel? ViewModel =>
        DataContext as SetupDesktopShellViewModel;

    private void PrepareClicked(object? sender, RoutedEventArgs args) =>
        ViewModel?.PrepareManifest();

    private async void SaveClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel is { } viewModel)
            _ = await viewModel.SaveManifestAsync();
    }
}
