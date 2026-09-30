namespace ISLAMU.Event.SetupAssistant.Desktop;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ISLAMU.Event.SetupAssistant.Desktop.ViewModels;
using ISLAMU.Event.SetupAssistant.Desktop.Views;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new SetupDesktopShellViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = shell
            };
            desktop.Exit += (_, _) => shell.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
