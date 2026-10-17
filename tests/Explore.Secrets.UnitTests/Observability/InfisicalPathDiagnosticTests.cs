using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Providers;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.UnitTests.Observability;

public sealed class InfisicalPathDiagnosticTests
{
    [Test]
    public async Task NativeFailureRetainsSelectedRootAndStatusWithoutPrivateAcquisitionMaterial()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string canary = Guid.CreateVersion7().ToString("N");
        var logs = new Collector();
        var options = Options(listener, canary, "/ai");
        await using var provider = new InfisicalSecretProvider(logs, Microsoft.Extensions.Options.Options.Create(options));
        var server = ServeFailureAsync(listener, canary, deadline.Token);
        string target;
        try
        {
            await Assert.ThrowsAsync<SecretProviderException>(() => provider.InitializeAsync(deadline.Token));
            target = await server.WaitAsync(deadline.Token);
        }
        finally
        {
            await deadline.CancelAsync();
            listener.Stop();
            try { await server; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }

        await Assert.That(target).Contains("secretPath=/ai");
        await Assert.That(target).Contains(canary);
        await Assert.That(logs.Events.Any(row => row.State.Any(pair => pair.Key == "Path" && Equals(pair.Value, "/ai")))).IsTrue();
        await Assert.That(logs.Events.Any(row => row.State.Any(pair => pair.Key == "StatusCode" && Equals(pair.Value, HttpStatusCode.ServiceUnavailable)))).IsTrue();
        string output = JsonSerializer.Serialize(logs.Events);
        await Assert.That(output).DoesNotContain(canary);
        await Assert.That(output).DoesNotContain("private-provider-body");
        await Assert.That(output).DoesNotContain("forged-event");
    }

    [Test]
    public async Task InadmissiblePathFailsBeforeCredentialHandoff()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string canary = Guid.CreateVersion7().ToString("N");
        var logs = new Collector();
        await using var provider = new InfisicalSecretProvider(logs,
            Microsoft.Extensions.Options.Options.Create(Options(listener, canary, "/ai/" + canary)));

        await Assert.ThrowsAsync<SecretProviderException>(() => provider.InitializeAsync(deadline.Token));

        await Assert.That(listener.Pending()).IsFalse();
        await Assert.That(JsonSerializer.Serialize(logs.Events)).DoesNotContain(canary);
    }

    private static SecretProviderOptions Options(TcpListener listener, string canary, string path) => new()
    {
        Provider = SecretProviderType.Infisical,
        Consumer = SecretConsumer.Api,
        Infisical = new InfisicalOptions
        {
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}",
            ProjectId = canary,
            ClientId = canary,
            ClientSecret = canary,
            Environment = canary,
            Paths = [path]
        }
    };

    private static async Task<string> ServeFailureAsync(TcpListener listener, string canary, CancellationToken token)
    {
        string target = "";
        for (int index = 0; index < 2; index++)
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            string request = await reader.ReadLineAsync(token) ?? throw new IOException("Missing request.");
            bool chunked = false;
            int contentLength = 0;
            while (await reader.ReadLineAsync(token) is { Length: > 0 } header)
            {
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    contentLength = int.Parse(header["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
                if (header.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase))
                    chunked = header.Contains("chunked", StringComparison.OrdinalIgnoreCase);
            }
            if (chunked)
            {
                while (true)
                {
                    string size = await reader.ReadLineAsync(token) ?? throw new IOException("Missing chunk.");
                    int length = int.Parse(size.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (length == 0)
                    {
                        while (await reader.ReadLineAsync(token) is { Length: > 0 }) { }
                        break;
                    }
                    char[] body = new char[length];
                    if (await reader.ReadBlockAsync(body.AsMemory(), token) != length)
                        throw new EndOfStreamException("Truncated request chunk.");
                    await reader.ReadLineAsync(token);
                }
            }
            else if (contentLength > 0
                && await reader.ReadBlockAsync(new char[contentLength].AsMemory(), token) != contentLength)
                throw new EndOfStreamException("Truncated request body.");
            string bodyJson = index == 0
                ? JsonSerializer.Serialize(new { accessToken = canary, expiresIn = 3600 })
                : JsonSerializer.Serialize(new { error = canary + "\r\nprivate-provider-body\r\nforged-event" });
            byte[] responseBody = Encoding.UTF8.GetBytes(bodyJson);
            string status = index == 0 ? "200 OK" : "503 Service Unavailable";
            byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, token);
            await stream.WriteAsync(responseBody, token);
            await stream.FlushAsync(token);
            if (index == 1)
                target = Uri.UnescapeDataString(request.Split(' ')[1]);
        }
        return target;
    }

    private sealed record Export(string Message, KeyValuePair<string, object?>[] State, string? Exception);

    private sealed class Collector : ILogger<InfisicalSecretProvider>
    {
        public List<Export> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Events.Add(new(
                formatter(state, exception),
                state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
                exception?.ToString()));
    }
}
