using System.Diagnostics;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Event.Testing.DataProtection;
using Explore.AppHost;
using Microsoft.Extensions.Configuration;

namespace Event.Distributed.IntegrationTests;

public sealed class OwnedAppHostFixture : IAsyncDisposable
{
    private static readonly string[] DatabasePurposes =
    [
        "RUNTIME", "GLOBAL", "LOCAL_IDENTITY_WRITER", "INSTANCE_KEY_READ", "INSTANCE_KEY_WRITE",
        "INSTANCE_KEY_ISSUANCE", "PLATFORM_KEY_REPORT", "EMAIL_POLICY_INSTANCE_MUTATION", "EMAIL_POLICY_TENANT_MUTATION"
    ];
    private readonly string topology;
    private string[] volumes = [];
    private readonly Lock disposalLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? disposal;
    private Task? runningStartup;
    private HashSet<string> containerResources = [];
    private Exception? startupFailure;
    internal IReadOnlyDictionary<string, string?> EnvironmentAuthority { get; } = CreateEnvironmentAuthority();

    internal OwnedAppHostFixture(string topology)
    {
        this.topology = topology;
        RunId = Guid.NewGuid().ToString("N");
        Root = Path.Combine(Path.GetTempPath(), "islamu-assurance", RunId);
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(Root, TestingResourceIsolation.MarkerName), RunId);
    }

    public string RunId { get; }
    public string Root { get; }
    public string BootstrapIdentifier => $"assurance-{RunId}@example.invalid";
    public string BootstrapPassword => EnvironmentAuthority["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"]
        ?? throw new InvalidOperationException("test_bootstrap_authority_missing");
    public string BootstrapReplacementPassword => EnvironmentAuthority["ISLAMU_TEST_REPLACEMENT_PASSWORD"]
        ?? throw new InvalidOperationException("test_replacement_authority_missing");
    public DistributedApplication Application { get; private set; } = null!;

    public static Task<OwnedAppHostFixture> CreateAsync(string topology, CancellationToken cancellationToken,
        bool configuredAdministrator = false, string deploymentMode = "SingleTenant") =>
        CreateAsync(topology, configuredAdministrator, deploymentMode, null, null, cancellationToken);

    internal static async Task<OwnedAppHostFixture> CreateAsync(string topology,
        bool configuredAdministrator, string deploymentMode,
        IReadOnlyDictionary<string, string?>? untrustedInputs,
        Func<IDistributedApplicationTestingBuilder, Task>? configureBuilder, CancellationToken cancellationToken)
    {
        var owner = new OwnedAppHostFixture(topology);
        IDistributedApplicationTestingBuilder? builder = null;
        try
        {
            builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Explore_AppHost>(
                [
                    $"ISLAMU_TEST_RUN_ID={owner.RunId}", $"ISLAMU_TEST_ROOT={owner.Root}",
                    $"Aspire:Store:Path={Path.Combine(owner.Root, "aspire")}",
                    "DOTNET_ENVIRONMENT=Assurance", "ASPNETCORE_ENVIRONMENT=Assurance",
                    "ISLAMU_ASPIRE_MODE=local-default", $"Hosting:Topology={topology}",
                    "SECRET_PROVIDER=Environment", "SecretProvider:Provider=Environment",
                    "DATABASE_PROVIDER=PostgreSql", "DATABASE_NAME=islamu_assurance",
                    "DATABASE_SCHEMA=assurance", "DATABASE_HOST=localhost", "IDENTITY_DATABASE_TOPOLOGY=CoLocated",
                    "PrivacyErasure:Authority:Topology=EmbeddedSqlite", "ERASURE_DATABASE_TOPOLOGY=EmbeddedSqlite",
                    "AUTHENTICATION_PROVIDER=Local", "AUTHORIZATION_PROVIDER=Local",
                    $"DEPLOYMENT_MODE={deploymentMode}",
                    "GEOCODING_PROVIDER=None", "CONFIGURATION_MANIFEST_MODE=Off",
                    "AGENT_BROWSER_SEED_ENABLED=false", "WEBHOOKS_PROVIDER=Local",
                    $"INSTANCE_BOOTSTRAP_MODE={(configuredAdministrator ? "ConfiguredAdministrator" : "Interactive")}",
                    $"INSTANCE_BOOTSTRAP_ADMIN_PROVIDER={(configuredAdministrator ? "local" : string.Empty)}",
                    $"INSTANCE_BOOTSTRAP_ADMIN_SUBJECT={(configuredAdministrator ? Guid.CreateVersion7().ToString("D") : string.Empty)}",
                    $"INSTANCE_BOOTSTRAP_ADMIN_EMAIL={(configuredAdministrator ? owner.BootstrapIdentifier : string.Empty)}",
                    $"INSTANCE_BOOTSTRAP_BINDING_GENERATION={(configuredAdministrator ? "1" : string.Empty)}"
                ], (_, settings) =>
                {
                    settings.EnvironmentName = "Assurance";
                    settings.Configuration ??= new ConfigurationManager();
                    if (untrustedInputs is not null)
                        settings.Configuration.AddInMemoryCollection(untrustedInputs);
                    settings.Configuration.AddInMemoryCollection(owner.EnvironmentAuthority.Select(pair =>
                        new KeyValuePair<string, string?>($"TestingEnvironment:{pair.Key}", pair.Value))
                        .Append(new("TestingEnvironment:ISLAMU_TEST_RUN_ID", owner.RunId)));
                }, cancellationToken);

            string blocklist = Path.Combine(owner.Root, "password-blocklist.json");
            byte[] blocklistBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Version = 1, Revision = owner.RunId, Source = "ISLAMU generated assurance canary", License = "AGPL-3.0-or-later",
                Passwords = new[] { Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) }
            });
            await File.WriteAllBytesAsync(blocklist, blocklistBytes, cancellationToken);
            foreach (var project in builder.Resources.OfType<ProjectResource>()
                .Where(resource => resource.Name is "explore-api" or "event-standalone"))
            {
                builder.CreateResourceBuilder(project)
                    .WithEnvironment("AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_PATH", blocklist)
                    .WithEnvironment("AUTHENTICATION_LOCAL_PASSWORD_BLOCKLIST_SHA256", Convert.ToHexString(SHA256.HashData(blocklistBytes)));
                if (configuredAdministrator)
                    builder.CreateResourceBuilder(project).WithEnvironment("Authentication__Local__AccessTokenLifetimeMinutes", "5");
            }

            string bootstrap = Path.Combine(owner.Root, "database-logins.sql");
            await File.WriteAllLinesAsync(bootstrap, DatabasePurposes.Select(purpose =>
                $"\\getenv role_password DATABASE_{purpose}_PASSWORD\nCREATE ROLE \"{owner.EnvironmentAuthority[$"DATABASE_{purpose}_USERNAME"]}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD :'role_password';"), cancellationToken);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(bootstrap, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var postgres = builder.Resources.OfType<PostgresServerResource>().Single(resource => resource.Name == "postgres");
            var postgresBuilder = builder.CreateResourceBuilder(postgres)
                .WithBindMount(bootstrap, "/docker-entrypoint-initdb.d/assurance-logins.sql", isReadOnly: true);
            foreach (string purpose in DatabasePurposes)
                postgresBuilder.WithEnvironment($"DATABASE_{purpose}_PASSWORD", owner.EnvironmentAuthority[$"DATABASE_{purpose}_PASSWORD"]);
            if (configureBuilder is not null)
                await configureBuilder(builder);
            owner.volumes = builder.Resources.OfType<ContainerResource>()
                .SelectMany(resource => resource.Annotations.OfType<ContainerMountAnnotation>())
                .Where(mount => mount.Type == ContainerMountType.Volume)
                .Select(mount => mount.Source!).Distinct(StringComparer.Ordinal).ToArray();
            owner.containerResources = builder.Resources.OfType<ContainerResource>()
                .Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);
            if (owner.volumes.Any(name => !name.StartsWith($"islamu-test-{owner.RunId}-", StringComparison.Ordinal)))
                throw new InvalidOperationException("test_resource_unowned_volume");
            owner.Application = await builder.BuildAsync(cancellationToken);
            return owner;
        }
        catch (Exception creationFailure)
        {
            var failures = new List<Exception> { creationFailure };
            if (builder is not null)
            {
                try { await builder.DisposeAsync(); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (failures.Count > 1)
                throw new AggregateException(failures);
            try { await owner.DisposeAsync(); }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count > 1)
                throw new AggregateException(failures);
            throw;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (disposalLock)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            return runningStartup ??= StartCoreAsync(cancellationToken);
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        Task start = Application.StartAsync(startup.Token);
        Task failures = RejectFailedResourcesAsync(startup.Token);
        Task ready = Task.CompletedTask;
        try
        {
            await await Task.WhenAny(start, failures);
            await start;
            startup.Token.ThrowIfCancellationRequested();
            ready = WaitForReadyAsync(startup.Token);
            await await Task.WhenAny(ready, failures);
            await ready;
        }
        catch (Exception exception)
        {
            startupFailure = exception;
        }
        finally
        {
            await startup.CancelAsync();
            try { await Task.WhenAll(start, failures, ready); }
            catch (Exception)
            {
                var unexpected = new[] { start, failures, ready }.Where(task => task.IsFaulted)
                    .SelectMany(task => task.Exception!.Flatten().InnerExceptions)
                    .Where(exception => exception is not OperationCanceledException
                        && !ReferenceEquals(exception, startupFailure)).ToList();
                if (unexpected.Count > 0)
                {
                    startupFailure = new AggregateException(startupFailure is null
                        ? unexpected : unexpected.Prepend(startupFailure));
                }
            }
        }
        if (startupFailure is not null)
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    private async Task RejectFailedResourcesAsync(CancellationToken cancellationToken)
    {
        await foreach (var update in Application.ResourceNotifications.WatchAsync(cancellationToken))
        {
            if (update.Snapshot.State?.Text == KnownResourceStates.FailedToStart
                || update.Snapshot.ExitCode is not (null or 0))
                throw new InvalidOperationException($"test_resource_start_failed:{update.Resource.Name}");
        }
    }

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        await Application.ResourceNotifications.WaitForResourceAsync(
            "event-migrationservice", KnownResourceStates.Exited, cancellationToken);
        string[] services = topology == "Split" ? ["explore-api", "explore-blazor"] : ["event-standalone"];
        foreach (string service in services)
        {
            await Application.ResourceNotifications.WaitForResourceHealthyAsync(service, cancellationToken);
            using var client = CreateHttpClient(service, "operations");
            using var response = await client.GetAsync("/health", cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public HttpClient CreateHttpClient(string resourceName, string endpointName = "http") =>
        Application.CreateHttpClient(resourceName, endpointName);

    public ValueTask DisposeAsync()
    {
        lock (disposalLock)
            return new(disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        using var ownedLifetime = lifetime;
        var failures = new List<Exception>();
        bool applicationSettled = true;
        using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await lifetime.CancelAsync();
        if (Application is not null)
        {
            try { await Application.StopAsync(cleanupDeadline.Token); }
            catch (Exception exception) { failures.Add(exception); }
            finally
            {
                try { await Application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60)); }
                catch (Exception exception)
                {
                    applicationSettled = false;
                    failures.Add(exception);
                }
            }
        }
        if (runningStartup is not null)
        {
            try { await runningStartup.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception exception) when (runningStartup.IsCompleted)
            {
                if (startupFailure is null)
                    failures.Add(exception);
            }
            catch (TimeoutException exception)
            {
                failures.Add(exception);
                throw new AggregateException(startupFailure is null ? failures : failures.Prepend(startupFailure));
            }
        }
        if (!applicationSettled)
            throw new AggregateException(startupFailure is null ? failures : failures.Prepend(startupFailure));
        try { new TestingResourceIsolation(RunId, Root).VerifyOwnership(); }
        catch (Exception exception) when (startupFailure is not null || failures.Count > 0)
        {
            failures.Add(exception);
            throw new AggregateException(startupFailure is null ? failures : failures.Prepend(startupFailure));
        }
        foreach (string volume in volumes)
        {
            try
            {
            var retained = await RunDockerAsync(["ps", "-aq", "--filter", $"volume={volume}"], cleanupDeadline.Token);
            if (retained.ExitCode != 0)
                throw new InvalidOperationException("test_resource_container_inventory_failed");
            foreach (string containerId in retained.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (containerId.Length is not (12 or 64) || !containerId.All(char.IsAsciiHexDigit))
                    throw new InvalidOperationException("test_resource_container_identity_invalid");
                var labels = await RunDockerAsync(["inspect", "--format", "{{json .Config.Labels}}", containerId], cleanupDeadline.Token);
                if (labels.ExitCode != 0)
                    throw new InvalidOperationException("test_resource_container_inspection_failed");
                RequireOwnedContainer(RunId, containerResources, labels.Output);
                await RunDockerAsync(["wait", containerId], cleanupDeadline.Token);
                var removal = await RunDockerAsync(["rm", containerId], cleanupDeadline.Token);
                if (removal.ExitCode != 0 && !removal.Error.Contains("no such container", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("test_resource_container_cleanup_failed");
            }
            var volumeRemoval = await RunDockerAsync(["volume", "rm", volume], cleanupDeadline.Token);
            if (volumeRemoval.ExitCode != 0 && !volumeRemoval.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"test_resource_volume_cleanup_failed:{volume}:{volumeRemoval.Error}");
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count > 0)
            throw new AggregateException(startupFailure is null ? failures : failures.Prepend(startupFailure));
        try
        {
            new TestingResourceIsolation(RunId, Root).VerifyOwnership();
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (startupFailure is not null)
        {
            throw new AggregateException(startupFailure, exception);
        }
    }

    internal static void RequireOwnedContainer(string runId, IReadOnlySet<string> resources, string labelsJson)
    {
        using var labels = JsonDocument.Parse(labelsJson);
        if (!labels.RootElement.TryGetProperty("islamu.assurance.owner", out var owner)
            || owner.GetString() != runId
            || !labels.RootElement.TryGetProperty("islamu.assurance.resource", out var resource)
            || !resources.Contains(resource.GetString() ?? string.Empty))
            throw new InvalidOperationException("test_resource_foreign_container");
    }

    internal static async Task<(int ExitCode, string Output, string Error)> RunDockerAsync(
        string[] arguments, CancellationToken cancellationToken, Action<int>? processStarted = null)
    {
        using var command = new Process();
        command.StartInfo = new("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            command.StartInfo.ArgumentList.Add(argument);
        command.Start();
        Task<string> output = command.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error = command.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            processStarted?.Invoke(command.Id);
            await command.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            if (!command.HasExited)
            {
                try { command.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (command.HasExited) { }
            }
            await command.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            throw;
        }
        return (command.ExitCode, await output, await error);
    }

    private static FrozenDictionary<string, string?> CreateEnvironmentAuthority()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["KEYCLOAK_ADMIN"] = "assurance_admin", ["STORAGE_S3_ACCESS_KEY_ID"] = "assurance_storage",
            ["SECRET_PROVIDER"] = "Environment", ["DOTNET_ENVIRONMENT"] = "Assurance",
            ["ASPNETCORE_ENVIRONMENT"] = "Assurance", ["SecretProvider:Provider"] = "Environment",
            ["DATABASE_PROVIDER"] = "PostgreSql", ["DATABASE_NAME"] = "islamu_assurance",
            ["DATABASE_SCHEMA"] = "assurance", ["AUTHENTICATION_PROVIDER"] = "Local",
            ["AUTHORIZATION_PROVIDER"] = "Local"
        };
        foreach (string purpose in DatabasePurposes.Append("MIGRATOR"))
        {
            values[$"DATABASE_{purpose}_USERNAME"] = "assurance_" + purpose.ToLowerInvariant();
            values[$"DATABASE_{purpose}_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }
        foreach (string key in new[] { "CERBOS_POSTGRES_PASSWORD", "KEYCLOAK_ADMIN_PASSWORD",
            "STORAGE_S3_SECRET_ACCESS_KEY", "AUTHENTICATION_LOCAL_JWT_KEY", "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
            "ISLAMU_TEST_REPLACEMENT_PASSWORD" })
            values[key] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        foreach (string partition in new[] { "api", "bff", "combined" })
        {
            var input = new ExternalDataProtectionTestAuthority(partition).Input;
            string prefix = $"EVENT_DP_{partition.ToUpperInvariant()}_";
            values[prefix + "RING"] = input.Ring;
            values[prefix + "ADMISSION"] = input.Admission;
            values[prefix + "ADMISSION_SHA256"] = input.AdmissionSha256;
            values[prefix + "MINIMUM_GENERATION"] = "1";
        }
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
