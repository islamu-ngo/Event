using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Explore.Blazor.IntegrationTests.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.IntegrationTests.Fixtures;

/// <summary>
/// Owns real BFF child processes and a held upstream request. Only subscribed
/// native shutdown and OS exit satisfy drain; forced teardown is not evidence.
/// </summary>
internal sealed class BffCredentialCutoverProcessFixture : IAsyncDisposable
{
    private readonly WebApplication _upstream;
    private readonly RedisBffTicketStoreTests.Authority _sessionAuthority = new();
    private readonly string _scratch;
    private readonly byte[] _certificatePin;
    private int _disposed;
    private readonly string _certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int? FailedBindProcessId { get; private set; }

    public BffCredentialCutoverProcessFixture()
    {
        _scratch = Directory.CreateTempSubdirectory("bff-cutover-").FullName;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        _certificatePin = certificate.GetCertHash(HashAlgorithmName.SHA256);
        File.WriteAllBytes(Path.Combine(_scratch, "localhost.pfx"),
            certificate.Export(X509ContentType.Pfx, _certificatePassword));

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        _upstream = builder.Build();
        _upstream.MapGet("/api/instance/settings/resolver-config", () =>
            Results.Json(new { pathEnabled = false, headerEnabled = false }));
        _upstream.MapGet("/api/instanceonboarding/status", () => Results.Json(new
        {
            isCompleted = true,
            state = "Completed",
            mode = "Interactive",
            generation = 1
        }));
        _upstream.MapGet("/api/instanceonboarding/auth-provider-configuration", () => Results.NoContent());
        _upstream.MapGet("/api/cutover-drain", async (HttpContext context) =>
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(context.RequestAborted);
            return Results.Text("drained");
        });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _sessionAuthority.InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            await _upstream.StartAsync(cancellationToken);
        }
        catch (Exception startupFailure)
        {
            try
            {
                await DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("bff_cutover_start_cleanup_failed", startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    public async Task<Replica> StartReplicaAsync(
        BffKeycloakFixture keycloak, string credential, CancellationToken cancellationToken,
        bool retainPortForBindFailure = false)
    {
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "Explore.slnx")))
        {
            repository = Directory.GetParent(repository)?.FullName
                ?? throw new InvalidOperationException("bff_process_repository_not_found");
        }
        string project = Path.Combine(repository, "src", "Explore.Blazor");
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        var origin = new Uri($"https://127.0.0.1:{port}/");
        if (!retainPortForBindFailure) reservation.Stop();
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(project, "bin", "Release", "net10.0", "Explore.Blazor.dll"));
        var hostVariables = new[] { "PATH", "DOTNET_ROOT", "TMPDIR", "LD_LIBRARY_PATH" }
            .ToDictionary(key => key, Environment.GetEnvironmentVariable);
        start.Environment.Clear();
        foreach (var (key, value) in hostVariables)
            if (value is not null) start.Environment[key] = value;
        string home = Path.Combine(_scratch, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        start.Environment["HOME"] = home;
        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["SECRET_PROVIDER"] = "Environment";
        var sessionOptions = _sessionAuthority.Options();
        var protection = _sessionAuthority.Source.Read();
        start.Environment["BFF_SESSION_REDIS_ENDPOINT"] = sessionOptions.Endpoint;
        start.Environment["BFF_SESSION_NAMESPACE"] = sessionOptions.Namespace;
        start.Environment["BFF_SESSION_REDIS_PASSWORD"] = sessionOptions.Password;
        start.Environment["EVENT_DP_BFF_RING"] = protection.Ring;
        start.Environment["EVENT_DP_BFF_ADMISSION"] = protection.Admission;
        start.Environment["EVENT_DP_BFF_ADMISSION_SHA256"] = protection.AdmissionSha256;
        start.Environment["EVENT_DP_BFF_MINIMUM_GENERATION"] = protection.MinimumGeneration
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["AUTHENTICATION_PROVIDER"] = "keycloak";
        start.Environment["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] = Guid.NewGuid().ToString();
        start.Environment["KEYCLOAK_ENDPOINT"] = keycloak.Authority[..keycloak.Authority.IndexOf("/realms/", StringComparison.Ordinal)];
        start.Environment["KEYCLOAK_REALM"] = BffKeycloakFixture.RealmName;
        start.Environment["KEYCLOAK_CLIENT_ID"] = BffKeycloakFixture.TestClientId;
        start.Environment["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = credential;
        start.Environment["Keycloak__RequireHttpsMetadata"] = "false";
        start.Environment["API_ENDPOINT"] = _upstream.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        start.Environment["Blazor__RequireApiReadyOnStart"] = "true";
        start.Environment["ASPNETCORE_URLS"] = origin.AbsoluteUri;
        start.Environment["Kestrel__Certificates__Default__Path"] = Path.Combine(_scratch, "localhost.pfx");
        start.Environment["Kestrel__Certificates__Default__Password"] = _certificatePassword;
        start.Environment["Deployment__Mode"] = "SingleTenant";
        start.Environment["Deployment__DefaultTenantId"] = Guid.NewGuid().ToString();
        var replica = new Replica(start, origin, _certificatePin);
        try
        {
            await replica.WaitUntilReadyAsync(cancellationToken);
            await keycloak.AddCallbackAsync(new Uri(replica.Address, "/signin-oidc"), cancellationToken);
            return replica;
        }
        catch (Exception startupFailure)
        {
            if (retainPortForBindFailure) FailedBindProcessId = replica.StartedProcessId;
            try
            {
                await replica.DisposeAsync();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("bff_replica_start_cleanup_failed", startupFailure, cleanupFailure);
            }
            throw;
        }
    }

    internal sealed class Replica : IAsyncDisposable
    {
        private readonly ProcessStartInfo _start;
        private Process? _process;
        private Task _output = Task.CompletedTask;
        private Task _error = Task.CompletedTask;
        private readonly Uri _origin;
        private readonly byte[] _certificatePin;
        private int _disposed;
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Address => _origin;
        public int? StartedProcessId { get; private set; }
        public int ProcessId => StartedProcessId ?? throw new InvalidOperationException("bff_process_not_started");

        public Replica(ProcessStartInfo start, Uri origin, byte[] certificatePin)
        {
            _start = start;
            _origin = origin;
            _certificatePin = certificatePin.ToArray();
        }

        private void StartProcess()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (_process is not null) throw new InvalidOperationException("bff_process_start_already_attempted");
            _process = new Process();
            Process process = _process;
            process.StartInfo = _start;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                Exited.TrySetResult(process.ExitCode);
            };
            if (!process.Start()) throw new InvalidOperationException("bff_process_start_failed");
            StartedProcessId = process.Id;
            _output = ConsumeAsync(process.StandardOutput);
            _error = ConsumeAsync(process.StandardError);
        }

        private async Task ConsumeAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith('{')) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.TryGetProperty("Seconds", out var seconds) && seconds.GetInt32() == 25)
                        Stopping.TrySetResult();
                }
                catch (JsonException) { }
            }
        }

        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartProcess();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using HttpClient probe = CreateClient(_origin, _certificatePin);
            probe.MaxResponseContentBufferSize = 4096;
            string observation = "no_response";
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Exited.Task.IsCompleted)
                    throw new InvalidOperationException($"bff_process_exited_before_readiness:{await Exited.Task}");
                if (deadline.IsCancellationRequested)
                    throw new InvalidOperationException($"bff_process_readiness_deadline:{observation}");
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(1));
                try
                {
                    Task<HttpResponseMessage> request = probe.GetAsync("/auth/status", attempt.Token);
                    if (await Task.WhenAny(request, Exited.Task) == Exited.Task)
                    {
                        await attempt.CancelAsync();
                        try { using HttpResponseMessage abandoned = await request; }
                        catch (HttpRequestException) { }
                        catch (OperationCanceledException) { }
                        throw new InvalidOperationException($"bff_process_exited_before_readiness:{await Exited.Task}");
                    }
                    using HttpResponseMessage response = await request;
                    observation = "unexpected_status";
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(attempt.Token));
                        if (body.RootElement.ValueKind == JsonValueKind.Object
                            && body.RootElement.TryGetProperty("isAuthenticated", out var authenticated)
                            && authenticated.ValueKind == JsonValueKind.False)
                        {
                            if (Exited.Task.IsCompleted)
                                throw new InvalidOperationException($"bff_process_exited_before_readiness:{await Exited.Task}");
                            return;
                        }
                        observation = "unexpected_anonymous_shape";
                    }
                }
                catch (HttpRequestException) { observation = "HttpRequestException"; }
                catch (JsonException) { observation = "JsonException"; }
                catch (OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!deadline.IsCancellationRequested) observation = "attempt_cancelled";
                }
                await Task.WhenAny(Exited.Task, Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token));
            }
        }

        public HttpClient CreateBrowser() => CreateClient(Address, _certificatePin);

        public HttpClient CreateBrowserWithMismatchedCertificatePin()
        {
            byte[] mismatch = _certificatePin.ToArray();
            mismatch[0] ^= 1;
            return CreateClient(Address, mismatch);
        }

        private static HttpClient CreateClient(Uri origin, byte[] certificatePin) => new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null && CryptographicOperations.FixedTimeEquals(
                    certificate.GetCertHash(HashAlgorithmName.SHA256), certificatePin)
        })
        { BaseAddress = origin };

        public async Task SignalStopAsync(CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo("kill") { UseShellExecute = false };
            start.ArgumentList.Add("-TERM");
            start.ArgumentList.Add(ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var signal = Process.Start(start) ?? throw new InvalidOperationException("bff_signal_start_failed");
            await signal.WaitForExitAsync(cancellationToken);
            if (signal.ExitCode != 0) throw new InvalidOperationException("bff_signal_failed");
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            List<Exception> failures = [];
            Process? process = _process;
            try
            {
                if (process is not null)
                {
                    bool associated = false;
                    try { _ = process.Id; associated = true; }
                    catch (InvalidOperationException) { }
                    catch (Exception failure) { failures.Add(failure); }
                    if (associated)
                    {
                        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                        catch (Exception failure) { failures.Add(failure); }
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                        catch (Exception failure) { failures.Add(failure); }
                    }
                }
                try { await Task.WhenAll(_output, _error).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception failure) { failures.Add(failure); }
            }
            finally
            {
                try { process?.Dispose(); }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count > 0) throw new AggregateException("bff_replica_cleanup_failed", failures);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Release.TrySetResult();
        List<Exception> failures = [];
        try { await _upstream.DisposeAsync(); }
        catch (Exception failure) { failures.Add(failure); }
        try { await _sessionAuthority.DisposeAsync(); }
        catch (Exception failure) { failures.Add(failure); }
        try { Directory.Delete(_scratch, recursive: true); }
        catch (Exception failure) { failures.Add(failure); }
        if (failures.Count > 0) throw new AggregateException("bff_cutover_cleanup_failed", failures);
    }
}
