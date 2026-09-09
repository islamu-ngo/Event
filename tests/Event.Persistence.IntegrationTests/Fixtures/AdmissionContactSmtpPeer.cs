using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Models;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Exercises real MailKit preparation and handoff against an event-driven local peer.</summary>
internal sealed class AdmissionContactSmtpPeer(string scenario, Action expire) : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(60));
    private Task run = Task.CompletedTask;
    internal bool Armed { get; set; }
    internal bool Crossed { get; private set; }
    internal List<string> Messages { get; } = [];
    internal List<string> Envelopes { get; } = [];
    internal int Connections { get; private set; }

    internal SmtpConfiguration Start()
    {
        listener.Start();
        run = RunAsync();
        return new SmtpConfiguration
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            FromAddress = "sender@example.test",
            Security = SmtpSecurityMode.None,
            Username = scenario == "smtp-auth" ? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) : null,
            Password = scenario == "smtp-auth" ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) : null
        };
    }

    internal void CrossConfigurationBoundary()
    {
        if (Armed && scenario == "smtp-config") Cross();
    }

    private void Cross()
    {
        Crossed = true;
        expire();
    }

    private async Task RunAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var connection = await listener.AcceptTcpClientAsync(stop.Token);
                Connections++;
                using var reader = new StreamReader(connection.GetStream());
                await using var writer = new StreamWriter(connection.GetStream()) { NewLine = "\r\n", AutoFlush = true };
                await writer.WriteLineAsync("220 contact.test SMTP");
                var body = new StringBuilder();
                bool data = false;
                while (await reader.ReadLineAsync(stop.Token) is { } line)
                {
                    if (data)
                    {
                        if (line != ".") { body.AppendLine(line); continue; }
                        Messages.Add(body.ToString());
                        body.Clear();
                        data = false;
                        if (Armed && scenario is "smtp-accepted" or "smtp-ambiguous") Cross();
                        if (Armed && scenario == "smtp-ambiguous") break;
                        await writer.WriteLineAsync("250 accepted");
                    }
                    else if (line.StartsWith("EHLO", StringComparison.Ordinal))
                    {
                        if (Armed && scenario == "smtp-connect") Cross();
                        await writer.WriteLineAsync(scenario == "smtp-auth" ? "250-contact.test\r\n250 AUTH PLAIN" : "250 contact.test");
                    }
                    else if (line.StartsWith("AUTH PLAIN", StringComparison.Ordinal))
                    {
                        if (Armed && scenario == "smtp-auth") Cross();
                        await writer.WriteLineAsync("235 authenticated");
                    }
                    else if (line.StartsWith("MAIL FROM", StringComparison.Ordinal) || line.StartsWith("RCPT TO", StringComparison.Ordinal))
                    {
                        Envelopes.Add(line);
                        await writer.WriteLineAsync("250 ok");
                    }
                    else if (line == "DATA") { data = true; await writer.WriteLineAsync("354 send body"); }
                    else if (line == "QUIT") { await writer.WriteLineAsync("221 bye"); break; }
                    else await writer.WriteLineAsync("250 ok");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        listener.Stop();
        try { await run; }
        finally { stop.Dispose(); }
    }
}
