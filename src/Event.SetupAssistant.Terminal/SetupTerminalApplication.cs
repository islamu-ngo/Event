namespace ISLAMU.Event.SetupAssistant.Terminal;

using ISLAMU.Event.SetupAssistant.Presentation;
using global::Terminal.Gui.App;

internal sealed class SetupTerminalApplication(SetupPresentationSession session)
{
    private readonly SetupPresentationSession _session = session ?? throw new ArgumentNullException(nameof(session));

    internal int Run()
    {
        if (!SetupWorkspaceId.TryCreate("environment", out SetupWorkspaceId workspaceId))
            throw new InvalidOperationException("terminal-workspace-id-invalid");

        using var secret = new SetupTerminalSecretBuffer();
        var protectedWriter = new SetupTerminalProtectedWriter(Directory.GetCurrentDirectory());
        SetupPresentationWorkspace? workspace = null;
        IApplication? app = null;
        SetupTerminalWindow? window = null;
        SetupTerminalSignalScope? signals = null;
        var operation = new SetupTerminalArtifactOperation(
            () => workspace?.PublicInput ?? string.Empty,
            secret,
            protectedWriter);
        try
        {
            workspace = _session.CreateWorkspace(workspaceId, operation);
            workspace.Activate();
            app = Application.Create();
            app.Init();
            window = new SetupTerminalWindow(app, workspace, operation, secret, protectedWriter.IsAvailable);
            signals = new SetupTerminalSignalScope(
                secret,
                () => app.Invoke(_ => window.RequestStopFromSignal()));
            app.Run(window);
            return window.ExitCode;
        }
        finally
        {
            signals?.Dispose();
            window?.Dispose();
            secret.Clear();
            workspace?.Deactivate();
            workspace?.Dispose();
            app?.Dispose();
        }
    }
}
