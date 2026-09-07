namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Runtime.InteropServices;

internal sealed class SetupTerminalSignalScope : IDisposable
{
    private readonly List<PosixSignalRegistration> _registrations = [];
    private readonly SetupTerminalSecretBuffer _secret;
    private readonly Action _stop;
    private bool _disposed;

    internal SetupTerminalSignalScope(SetupTerminalSecretBuffer secret, Action stop)
    {
        _secret = secret ?? throw new ArgumentNullException(nameof(secret));
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        Console.CancelKeyPress += OnCancelKeyPress;
        if (!OperatingSystem.IsWindows())
        {
            Register(PosixSignal.SIGTERM);
            Register(PosixSignal.SIGHUP);
            Register(PosixSignal.SIGQUIT);
            Register(PosixSignal.SIGTSTP);
        }
    }

    private void Register(PosixSignal signal)
    {
        try
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, context =>
            {
                context.Cancel = true;
                RequestStop();
            }));
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (ArgumentException)
        {
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        RequestStop();
    }

    internal void RequestStop()
    {
        _secret.Clear();
        try
        {
            _stop();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Console.CancelKeyPress -= OnCancelKeyPress;
        foreach (PosixSignalRegistration registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
        _secret.Clear();
    }
}
