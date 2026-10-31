using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using static ISLAMU.ReleaseEngineering.SecurityExecutionEvidence;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceDaemonTransportTests
{
    [Test]
    public async Task DaemonObservationDoesNotPropagateAmbientTraceContext()
    {
        string root = Directory.CreateTempSubdirectory("daemon-headers-").FullName;
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string path = Path.Combine(root, "api.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var activity = new Activity("daemon-observation-probe");
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.TraceStateString = "probe=local";
            activity.AddBaggage("probe-context", "not-for-daemon");
            activity.Start();
            using var daemon = new SelectedDaemon(path);
            var headers = ObserveHeaderNamesAsync(listener, deadline.Token);
            Task<JsonDocument> observation = daemon.GetAsync("/v1.40/images/json?all=true", deadline.Token);
            Task completed = Task.WhenAll(headers, observation);
            try
            {
                try { await completed; }
                catch
                {
                    if (completed.Exception is { } failures) throw failures;
                    throw;
                }
                string[] forbidden = ["traceparent", "tracestate", "baggage", "correlation-context", "request-id"];
                var request = await headers;
                await Assert.That(request.Names.Intersect(forbidden, StringComparer.OrdinalIgnoreCase)).IsEmpty();
                await Assert.That(request.ExpectedRequest).IsTrue();
                await Assert.That((await observation).RootElement.GetProperty("ok").GetBoolean()).IsTrue();
                await Assert.That(ReferenceEquals(Activity.Current, activity)).IsTrue();
            }
            finally
            {
                if (observation.IsCompletedSuccessfully) observation.Result.Dispose();
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PreCancelledObservationDoesNotConnect()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        int connections = 0;
        using var daemon = new SelectedDaemon(
            Path.Combine(Path.GetTempPath(), "unselected-" + Guid.CreateVersion7().ToString("N") + ".sock"),
            socketConnected: _ => Interlocked.Increment(ref connections));
        Task<JsonDocument> request = daemon.GetAsync("/v1.40/info", caller.Token);
        try { using JsonDocument unexpected = await request; }
        catch (OperationCanceledException) { }
        await Assert.That(request.IsCanceled).IsTrue();
        await Assert.That(connections).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationSettlesAReadWithAnIncompleteResponseBody()
    {
        string root = Directory.CreateTempSubdirectory("daemon-cancel-").FullName;
        File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            string path = Path.Combine(root, "api.sock");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var caller = new CancellationTokenSource();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            const string prefix =
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"ok\":";
            using var daemon = new SelectedDaemon(path, streamConnected: stream =>
                new ResponseReadWitness(stream, Encoding.ASCII.GetByteCount(prefix), waiting));
            var peer = ObserveHeaderNamesAsync(listener, deadline.Token, prefix, release.Task);
            Task<JsonDocument> request = daemon.GetAsync("/v1.40/images/json?all=true", caller.Token);
            Exception? failure = null;
            try
            {
                await waiting.Task.WaitAsync(deadline.Token);
                await caller.CancelAsync();
                try { using JsonDocument unexpected = await request; }
                catch (OperationCanceledException) { }
                await Assert.That(request.IsCanceled).IsTrue();
            }
            catch (Exception observationFailure) { failure = observationFailure; }
            finally
            {
                await caller.CancelAsync();
                release.TrySetResult();
                Task settled = Task.WhenAll(peer, request);
                try { await settled.WaitAsync(deadline.Token); }
                catch (Exception cleanupFailure)
                {
                    if (!(request.IsCanceled && peer.IsCompletedSuccessfully))
                    {
                        Exception retained = settled.Exception is { } faults ? faults : cleanupFailure;
                        failure = failure is null ? retained : new AggregateException(failure, retained);
                    }
                }
            }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    [Explicit]
    public Task SelectedSocketCompletesTheReadOnlyAcquisitionSequence() => ObserveSelectedSocketAsync(true);

    [Test]
    [Explicit]
    public Task SelectedSocketCompletesTheFreshConnectionAcquisitionSequence() => ObserveSelectedSocketAsync(false);

    private static async Task ObserveSelectedSocketAsync(bool reuseConnection)
    {
        string selected = Environment.GetEnvironmentVariable("DOCKER_HOST")
            ?? throw new InvalidOperationException("DOCKER_HOST must select the native Unix socket.");
        if (!selected.StartsWith("unix:///", StringComparison.Ordinal))
            throw new InvalidOperationException("A native Unix socket is required.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string socketPath = selected["unix://".Length..];
        using var reused = reuseConnection ? new SelectedDaemon(socketPath) : null;
        string[] paths =
        [
            "/v1.40/version", "/v1.40/info", "/v1.40/containers/json?all=true",
            "/v1.40/images/json?all=true", "/v1.40/images/json?all=true"
        ];
        int index = 0;
        try
        {
            for (; index < paths.Length; index++)
            {
                using var isolated = reuseConnection ? null : new SelectedDaemon(socketPath);
                SelectedDaemon daemon = reused ?? isolated
                    ?? throw new InvalidOperationException("A selected daemon client is required.");
                using JsonDocument observation = await daemon.GetAsync(paths[index], deadline.Token);
                await Assert.That(observation.RootElement.ValueKind)
                    .IsEqualTo(index < 2 ? JsonValueKind.Object : JsonValueKind.Array);
            }
        }
        catch (Exception failure)
        {
            throw new InvalidOperationException($"selected_daemon_read_failed:{index}", failure);
        }
    }

    private sealed class ResponseReadWitness(Stream inner, int prefixBytes, TaskCompletionSource waiting) : Stream
    {
        private long _received;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 0 && Interlocked.Read(ref _received) >= prefixBytes) waiting.TrySetResult();
            int count = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Interlocked.Add(ref _received, count);
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.WriteAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        protected override void Dispose(bool disposing)
        {
            try { if (disposing) inner.Dispose(); }
            finally { base.Dispose(disposing); }
        }
    }

    private static async Task<(string[] Names, bool ExpectedRequest)> ObserveHeaderNamesAsync(
        Socket listener, CancellationToken token, string? responsePrefix = null, Task? releaseResponse = null)
    {
        await using var stream = new NetworkStream(await listener.AcceptAsync(token), ownsSocket: true);
        using var bytes = new MemoryStream();
        byte[] one = new byte[1], ending = [13, 10, 13, 10];
        int matched = 0;
        for (int count = 0; count < 16_384; count++)
        {
            if (await stream.ReadAsync(one, token) == 0)
                throw new InvalidDataException("Request headers are incomplete.");
            bytes.WriteByte(one[0]);
            matched = one[0] == ending[matched] ? matched + 1 : one[0] == ending[0] ? 1 : 0;
            if (matched != ending.Length) continue;
            string[] lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.None);
            string[] names = lines.Where(line => line.IndexOf(':') > 0).Select(line => line[..line.IndexOf(':')]).ToArray();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(responsePrefix ??
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 11\r\nConnection: close\r\n\r\n{\"ok\":true}"), token);
            if (releaseResponse is not null) await releaseResponse.WaitAsync(token);
            return (names, lines[0] == "GET /v1.40/images/json?all=true HTTP/1.1"
                && lines.Any(line => line.Equals("Host: localhost", StringComparison.OrdinalIgnoreCase)));
        }
        throw new InvalidDataException("Request headers exceed the bound.");
    }
}
