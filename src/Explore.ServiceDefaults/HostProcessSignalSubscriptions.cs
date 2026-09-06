// ABOUTME: Owns process-wide signal delegates for one disposable application host.
// ABOUTME: Detaches every registration on host disposal, including hosts that never started.

namespace Explore.ServiceDefaults;

public sealed class HostProcessSignalSubscriptions : IDisposable
{
    private readonly Lock gate = new();
    private readonly List<(ConsoleCancelEventHandler Interrupt, EventHandler? Exit)> handlers = [];
    private bool disposed;

    public void Register(ConsoleCancelEventHandler interrupt, EventHandler? exit = null)
    {
        ArgumentNullException.ThrowIfNull(interrupt);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            handlers.Add((interrupt, exit));
            Console.CancelKeyPress += interrupt;
            if (exit is not null) AppDomain.CurrentDomain.ProcessExit += exit;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var handler in handlers)
            {
                Console.CancelKeyPress -= handler.Interrupt;
                if (handler.Exit is not null) AppDomain.CurrentDomain.ProcessExit -= handler.Exit;
            }
            handlers.Clear();
        }
    }
}
