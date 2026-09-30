namespace ISLAMU.Event.SetupAssistant.Desktop.Views;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ISLAMU.Event.SetupAssistant.Desktop.ViewModels;

public sealed partial class IdentityDraftView : UserControl
{
    private readonly TextBox _identityDocument;

    public IdentityDraftView()
    {
        AvaloniaXamlLoader.Load(this);
        _identityDocument = this.GetControl<TextBox>("IdentityDocument");
    }

    private SetupDesktopShellViewModel? ViewModel =>
        DataContext as SetupDesktopShellViewModel;

    private void IdentityChanged(object? sender, TextChangedEventArgs args) =>
        ViewModel?.InvalidateIdentityDraft();

    private void PrepareClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel is { } viewModel)
            viewModel.PrepareIdentityDraft(_identityDocument.Text ?? string.Empty);
    }

    private async void SaveClicked(object? sender, RoutedEventArgs args)
    {
        if (ViewModel is { } viewModel)
            _ = await viewModel.SaveIdentityAsync();
    }

    private void ClearClicked(object? sender, RoutedEventArgs args)
    {
        _identityDocument.Clear();
        ViewModel?.InvalidateIdentityDraft();
        _identityDocument.Focus();
    }
}
