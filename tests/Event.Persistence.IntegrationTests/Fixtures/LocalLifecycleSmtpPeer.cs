// ABOUTME: Controlled loopback SMTP peer for real MailKit lifecycle handoff and MIME inspection.
// ABOUTME: Awaits exact SMTP DATA completion with bounded cancellation and never polls or sleeps.

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed class LocalLifecycleSmtpPeer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new(TimeSpan.FromSeconds(25));
    private readonly TaskCompletionSource<string> _message = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _run;
    internal int Port { get; }
    internal Task<string> Message => _message.Task;

    internal LocalLifecycleSmtpPeer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _run = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var reader = new StreamReader(connection.GetStream());
            await using var writer = new StreamWriter(connection.GetStream()) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 lifecycle.test SMTP");
            var message = new StringBuilder();
            bool data = false;
            while (await reader.ReadLineAsync(_stop.Token) is { } line)
            {
                if (data)
                {
                    if (line != ".") { message.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append("\r\n"); continue; }
                    _message.TrySetResult(message.ToString());
                    data = false;
                    await writer.WriteLineAsync("250 accepted");
                }
                else if (line.StartsWith("EHLO", StringComparison.Ordinal)) await writer.WriteLineAsync("250 lifecycle.test");
                else if (line == "DATA") { data = true; await writer.WriteLineAsync("354 send body"); }
                else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); return; }
                else await writer.WriteLineAsync("250 ok");
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { _message.TrySetCanceled(_stop.Token); }
        catch (Exception exception) { _message.TrySetException(exception); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try { await _run; }
        finally { _stop.Dispose(); }
    }
}
