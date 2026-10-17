using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
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
    private readonly string _scratch;
    private readonly string _certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public Task StartAsync(CancellationToken cancellationToken) => _upstream.StartAsync(cancellationToken);

    public async Task<Replica> StartReplicaAsync(
        BffKeycloakFixture keycloak, string credential, CancellationToken cancellationToken)
    {
        string repository = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repository, "Explore.slnx")))
        {
            repository = Directory.GetParent(repository)?.FullName
                ?? throw new InvalidOperationException("bff_process_repository_not_found");
        }
        string project = Path.Combine(repository, "src", "Explore.Blazor");
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
        start.Environment["ASPNETCORE_URLS"] = "https://127.0.0.1:0";
        start.Environment["Kestrel__Certificates__Default__Path"] = Path.Combine(_scratch, "localhost.pfx");
        start.Environment["Kestrel__Certificates__Default__Password"] = _certificatePassword;
        start.Environment["Deployment__Mode"] = "SingleTenant";
        start.Environment["Deployment__DefaultTenantId"] = Guid.NewGuid().ToString();
        var replica = new Replica(start);
        try
        {
            await replica.Listening.Task.WaitAsync(cancellationToken);
            await keycloak.AddCallbackAsync(new Uri(replica.Address, "/signin-oidc"), cancellationToken);
            return replica;
        }
        catch
        {
            await replica.DisposeAsync();
            throw;
        }
    }

    internal sealed class Replica : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task _output;
        private readonly Task _error;
        public TaskCompletionSource<Uri> Listening { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Uri Address => Listening.Task.GetAwaiter().GetResult();
        public int ProcessId => _process.Id;

        public Replica(ProcessStartInfo start)
        {
            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.Exited += (_, _) =>
            {
                Exited.TrySetResult(_process.ExitCode);
                Listening.TrySetException(new InvalidOperationException(
                    $"bff_process_exited_before_listening:{_process.ExitCode}"));
            };
            if (!_process.Start()) throw new InvalidOperationException("bff_process_start_failed");
            _output = ConsumeAsync(_process.StandardOutput);
            _error = ConsumeAsync(_process.StandardError);
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
                    if (root.TryGetProperty("SourceContext", out var source)
                        && source.GetString() == "Microsoft.Hosting.Lifetime"
                        && root.TryGetProperty("address", out var address)
                        && Uri.TryCreate(address.GetString(), UriKind.Absolute, out var listener))
                        Listening.TrySetResult(listener);
                    if (root.TryGetProperty("Seconds", out var seconds) && seconds.GetInt32() == 25)
                        Stopping.TrySetResult();
                }
                catch (JsonException) { }
            }
        }

        public HttpClient CreateBrowser() => new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate?.GetNameInfo(X509NameType.SimpleName, false) == "localhost"
        })
        { BaseAddress = Address };

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
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await Task.WhenAll(_output, _error).WaitAsync(TimeSpan.FromSeconds(10));
            _process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Release.TrySetResult();
        await _upstream.DisposeAsync();
        Directory.Delete(_scratch, recursive: true);
    }
}
