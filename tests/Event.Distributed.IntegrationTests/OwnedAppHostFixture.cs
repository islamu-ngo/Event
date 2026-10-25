using System.Diagnostics;
using System.Globalization;
using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Collections.Immutable;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Event.Testing.DataProtection;
using Explore.AppHost;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Distributed.IntegrationTests;

public sealed class OwnedAppHostFixture : IAsyncDisposable
{
    private static readonly string[] DatabasePurposes =
    [
        "RUNTIME", "GLOBAL", "LOCAL_IDENTITY_WRITER", "INSTANCE_KEY_READ", "INSTANCE_KEY_WRITE",
        "INSTANCE_KEY_ISSUANCE", "PLATFORM_KEY_REPORT", "EMAIL_POLICY_INSTANCE_MUTATION", "EMAIL_POLICY_TENANT_MUTATION"
    ];
    private readonly string topology;
    private readonly OwnedAppHostProfile profile;
    private string[] volumes = [];
    private readonly Lock disposalLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TestingApplicationExecution execution = new();
    private IHostApplicationLifetime? applicationLifetime;
    private Task? disposal;
    private Task? runningStartup;
    private HashSet<string> containerResources = [];
    private ContainerResource[] cleanupResources = [];
    private readonly Lock containerIdentityLock = new();
    private readonly Dictionary<string, ContainerCleanupIdentity> containerIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Id, HashSet<string> VolumeTargets)> cleanupImages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VolumeCleanupIdentity> volumeIdentities = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource cleanupObservationLifetime = new();
    private readonly List<ContainerCleanupCandidate> cleanupCandidates = [];
    private readonly List<ContainerInspectionObservation> cleanupInspections = [];
    private readonly CleanupProofFailures cleanupProofFailures = new();
    private Task cleanupObservation = Task.CompletedTask;
    private Task cleanupObservationCancellation = Task.CompletedTask;
    private bool cleanupPrepared;
    private HashSet<string> initialContainerIds = new(StringComparer.Ordinal);
    private HashSet<string> initialVolumeNames = new(StringComparer.Ordinal);
    private Exception? startupFailure;
    private IResource[] shapeResources = [];
    private System.Reflection.Assembly? shapeAppHostAssembly;
    private bool shapeObservationRequested;
    private ResourcePropertyShapeObservation? propertyShapeObservation;
    private string? selectedAppHostAssemblyFileSha256;
    private ManagedResourceLaunchObservation? managedLaunchObservation;
    internal IReadOnlyDictionary<string, string?> EnvironmentAuthority { get; }

    internal OwnedAppHostFixture(string topology, OwnedAppHostProfile profile = OwnedAppHostProfile.Standard)
    {
        this.topology = topology;
        this.profile = profile;
        RunId = Guid.NewGuid().ToString("N");
        EnvironmentAuthority = CreateEnvironmentAuthority(RunId, profile);
        Root = Path.Combine(Path.GetTempPath(), "islamu-assurance", RunId);
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var markerOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
            markerOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var marker = new FileStream(Path.Combine(Root, TestingResourceIsolation.MarkerName), markerOptions);
        marker.Write(System.Text.Encoding.UTF8.GetBytes(RunId));
    }

    public string RunId { get; }
    public string Root { get; }
    internal bool ExecutionSettled => execution.IsSettled;
    internal string[] OwnedVolumeNames => [.. volumes];
    internal ContainerCleanupIdentity[] CompletedCleanupIdentities
    {
        get { lock (containerIdentityLock) return [.. containerIdentities.Values]; }
    }
    public string BootstrapIdentifier => $"assurance-{RunId}@example.invalid";
    public string BootstrapPassword => EnvironmentAuthority["INSTANCE_BOOTSTRAP_LOCAL_PASSWORD"]
        ?? throw new InvalidOperationException("test_bootstrap_authority_missing");
    public string BootstrapReplacementPassword => EnvironmentAuthority["ISLAMU_TEST_REPLACEMENT_PASSWORD"]
        ?? throw new InvalidOperationException("test_replacement_authority_missing");
    public DistributedApplication Application { get; private set; } = null!;

    public static Task<OwnedAppHostFixture> CreateAsync(string topology, CancellationToken cancellationToken,
        bool configuredAdministrator = false, string deploymentMode = "SingleTenant",
        OwnedAppHostProfile profile = OwnedAppHostProfile.Standard,
        OwnedBffPeerProtection peerProtection = OwnedBffPeerProtection.Shared) =>
        CreateAsync(topology, configuredAdministrator, deploymentMode, null, null, cancellationToken, profile, peerProtection);

    internal static async Task<OwnedAppHostFixture> CreateAsync(string topology,
        bool configuredAdministrator, string deploymentMode,
        IReadOnlyDictionary<string, string?>? untrustedInputs,
        Func<IDistributedApplicationTestingBuilder, Task>? configureBuilder, CancellationToken cancellationToken,
        OwnedAppHostProfile profile = OwnedAppHostProfile.Standard,
        OwnedBffPeerProtection peerProtection = OwnedBffPeerProtection.Shared)
    {
        if (profile == OwnedAppHostProfile.ClosedCompositionModel)
            throw new InvalidOperationException("test_resource_profile_invalid");
        return await CreateCoreAsync(topology, configuredAdministrator, deploymentMode, untrustedInputs,
            configureBuilder, profile, peerProtection, "local-default", "EmbeddedSqlite", null, "Local", cancellationToken);
    }

    internal static async Task InspectCompositionAsync(string? topology, string runMode,
        string privacyErasureTopology, string? eventLocationPrivacyMigrationStage, string webhookProvider,
        Func<IDistributedApplicationTestingBuilder, Task> inspect, CancellationToken cancellationToken)
    {
        var completed = new CompositionInspectionCompleted();
        try
        {
            var unexpectedOwner = await CreateCoreAsync(topology, false, "SingleTenant", null, async builder =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await inspect(builder);
                cancellationToken.ThrowIfCancellationRequested();
                throw completed;
            }, OwnedAppHostProfile.ClosedCompositionModel, OwnedBffPeerProtection.Shared,
                runMode, privacyErasureTopology, eventLocationPrivacyMigrationStage, webhookProvider, cancellationToken);
            var failure = new InvalidOperationException("test_resource_inspection_incomplete");
            try { await unexpectedOwner.DisposeAsync(); }
            catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
            throw failure;
        }
        catch (CompositionInspectionCompleted exception) when (ReferenceEquals(exception, completed)) { }
    }

    private sealed class CompositionInspectionCompleted : Exception { }

    private static async Task<OwnedAppHostFixture> CreateCoreAsync(string? topology,
        bool configuredAdministrator, string deploymentMode,
        IReadOnlyDictionary<string, string?>? untrustedInputs,
        Func<IDistributedApplicationTestingBuilder, Task>? configureBuilder,
        OwnedAppHostProfile profile, OwnedBffPeerProtection peerProtection,
        string runMode, string privacyErasureTopology,
        string? eventLocationPrivacyMigrationStage, string webhookProvider, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(profile) || !Enum.IsDefined(peerProtection)
            || profile == OwnedAppHostProfile.ExternalIdentitySharedBff && topology != "Split"
            || profile != OwnedAppHostProfile.ExternalIdentitySharedBff && peerProtection != OwnedBffPeerProtection.Shared)
            throw new InvalidOperationException("test_resource_profile_invalid");
        var owner = new OwnedAppHostFixture(topology ?? string.Empty, profile);
        IDistributedApplicationTestingBuilder? builder = null;
        try
        {
            builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Explore_AppHost>(
                [
                    $"ISLAMU_TEST_RUN_ID={owner.RunId}", $"ISLAMU_TEST_ROOT={owner.Root}",
                    $"ISLAMU_TEST_PROFILE={profile}",
                    $"Aspire:Store:Path={Path.Combine(owner.Root, "aspire")}",
                    "DOTNET_ENVIRONMENT=Assurance", "ASPNETCORE_ENVIRONMENT=Assurance",
                    $"ISLAMU_ASPIRE_MODE={runMode}", $"Hosting:Topology={topology}",
                    "SECRET_PROVIDER=Environment", "SecretProvider:Provider=Environment",
                    "DATABASE_PROVIDER=PostgreSql", "DATABASE_NAME=islamu_assurance",
                    "DATABASE_SCHEMA=assurance", "DATABASE_HOST=localhost",
                    $"IDENTITY_DATABASE_TOPOLOGY={(profile == OwnedAppHostProfile.ExternalIdentitySharedBff ? "External" : "CoLocated")}",
                    .. (profile == OwnedAppHostProfile.ExternalIdentitySharedBff
                        ? new[] { "IDENTITY_DATABASE_PROVIDER=Sqlite", $"IDENTITY_DATABASE_NAME={Path.Combine(owner.Root, "identity.sqlite")}" }
                        : []),
                    $"PrivacyErasure:Authority:Topology={privacyErasureTopology}", $"ERASURE_DATABASE_TOPOLOGY={privacyErasureTopology}",
                    $"Database:Migrations:EventLocationPrivacyStage={eventLocationPrivacyMigrationStage}",
                    "AUTHENTICATION_PROVIDER=Local", "AUTHORIZATION_PROVIDER=Local",
                    $"DEPLOYMENT_MODE={deploymentMode}",
                    "GEOCODING_PROVIDER=None", "CONFIGURATION_MANIFEST_MODE=Off",
                    "AGENT_BROWSER_SEED_ENABLED=false", $"WEBHOOKS_PROVIDER={webhookProvider}",
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

            if (profile == OwnedAppHostProfile.ClosedCompositionModel)
            {
                await configureBuilder!(builder);
                throw new InvalidOperationException("test_resource_inspection_incomplete");
            }
            builder.Services.AddSingleton(owner.execution);
            if (profile == OwnedAppHostProfile.ExternalIdentitySharedBff)
            {
                var peer = builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == "explore-blazor-peer");
                var input = owner.CreatePeerProtection(peerProtection);
                builder.CreateResourceBuilder(peer)
                    .WithEnvironment("EVENT_DP_BFF_RING", input.Ring)
                    .WithEnvironment("EVENT_DP_BFF_ADMISSION", input.Admission)
                    .WithEnvironment("EVENT_DP_BFF_ADMISSION_SHA256", input.AdmissionSha256)
                    .WithEnvironment("EVENT_DP_BFF_MINIMUM_GENERATION", input.MinimumGeneration.ToString(CultureInfo.InvariantCulture));
            }
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
            await new TestingResourceIsolation(owner.RunId, owner.Root, profile)
                .PublishConfiguredContainerModelAsync(builder, cancellationToken);
            owner.volumes = builder.Resources.OfType<ContainerResource>()
                .SelectMany(resource => resource.Annotations.OfType<ContainerMountAnnotation>())
                .Where(mount => mount.Type == ContainerMountType.Volume)
                .Select(mount => mount.Source!).Distinct(StringComparer.Ordinal).ToArray();
            owner.containerResources = builder.Resources.OfType<ContainerResource>()
                .Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);
            owner.cleanupResources = builder.Resources.OfType<ContainerResource>().ToArray();
            if (owner.volumes.Any(name => !name.StartsWith($"islamu-test-{owner.RunId}-", StringComparison.Ordinal)))
                throw new InvalidOperationException("test_resource_unowned_volume");
            owner.shapeResources = builder.Resources.OfType<ProjectResource>()
                .Where(resource => resource.Name is "explore-api" or "explore-blazor" or "event-standalone"
                    or "event-migrationservice" or "explore-blazor-peer").Cast<IResource>().ToArray();
            owner.shapeAppHostAssembly = builder.AppHostAssembly;
            owner.managedLaunchObservation = new(owner.shapeResources, owner.RunId, owner.Root);
            owner.Application = await builder.BuildAsync(cancellationToken);
            owner.applicationLifetime = owner.Application.Services.GetRequiredService<IHostApplicationLifetime>();
            return owner;
        }
        catch (Exception creationFailure)
        {
            await DisposeFailedCreationAsync(creationFailure, builder, owner);
            throw;
        }
    }

    internal static async Task DisposeFailedCreationAsync(Exception creationFailure,
        IAsyncDisposable? builder, IAsyncDisposable owner)
    {
        if (builder is not null)
        {
            try { await builder.DisposeAsync(); }
            catch (Exception exception) { throw new AggregateException(creationFailure, exception); }
        }
        try { await owner.DisposeAsync(); }
        catch (Exception exception) { throw new AggregateException(creationFailure, exception); }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (profile == OwnedAppHostProfile.ClosedCompositionModel)
            throw new InvalidOperationException("test_resource_profile_invalid");
        lock (disposalLock)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            return runningStartup ??= StartCoreAsync(cancellationToken);
        }
    }

    internal async Task<Guid> AuthenticateBootstrapAsync(HttpClient api, CancellationToken cancellationToken)
    {
        using var initial = await api.PostAsJsonAsync("/api/auth/local/login", new
        {
            identifier = BootstrapIdentifier, password = BootstrapPassword
        }, cancellationToken);
        initial.EnsureSuccessStatusCode();
        using var challenge = JsonDocument.Parse(await initial.Content.ReadAsStringAsync(cancellationToken));
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            challenge.RootElement.GetProperty("replacementChallenge").GetProperty("token").GetString());
        using (var replacement = await api.PostAsJsonAsync("/api/auth/local/credential-replacement",
            new { newPassword = BootstrapReplacementPassword }, cancellationToken))
            await Assert.That(replacement.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        api.DefaultRequestHeaders.Authorization = null;
        using var login = await api.PostAsJsonAsync("/api/auth/local/login", new
        {
            identifier = BootstrapIdentifier, password = BootstrapReplacementPassword
        }, cancellationToken);
        login.EnsureSuccessStatusCode();
        using var authenticated = JsonDocument.Parse(await login.Content.ReadAsStringAsync(cancellationToken));
        await Assert.That(authenticated.RootElement.GetProperty("emailVerified").GetBoolean()).IsTrue();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            authenticated.RootElement.GetProperty("token").GetString());
        using var current = await api.GetAsync("/api/user", cancellationToken);
        current.EnsureSuccessStatusCode();
        using var user = JsonDocument.Parse(await current.Content.ReadAsStringAsync(cancellationToken));
        Guid userId = user.RootElement.GetProperty("id").GetGuid();
        await Assert.That(userId).IsEqualTo(authenticated.RootElement.GetProperty("userId").GetGuid());
        return userId;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            await PrepareContainerCleanupAsync(startup.Token);
            cleanupPrepared = true;
            cleanupObservation = ObserveContainerCleanupCandidatesAsync(cleanupObservationLifetime.Token);
            await RunObservedStartupAsync(RejectFailedResourcesAsync, Application.StartAsync,
                WaitForReadyAsync, applicationLifetime!.StopApplication,
                (phase, reason, requested) => managedLaunchObservation?.RecordStartupFailure(phase, reason, requested,
                    cancellationToken.IsCancellationRequested, lifetime.IsCancellationRequested), startup,
                () => CompleteCleanupProofBeforeStopAsync());
        }
        catch (Exception exception) { startupFailure = exception; }
        if (startupFailure is not null)
        {
            try { await execution.WaitForCompletionAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception exception) { startupFailure = new AggregateException(startupFailure, exception); }
        }
        if (startupFailure is not null)
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    internal static async Task RunObservedStartupAsync(Func<CancellationToken, Task> observe,
        Func<CancellationToken, Task> startApplication, Func<CancellationToken, Task> waitForReady,
        Action stopApplication, Action<string, string, bool> observeFailure, CancellationTokenSource startup,
        Func<Task>? beforeStop = null)
    {
        Task observation = Task.CompletedTask, start = Task.CompletedTask, ready = Task.CompletedTask;
        Task? awaited = null;
        Task? primaryTask = null;
        bool stopAttempted = false;
        bool proofAttempted = false;
        bool failureObserved = false;
        string phase = "observer_arm";
        var failures = new List<Exception>();
        void Retain(Exception exception, string failurePhase)
        {
            if (failures.Any(existing => ReferenceEquals(existing, exception))) return;
            failures.Add(exception);
            if (failureObserved) return;
            failureObserved = true;
            try
            {
                string reason = exception is OperationCanceledException cancellation
                    ? cancellation.CancellationToken == startup.Token ? "canceled_linked" : "canceled_other"
                    : ManagedResourceLaunchObservation.FailureReason(exception);
                observeFailure(failurePhase, reason, startup.IsCancellationRequested);
            }
            catch (Exception observationFailure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, observationFailure)))
                    failures.Add(observationFailure);
            }
        }
        async Task PrepareStopAsync()
        {
            if (proofAttempted) return;
            proofAttempted = true;
            if (beforeStop is null) return;
            try { await beforeStop(); }
            catch (Exception proofFailure) { Retain(proofFailure, "cleanup_proof"); }
        }
        async Task RequestStopAsync()
        {
            if (stopAttempted) return;
            stopAttempted = true;
            await PrepareStopAsync();
            try { stopApplication(); }
            catch (Exception stoppingFailure) { Retain(stoppingFailure, "stop"); }
        }
        try
        {
            // The observer requests its first notification before application startup is invoked.
            observation = observe(startup.Token);
            phase = "application_start_call";
            start = startApplication(startup.Token);
            awaited = await Task.WhenAny(start, observation);
            phase = ReferenceEquals(awaited, observation) ? "observer_wait" : "application_start_wait";
            await awaited;
            awaited = start;
            phase = "application_start_wait";
            await start;
            phase = "startup_token_check";
            startup.Token.ThrowIfCancellationRequested();
            phase = "readiness_call";
            ready = waitForReady(startup.Token);
            awaited = await Task.WhenAny(ready, observation);
            phase = ReferenceEquals(awaited, observation) ? "observer_wait" : "readiness_wait";
            await awaited;
            awaited = ready;
            phase = "readiness_wait";
            await ready;
            awaited = null;
        }
        catch (Exception exception)
        {
            primaryTask = awaited;
            Retain(exception, phase);
            await RequestStopAsync();
        }
        finally
        {
            // Even the helper-owned startup-token cancellation follows the bounded evidence attempt.
            await PrepareStopAsync();
            try { await startup.CancelAsync(); }
            catch (Exception cancellationFailure) { Retain(cancellationFailure, "cancellation_callbacks"); }
            foreach (Task task in new[] { observation, start, ready })
            {
                string joinPhase = ReferenceEquals(task, observation) ? "observer_join"
                    : ReferenceEquals(task, start) ? "application_start_join" : "readiness_join";
                try { await task; }
                catch (OperationCanceledException exception) when (ReferenceEquals(task, observation)
                    && startup.IsCancellationRequested && exception.CancellationToken == startup.Token)
                {
                    // Only the joined observer's requested shutdown is expected.
                }
                catch (Exception exception)
                {
                    if (task.IsFaulted)
                        foreach (Exception independent in task.Exception!.Flatten().InnerExceptions) Retain(independent, joinPhase);
                    else if (!ReferenceEquals(task, primaryTask)) Retain(exception, joinPhase);
                }
            }
        }
        if (failures.Count > 0) await RequestStopAsync();
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private async Task RejectFailedResourcesAsync(CancellationToken cancellationToken)
    {
        await foreach (var update in Application.ResourceNotifications.WatchAsync(cancellationToken))
        {
            propertyShapeObservation?.Capture(update.Resource, update.ResourceId, update.Snapshot.Properties);
            if (update.Snapshot.State?.Text == KnownResourceStates.FailedToStart
                || update.Snapshot.ExitCode is not (null or 0))
                throw new InvalidOperationException($"test_resource_start_failed:{update.Resource.Name}");
            if (managedLaunchObservation is not null)
                await managedLaunchObservation.CaptureAsync(update.Resource, update.ResourceId,
                    update.Snapshot.Properties, cancellationToken);
        }
    }

    internal sealed record ContainerVolume(string Name, string Target, bool Implicit);
    private sealed record ContainerCleanupCandidate(ContainerResource Resource, string Instance, int? ExitCode);
    private sealed record VolumeCleanupIdentity(string Name, string CreatedAt, string Mountpoint);
    internal sealed record ContainerCleanupIdentity(ContainerResource Resource, string Instance,
        string Id, string Image, IReadOnlySet<string> ImageVolumeTargets, string Inspection,
        int MountCount, ContainerVolume[] Volumes, bool Stopped);

    private const string ContainerInspectionFormat =
        "[{{json .ID}},{{json .Name}},{{json .Image}},{{json .Config.Labels}},{{json .Mounts}},{{json .State.Running}},{{json .State.ExitCode}}]";
    private const string VolumeInspectionFormat =
        "[{{json .Name}},{{json .CreatedAt}},{{json .Mountpoint}}]";

    private static bool SafeVolumeName(string? name) => !string.IsNullOrEmpty(name) && name.Length <= 256
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool CanonicalNativePath(string? path) => !string.IsNullOrEmpty(path)
        && path.Length <= 4096 && path.StartsWith('/') && !path.Any(char.IsControl)
        && path == Path.GetFullPath(path) && (path == "/" || !path.EndsWith('/'));

    private static InvalidOperationException UnknownContainer(IResource resource, string reason) =>
        new($"test_resource_container_identity_unknown:{resource.Name}:{reason}");

    private void RecordCleanupCandidate(IResource resource, string? instance, int? exitCode)
    {
        if (resource is not ContainerResource container || !cleanupResources.Any(item => ReferenceEquals(item, resource))
            || string.IsNullOrEmpty(instance)) return;
        if (instance.Length > 256 || !char.IsAsciiLetterOrDigit(instance[0])
            || instance.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw UnknownContainer(resource, "candidate-format");
        lock (containerIdentityLock)
        {
            int index = cleanupCandidates.FindIndex(candidate => ReferenceEquals(candidate.Resource, resource)
                && candidate.Instance == instance);
            var candidate = new ContainerCleanupCandidate(container, instance,
                exitCode ?? (index < 0 ? null : cleanupCandidates[index].ExitCode));
            if (index < 0) cleanupCandidates.Add(candidate);
            else cleanupCandidates[index] = candidate;
        }
    }

    private async Task ObserveContainerCleanupCandidatesAsync(CancellationToken token)
    {
        // No native reads here: requested startup cancellation must not interrupt prospective candidate recording.
        await foreach (var update in Application.ResourceNotifications.WatchAsync(token))
            RecordCleanupCandidate(update.Resource, update.ResourceId, update.Snapshot.ExitCode);
    }

    private async Task CompleteCleanupProofBeforeStopAsync(CancellationToken cancellationToken = default)
    {
        if (!cleanupPrepared) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var failures = new List<Exception>();
        var denials = new List<CleanupProofDenial>();
        var attempted = new List<ContainerCleanupCandidate>();
        while (!deadline.IsCancellationRequested)
        {
            ContainerCleanupCandidate[] pending;
            lock (containerIdentityLock)
                pending = cleanupCandidates.Where(candidate => !attempted.Any(previous =>
                    ReferenceEquals(previous.Resource, candidate.Resource) && previous.Instance == candidate.Instance)).ToArray();
            if (pending.Length == 0) break;
            foreach (var candidate in pending)
            {
                attempted.Add(candidate);
                string stage = "capture";
                try
                {
                    var captured = await CaptureContainerCleanupIdentityAsync(candidate.Resource, candidate.Instance,
                        candidate.ExitCode, deadline.Token);
                    stage = "completed-proof";
                    lock (containerIdentityLock)
                    {
                        if (!containerIdentities.Values.Any(identity => ReferenceEquals(identity.Resource, candidate.Resource)
                            && identity.Instance == candidate.Instance))
                        {
                            if (captured?.Outcome != ContainerInspectionOutcome.Absent)
                                // A candidate alone proves neither native creation nor an erased implicit-volume footprint.
                                throw UnknownContainer(candidate.Resource, "candidate-unproved");
                            // The recorded absent observation grants no deletion authority; final release checks it again.
                        }
                    }
                }
                catch (Exception failure)
                {
                    failures.Add(failure);
                    denials.Add(new(candidate.Resource.Name, stage, failure, candidate.Instance == candidate.Resource.Name));
                }
                if (deadline.IsCancellationRequested) break;
            }
        }
        if (cleanupObservation.IsFaulted)
        {
            try { await cleanupObservation; }
            catch (Exception failure)
            {
                failures.Add(failure);
                denials.Add(new(string.Empty, "observer", failure, false));
            }
        }
        if (deadline.IsCancellationRequested)
            failures.Add(new InvalidOperationException("test_resource_cleanup_proof_deadline"));
        if (failures.Count != 0)
        {
            // A proof deadline/fault is never a benign caller-cancellation result.
            var failure = CreateCleanupProofFailure(cleanupResources.Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal),
                denials, deadline.IsCancellationRequested, cleanupObservation.Status, new AggregateException(failures));
            cleanupProofFailures.Retain(failure);
            throw failure;
        }
    }

    internal sealed record CleanupProofDenial(string Resource, string Stage, Exception Failure, bool CandidateEqualsModel);

    internal static InvalidOperationException CreateCleanupProofFailure(IReadOnlySet<string> resources,
        IReadOnlyList<CleanupProofDenial> denials, bool deadlineRequested, TaskStatus observerStatus, AggregateException original)
    {
        const int maximum = 16;
        string[] identityReasons = ["model-reference", "candidate-format", "native-frame", "container-id",
            "preexisting-container", "candidate-name", "image-id", "owner-label", "resource-label", "mount-frame",
            "mount-target", "image-volume-shape", "volume-name", "preexisting-volume", "missing-model-mount",
            "missing-image-volume", "volume-binding-changed", "native-state", "native-inspection", "image-inspection",
            "image-metadata", "image-volumes", "volume-metadata-missing", "volume-consumers", "volume-metadata-changed",
            "candidate-unproved"];
        string Reason(CleanupProofDenial denial)
        {
            if (denial.Failure is OperationCanceledException) return "canceled";
            if (denial.Failure is TimeoutException) return "timeout";
            if (denial.Failure is IOException) return "io";
            if (denial.Failure is UnauthorizedAccessException) return "access";
            if (denial.Failure is not InvalidOperationException) return "fault";
            string exceptionMessage = denial.Failure.Message;
            if (exceptionMessage.Length > 256) return "guard-unknown";
            foreach (string resource in resources)
            {
                string prefix = $"test_resource_container_identity_unknown:{resource}:";
                if (exceptionMessage.StartsWith(prefix, StringComparison.Ordinal)
                    && identityReasons.Contains(exceptionMessage[prefix.Length..], StringComparer.Ordinal))
                    return "identity-" + exceptionMessage[prefix.Length..];
            }
            return exceptionMessage switch
            {
                "test_resource_container_inventory_failed" => "container-inventory",
                "test_resource_container_identity_invalid" => "container-id-invalid",
                "test_resource_container_inspection_failed" => "container-inspection",
                "test_resource_volume_inspection_failed" => "volume-inspection",
                "test_resource_volume_identity_unknown" => "volume-identity",
                "test_resource_volume_consumers_unknown" => "volume-consumers",
                "test_resource_volume_origin_unknown" => "volume-origin",
                "test_resource_foreign_container" => "foreign-consumer",
                _ => "guard-unknown"
            };
        }
        string Row(CleanupProofDenial denial)
        {
            string resource = resources.Contains(denial.Resource) && denial.Resource.Length is > 0 and <= 64
                && denial.Resource.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                ? denial.Resource : "unknown";
            string stage = denial.Stage is "capture" or "completed-proof" or "observer" ? denial.Stage : "unknown";
            return $"{resource}/{stage}/{Reason(denial)}/model-equal={(denial.CandidateEqualsModel ? 1 : 0)}";
        }
        string observer = observerStatus switch
        {
            TaskStatus.RanToCompletion => "complete",
            TaskStatus.Canceled => "canceled",
            TaskStatus.Faulted => "faulted",
            TaskStatus.Created or TaskStatus.WaitingForActivation or TaskStatus.WaitingToRun
                or TaskStatus.Running or TaskStatus.WaitingForChildrenToComplete => "pending",
            _ => "unknown"
        };
        string summary = string.Join(';', denials.Take(maximum).Select(Row));
        string message = FormattableString.Invariant(
            $"test_resource_cleanup_proof_incomplete:budget={(deadlineRequested ? 1 : 0)}:observer={observer}:denials={summary}:omitted={Math.Max(0, denials.Count - maximum)}");
        return new InvalidOperationException(message, original);
    }

    private async Task JoinCleanupObservationAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failures = new List<Exception>();
        try
        {
            cleanupObservationCancellation = cleanupObservationLifetime.CancelAsync();
            await cleanupObservationCancellation.WaitAsync(deadline.Token);
        }
        catch (Exception failure) { failures.Add(failure); }
        try { await cleanupObservation.WaitAsync(deadline.Token); }
        catch (OperationCanceledException failure) when (cleanupObservation.IsCanceled
            && failure.CancellationToken == cleanupObservationLifetime.Token) { }
        catch (Exception failure) { failures.Add(failure); }
        if (cleanupObservation.IsCompleted && cleanupObservationCancellation.IsCompleted)
        {
            try { await CompleteCleanupProofBeforeStopAsync(deadline.Token); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0)
            throw new InvalidOperationException("test_resource_cleanup_observer_unsettled", new AggregateException(failures));
    }

    private static string? DockerIdentity(string? value)
    {
        if (value is null) return null;
        if (value.StartsWith("sha256:", StringComparison.Ordinal)) value = value[7..];
        return value.Length == 64 && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
    }

    private async Task PrepareContainerCleanupAsync(CancellationToken cancellationToken)
    {
        var inventory = await RunDockerAsync(["ps", "--no-trunc", "-aq"], cancellationToken);
        if (inventory.ExitCode != 0) throw new InvalidOperationException("test_resource_container_inventory_failed");
        initialContainerIds = new(StringComparer.Ordinal);
        foreach (string row in inventory.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            initialContainerIds.Add(DockerIdentity(row)
                ?? throw new InvalidOperationException("test_resource_container_identity_invalid"));
        var volumeInventory = await RunDockerAsync(["volume", "ls", "--format", "{{.Name}}"], cancellationToken);
        if (volumeInventory.ExitCode != 0)
            throw new InvalidOperationException("test_resource_volume_inventory_failed");
        initialVolumeNames = new(StringComparer.Ordinal);
        foreach (string name in volumeInventory.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!SafeVolumeName(name)) throw new InvalidOperationException("test_resource_volume_identity_invalid");
            initialVolumeNames.Add(name);
        }
        foreach (var resource in cleanupResources)
        {
            if (!resource.TryGetContainerImageName(false, out string? image) || image is null)
                throw new InvalidOperationException("test_resource_container_image_unknown");
            var inspected = await RunDockerAsync(["image", "inspect", "--format",
                "[{{json .ID}},{{json .Config.Volumes}}]", image], cancellationToken);
            if (inspected.ExitCode != 0) throw UnknownContainer(resource, "image-inspection");
            try
            {
                using var document = JsonDocument.Parse(inspected.Output);
                var fields = document.RootElement;
                if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 2
                    || DockerIdentity(fields[0].GetString()) is not { } id)
                    throw UnknownContainer(resource, "image-metadata");
                var targets = new HashSet<string>(StringComparer.Ordinal);
                if (fields[1].ValueKind != JsonValueKind.Null)
                {
                    if (fields[1].ValueKind != JsonValueKind.Object) throw UnknownContainer(resource, "image-volumes");
                    foreach (var target in fields[1].EnumerateObject())
                        if (!CanonicalNativePath(target.Name) || !targets.Add(target.Name))
                            throw UnknownContainer(resource, "image-volumes");
                }
                cleanupImages.Add(resource.Name, (id, targets));
            }
            catch (Exception failure) when (failure is JsonException or ArgumentException
                || failure is InvalidOperationException && !failure.Message.StartsWith("test_resource_", StringComparison.Ordinal))
            { throw UnknownContainer(resource, "image-metadata"); }
        }
        foreach (string volume in volumes)
        {
            var existing = await RunDockerAsync(["volume", "inspect", "--format", "{{.Name}}", volume], cancellationToken);
            if (existing.ExitCode == 0 || !existing.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("test_resource_volume_origin_unknown");
        }
    }

    internal static bool TryReadContainerIdentity(IReadOnlyList<ContainerResource> resources,
        IResource resource, string? instance, string runId, string expectedImage,
        IReadOnlySet<string> imageVolumeTargets, IReadOnlySet<string> initialIds,
        IReadOnlySet<string> initialVolumes, string inspection, out ContainerCleanupIdentity? identity,
        out string reason, ContainerCleanupIdentity? original = null)
    {
        identity = null;
        reason = "model-reference";
        if (resource is not ContainerResource container || !resources.Any(item => ReferenceEquals(item, resource))) return false;
        reason = "candidate-format";
        if (string.IsNullOrEmpty(instance) || instance.Length > 256
            || !char.IsAsciiLetterOrDigit(instance[0])
            || instance.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            return false;
        try
        {
            reason = "native-frame";
            using var document = JsonDocument.Parse(inspection);
            var fields = document.RootElement;
            if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 7) return false;
            string? id = DockerIdentity(fields[0].GetString());
            reason = "container-id";
            if (id is null) return false;
            reason = "preexisting-container";
            if (initialIds.Contains(id)) return false;
            reason = "candidate-name";
            string? name = fields[1].GetString();
            if (name?.StartsWith('/') == true) name = name[1..];
            if (name != instance) return false;
            reason = "image-id";
            if (DockerIdentity(fields[2].GetString()) != expectedImage) return false;
            reason = "owner-label";
            RequireOwnedContainer(runId, resources.Select(item => item.Name).ToHashSet(StringComparer.Ordinal), fields[3].GetRawText());
            reason = "resource-label";
            if (fields[3].GetProperty("islamu.assurance.resource").GetString() != container.Name) return false;
            var expectedMounts = container.Annotations.OfType<ContainerMountAnnotation>().ToArray();
            var mounts = fields[4];
            reason = "mount-frame";
            if (mounts.ValueKind != JsonValueKind.Array) return false;
            var remaining = expectedMounts.ToList();
            var remainingTargets = imageVolumeTargets.Where(target => !expectedMounts.Any(mount => mount.Target == target))
                .ToHashSet(StringComparer.Ordinal);
            var volumeMounts = new List<ContainerVolume>();
            foreach (var mount in mounts.EnumerateArray())
            {
                reason = "mount-frame";
                string? type = mount.GetProperty("Type").GetString();
                string? source = mount.GetProperty(type == "volume" ? "Name" : "Source").GetString();
                string? target = mount.GetProperty("Destination").GetString();
                bool writable = mount.GetProperty("RW").GetBoolean();
                var selected = remaining.SingleOrDefault(expected =>
                    (expected.Type == ContainerMountType.Volume && type == "volume"
                        || expected.Type == ContainerMountType.BindMount && type == "bind")
                    && expected.Source == source && expected.Target == target && expected.IsReadOnly != writable);
                bool implicitVolume = selected is null;
                if (selected is not null) remaining.Remove(selected);
                else
                {
                    reason = "mount-target";
                    if (target is null || !remainingTargets.Remove(target)) return false;
                    reason = "image-volume-shape";
                    if (type != "volume" || !writable) return false;
                }
                if (type == "volume")
                {
                    reason = "volume-name";
                    if (!SafeVolumeName(source) || target is null) return false;
                    reason = "preexisting-volume";
                    if (initialVolumes.Contains(source!)) return false;
                    volumeMounts.Add(new(source!, target, implicitVolume));
                }
            }
            reason = "missing-model-mount";
            if (remaining.Count != 0) return false;
            reason = "missing-image-volume";
            if (remainingTargets.Count != 0) return false;
            var orderedVolumes = volumeMounts.OrderBy(volume => volume.Target, StringComparer.Ordinal).ToArray();
            reason = "volume-binding-changed";
            if (original is not null && (original.Id != id || !original.Volumes.SequenceEqual(orderedVolumes))) return false;
            reason = "native-state";
            bool stopped = !fields[5].GetBoolean() && fields[6].GetInt32() == 0;
            identity = new(container, instance, id, expectedImage, imageVolumeTargets, inspection,
                mounts.GetArrayLength(), orderedVolumes, stopped);
            reason = string.Empty;
            return true;
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    internal enum ContainerInspectionOutcome { Available, Absent }

    internal sealed record ContainerInspectionObservation(ContainerResource Resource, string Instance, ContainerInspectionOutcome Outcome);

    internal sealed class CleanupProofFailures
    {
        private readonly Lock sync = new();
        private readonly List<Exception> failures = [];

        internal void Retain(Exception failure)
        {
            lock (sync)
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
        }

        internal Exception[] Snapshot()
        {
            lock (sync) return [.. failures];
        }

        internal ContainerInspectionObservation ClassifyInspection(ContainerResource resource, string instance, int exitCode, string error)
        {
            try { return ClassifyContainerInspection(resource, instance, exitCode, error); }
            catch (Exception failure)
            {
                Retain(failure);
                throw;
            }
        }
    }

    internal static ContainerInspectionObservation ClassifyContainerInspection(ContainerResource resource, string instance,
        int exitCode, string error)
    {
        if (exitCode == 0) return new(resource, instance, ContainerInspectionOutcome.Available);
        if (error.Contains("no such", StringComparison.OrdinalIgnoreCase))
            return new(resource, instance, ContainerInspectionOutcome.Absent);
        throw UnknownContainer(resource, "native-inspection");
    }

    private async Task<ContainerInspectionObservation?> CaptureContainerCleanupIdentityAsync(IResource resource, string? instance,
        int? exitCode, CancellationToken cancellationToken)
    {
        try { return await CaptureContainerCleanupIdentityCoreAsync(resource, instance, exitCode, cancellationToken); }
        catch (Exception failure)
        {
            // A later absent result must not erase an earlier native/metadata/consumer refusal.
            cleanupProofFailures.Retain(failure);
            if (failure is OperationCanceledException)
                cleanupProofFailures.Retain(new InvalidOperationException("test_resource_cleanup_capture_incomplete", failure));
            throw;
        }
    }

    private async Task<ContainerInspectionObservation?> CaptureContainerCleanupIdentityCoreAsync(IResource resource, string? instance,
        int? exitCode, CancellationToken cancellationToken)
    {
        if (resource is not ContainerResource container || !cleanupResources.Any(item => ReferenceEquals(item, resource))
            || string.IsNullOrEmpty(instance)) return null;
        if (instance.Length > 256 || !char.IsAsciiLetterOrDigit(instance[0])
            || instance.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw UnknownContainer(resource, "candidate-format");
        var inspection = await RunDockerAsync(["inspect", "--type", "container", "--format",
            ContainerInspectionFormat, instance], cancellationToken);
        var observation = cleanupProofFailures.ClassifyInspection(container, instance, inspection.ExitCode, inspection.Error);
        lock (containerIdentityLock)
        {
            int index = cleanupInspections.FindIndex(previous => ReferenceEquals(previous.Resource, resource)
                && previous.Instance == instance);
            if (index < 0) cleanupInspections.Add(observation);
            else cleanupInspections[index] = observation;
        }
        if (observation.Outcome == ContainerInspectionOutcome.Absent) return observation;
        ContainerCleanupIdentity? original;
        lock (containerIdentityLock)
            original = containerIdentities.Values.SingleOrDefault(identity =>
                ReferenceEquals(identity.Resource, resource) && identity.Instance == instance);
        // SDK ResourceId is only a name candidate; the native equality check supplies the join.
        if (!TryReadContainerIdentity(cleanupResources, resource, instance, RunId,
            cleanupImages[container.Name].Id, cleanupImages[container.Name].VolumeTargets,
            initialContainerIds, initialVolumeNames, inspection.Output, out var identity, out string reason, original))
            throw UnknownContainer(resource, reason);
        if (original is null)
        {
            var capturedVolumes = new List<VolumeCleanupIdentity>();
            foreach (var volume in identity!.Volumes)
            {
                var observed = await ReadVolumeIdentityAsync(volume.Name, cancellationToken)
                    ?? throw UnknownContainer(resource, "volume-metadata-missing");
                string[] consumers = await ReadVolumeConsumersAsync(observed, cancellationToken);
                if (consumers.Length != 1 || consumers[0] != identity.Id)
                    throw UnknownContainer(resource, "volume-consumers");
                capturedVolumes.Add(observed);
            }
            lock (containerIdentityLock)
            {
                foreach (var volume in capturedVolumes)
                    if (volumeIdentities.TryGetValue(volume.Name, out var previous) && previous != volume)
                        throw UnknownContainer(resource, "volume-metadata-changed");
                foreach (var volume in capturedVolumes)
                    volumeIdentities[volume.Name] = volume;
            }
        }
        lock (containerIdentityLock)
            containerIdentities[identity!.Id] = identity with { Stopped = identity.Stopped && exitCode == 0 };
        return observation;
    }

    private static async Task<VolumeCleanupIdentity?> ReadVolumeIdentityAsync(string name, CancellationToken token)
    {
        var inspected = await RunDockerAsync(["volume", "inspect", "--format", VolumeInspectionFormat, name], token);
        if (inspected.ExitCode != 0)
        {
            if (inspected.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase)) return null;
            throw new InvalidOperationException("test_resource_volume_inspection_failed");
        }
        try
        {
            using var document = JsonDocument.Parse(inspected.Output);
            var fields = document.RootElement;
            if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 3
                || fields[0].GetString() != name || !SafeVolumeName(name)
                || fields[1].GetString() is not { Length: > 0 and <= 128 } created
                || created.Any(char.IsControl)
                || fields[2].GetString() is not { } mountpoint || !CanonicalNativePath(mountpoint) || mountpoint == "/")
                throw new InvalidOperationException("test_resource_volume_identity_unknown");
            // This tuple is observed metadata, not a unique or immutable volume-generation identifier.
            return new(name, created, mountpoint);
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException or InvalidOperationException)
        { throw new InvalidOperationException("test_resource_volume_identity_unknown"); }
    }

    internal static bool NativePathsOverlap(string first, string second) => first == second
        || first.StartsWith(second == "/" ? "/" : second + "/", StringComparison.Ordinal)
        || second.StartsWith(first == "/" ? "/" : first + "/", StringComparison.Ordinal);

    internal static void RequireVolumeConsumerInspectionSucceeded(int exitCode, string error)
    {
        if (exitCode == 0) return;
        throw VolumeConsumerInspectionFailure(exitCode, error);
    }

    private static string VolumeConsumerInspectionCategory(string error)
    {
        if (error.Length > 8192) return "oversize";
        if (string.IsNullOrWhiteSpace(error)) return "empty";
        // Marker observations cannot diagnose absence or authorize deletion.
        bool missing = error.Contains("no such", StringComparison.OrdinalIgnoreCase);
        bool permission = error.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || error.Contains("access denied", StringComparison.OrdinalIgnoreCase);
        bool connection = error.Contains("cannot connect", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || error.Contains("connection reset", StringComparison.OrdinalIgnoreCase);
        int classes = (missing ? 1 : 0) + (permission ? 1 : 0) + (connection ? 1 : 0);
        return classes > 1 ? "multiple-markers" : missing ? "no-such-marker"
            : permission ? "permission-marker" : connection ? "connection-marker" : "other";
    }

    private static InvalidOperationException VolumeConsumerInspectionFailure(int exitCode, string error) => new(FormattableString.Invariant(
        $"test_resource_volume_consumers_unknown:stage=bulk-inspect:exit={exitCode}:stderr={VolumeConsumerInspectionCategory(error)}"));

    private static Task<string[]> ReadVolumeConsumersAsync(VolumeCleanupIdentity volume, CancellationToken token) =>
        ReadVolumeConsumersAsync(volume.Name, volume.Mountpoint, (arguments, cancellation) => RunDockerAsync(arguments, cancellation), token);

    internal static async Task<string[]> ReadVolumeConsumersAsync(string volumeName, string mountpoint,
        Func<string[], CancellationToken, Task<(int ExitCode, string Output, string Error)>> execute, CancellationToken token)
    {
        var superseded = new List<Exception>();
        async Task<string[]> InventoryAsync()
        {
            token.ThrowIfCancellationRequested();
            var inventory = await execute(["ps", "--no-trunc", "-aq"], token);
            if (inventory.ExitCode != 0) throw new InvalidOperationException("test_resource_container_inventory_failed");
            string[] ids = inventory.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Any(id => DockerIdentity(id) != id || !unique.Add(id)))
                throw new InvalidOperationException("test_resource_container_identity_invalid");
            return ids;
        }
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string[] ids = await InventoryAsync();
                string[] consumers = [];
                if (ids.Length != 0)
                {
                    var inspected = await execute(["inspect", "--type", "container", "--format",
                        "[{{json .ID}},{{json .Mounts}}]", .. ids], token);
                    if (inspected.ExitCode != 0)
                    {
                        var failure = VolumeConsumerInspectionFailure(inspected.ExitCode, inspected.Error);
                        if (attempt == 0 && inspected.ExitCode == 1
                            && VolumeConsumerInspectionCategory(inspected.Error) == "no-such-marker")
                        {
                            // Discard all failed stdout. Only an independently complete fresh proof can supersede it.
                            superseded.Add(failure);
                            string[] afterFailure = await InventoryAsync();
                            if (!ids.ToHashSet(StringComparer.Ordinal).SetEquals(afterFailure))
                                continue;
                        }
                        throw failure;
                    }
                    consumers = ParseVolumeConsumers(volumeName, mountpoint, ids, inspected.Output);
                }
                string[] after = await InventoryAsync();
                token.ThrowIfCancellationRequested();
                if (ids.ToHashSet(StringComparer.Ordinal).SetEquals(after)) return consumers;
                var changed = new InvalidOperationException("test_resource_volume_consumers_unknown:stage=inventory-bracket:reason=membership-changed");
                if (attempt == 1) throw changed;
                superseded.Add(changed);
            }
            throw new InvalidOperationException("test_resource_volume_consumers_unknown:stage=inventory-bracket:reason=incomplete");
        }
        catch (Exception failure)
        {
            if (superseded.Count == 0) throw;
            if (!superseded.Any(existing => ReferenceEquals(existing, failure))) superseded.Add(failure);
            throw new AggregateException(superseded);
        }
    }

    private static string[] ParseVolumeConsumers(string volumeName, string mountpoint, string[] ids, string output)
    {
        var consumers = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                var fields = document.RootElement;
                if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != 2
                    || fields[0].GetString() is not { } id || !ids.Contains(id, StringComparer.Ordinal) || !seen.Add(id)
                    || fields[1].ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                foreach (var mount in fields[1].EnumerateArray())
                {
                    if (mount.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                    string? type = mount.GetProperty("Type").GetString();
                    if (type == "volume")
                    {
                        string? name = mount.GetProperty("Name").GetString();
                        if (!SafeVolumeName(name)) throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                        if (name == volumeName) consumers.Add(id);
                    }
                    else if (type == "bind")
                    {
                        string? source = mount.GetProperty("Source").GetString();
                        if (!CanonicalNativePath(source)) throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                        if (NativePathsOverlap(source!, mountpoint)) consumers.Add(id);
                    }
                    else if (type == "tmpfs")
                    {
                        if (mount.GetProperty("Source").GetString() != string.Empty
                            || mount.TryGetProperty("Name", out var name)
                                && (name.ValueKind != JsonValueKind.String || name.GetString() != string.Empty))
                            throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                    }
                    else throw new InvalidOperationException("test_resource_volume_consumers_unknown");
                }
            }
            if (seen.Count != ids.Length) throw new InvalidOperationException("test_resource_volume_consumers_unknown");
            return consumers.ToArray();
        }
        catch (Exception failure) when (failure is JsonException or ArgumentException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidOperationException("test_resource_volume_consumers_unknown"); }
    }

    internal static void RequireExclusiveVolumeConsumer(string ownedContainerId, IReadOnlyList<string> consumers)
    {
        if (consumers.Any(consumer => consumer != ownedContainerId))
            throw new InvalidOperationException("test_resource_foreign_container");
    }

    internal async Task<ContainerCleanupIdentity> WaitForOwnedContainerAsync(string resource,
        bool requireStopped, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cleanupObservation.IsFaulted) await cleanupObservation;
            ContainerCleanupCandidate? candidate;
            lock (containerIdentityLock)
            {
                var identity = containerIdentities.Values.FirstOrDefault(item => item.Resource.Name == resource
                    && (!requireStopped || item.Stopped));
                if (identity is not null) return identity;
                candidate = cleanupCandidates.LastOrDefault(item => item.Resource.Name == resource);
            }
            if (candidate is not null)
                await CaptureContainerCleanupIdentityAsync(candidate.Resource, candidate.Instance,
                    candidate.ExitCode, cancellationToken);
            if (runningStartup?.IsCompleted == true) await runningStartup;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    internal async Task BeginResourcePropertyShapeObservationAsync(CancellationToken cancellationToken)
    {
        System.Reflection.Assembly assembly;
        lock (disposalLock)
        {
            if (disposal is not null || runningStartup is not null || shapeObservationRequested
                || shapeAppHostAssembly is null)
                throw new InvalidOperationException("test_resource_shape_observation_invalid_state");
            shapeObservationRequested = true;
            assembly = shapeAppHostAssembly;
        }
        string digest;
        try
        {
            string location = assembly.Location;
            if (string.IsNullOrEmpty(location) || !File.Exists(location))
                throw new InvalidOperationException("test_resource_shape_assembly_unavailable");
            await using var input = File.OpenRead(location);
            digest = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw new InvalidOperationException("test_resource_shape_assembly_unavailable"); }
        finally
        {
            lock (disposalLock) shapeAppHostAssembly = null;
        }
        lock (disposalLock)
        {
            if (disposal is not null || runningStartup is not null)
                throw new InvalidOperationException("test_resource_shape_observation_invalid_state");
            selectedAppHostAssemblyFileSha256 = digest;
            propertyShapeObservation = new(shapeResources);
        }
    }

    internal async Task CompleteResourcePropertyShapeObservationAsync(Exception? originalFailure,
        CancellationToken cancellationToken)
    {
        Exception? failure = originalFailure;
        try
        {
            ResourcePropertyShapeObservation observation;
            string digest;
            lock (disposalLock)
            {
                if (disposal is not null || runningStartup is null || !runningStartup.IsCompleted
                    || propertyShapeObservation is null || selectedAppHostAssemblyFileSha256 is null)
                    throw new InvalidOperationException("test_resource_shape_write_invalid_state");
                observation = propertyShapeObservation;
                digest = selectedAppHostAssemblyFileSha256;
            }
            await WritePropertyShapeEvidenceAsync(RunId, digest, observation, cancellationToken);
        }
        catch (Exception writerFailure)
        {
            failure = failure is null ? writerFailure : new AggregateException(failure, writerFailure);
        }
        if (managedLaunchObservation is not null)
            await RetainManagedLaunchEvidenceAsync(managedLaunchObservation, failure, cancellationToken);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal Task CompleteManagedLaunchObservationAsync(Exception? originalFailure, CancellationToken cancellationToken)
    {
        if (managedLaunchObservation is not null)
            return RetainManagedLaunchEvidenceAsync(managedLaunchObservation, originalFailure, cancellationToken);
        Exception unavailable = new InvalidOperationException("test_managed_launch_observation_unavailable");
        return Task.FromException(originalFailure is null ? unavailable : new AggregateException(originalFailure, unavailable));
    }

    internal static async Task RetainManagedLaunchEvidenceAsync(ManagedResourceLaunchObservation observation,
        Exception? originalFailure, CancellationToken cancellationToken)
    {
        Exception? failure = originalFailure;
        try { await observation.RetainAsync(cancellationToken); }
        catch (Exception writerFailure)
        { failure = failure is null ? writerFailure : new AggregateException(failure, writerFailure); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    internal static async Task WritePropertyShapeEvidenceAsync(string runId, string appHostFileDigest,
        ResourcePropertyShapeObservation observation, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("test_resource_shape_platform_unsupported");
        cancellationToken.ThrowIfCancellationRequested();
        if (runId.Length != 32 || !runId.All(char.IsAsciiHexDigit)
            || appHostFileDigest.Length != 64 || !appHostFileDigest.All(char.IsAsciiHexDigit))
            throw new InvalidOperationException("test_resource_shape_identity_invalid");
        try
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            for (DirectoryInfo? component = new(temporaryRoot); component is not null; component = component.Parent)
                if (!component.Exists || component.LinkTarget is not null
                    || (component.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("test_resource_shape_root_untrusted");
            const UnixFileMode ownerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if ((File.GetUnixFileMode(temporaryRoot) & ~ownerDirectory) != 0)
                throw new InvalidOperationException("test_resource_shape_root_untrusted");
            string directory = Path.Combine(temporaryRoot, $"islamu-assurance-resource-shapes-{runId}");
            var destination = new DirectoryInfo(directory);
            if (destination.Exists || File.Exists(directory) || destination.LinkTarget is not null)
                throw new InvalidOperationException("test_resource_shape_destination_exists");
            Directory.CreateDirectory(directory, ownerDirectory);
            destination.Refresh();
            if (destination.LinkTarget is not null || (destination.Attributes & FileAttributes.ReparsePoint) != 0
                || File.GetUnixFileMode(directory) != ownerDirectory)
                throw new InvalidOperationException("test_resource_shape_destination_untrusted");
            string path = Path.Combine(directory, "shapes.json");
            if (File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                throw new InvalidOperationException("test_resource_shape_destination_exists");
            await using var output = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            await JsonSerializer.SerializeAsync(output, new
            {
                Schema = 1, RunId = runId, SelectedAppHostAssemblyFileSha256 = appHostFileDigest,
                observation.LaunchInputs, observation.Rows, observation.HasOmittedPropertyShapes
            }, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("test_resource_shape_write_cancelled", cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException("test_resource_shape_write_failed");
        }
    }

    internal sealed record ManagedProcessIdentity(int Pid, int Parent, uint User, int Group, int Session,
        ulong Start, char State);
    internal sealed record ManagedFileInput(string Path, long Bytes, string Sha256, long LastWriteUtcTicks,
        long CreationUtcTicks);
    internal sealed record ManagedProcessInputs(ManagedProcessIdentity[] Ancestry, string Executable,
        string WorkingDirectory, string Entry, ManagedFileInput[] Files);
    internal sealed record ManagedLaunchHint(string Resource, string InstanceSha256, int Pid, ulong Start, string Diagnostic,
        ManagedProcessInputs? Inputs);

    internal sealed class ManagedResourceLaunchObservation(IReadOnlyList<IResource> selected, string runId, string root)
    {
        private readonly Dictionary<IResource, Dictionary<(string Instance, int Pid, ulong Start), ManagedLaunchHint>>
            observations = new(ReferenceEqualityComparer.Instance);
        private int records;
        internal IReadOnlyList<ManagedLaunchHint> Rows => observations.Values.SelectMany(history => history.Values).ToArray();
        internal string StartupFailureDiagnostic { get; private set; } = "not-observed";

        internal void RecordStartupFailure(string phase, string reason, bool linkedCancellationRequested,
            bool callerCancellationRequested, bool fixtureCancellationRequested)
        {
            if (StartupFailureDiagnostic != "not-observed") return;
            StartupFailureDiagnostic = FormattableString.Invariant(
                $"{phase}:{reason}:linked={(linkedCancellationRequested ? 1 : 0)}:caller={(callerCancellationRequested ? 1 : 0)}:fixture={(fixtureCancellationRequested ? 1 : 0)}");
        }

        internal static string FailureReason(Exception exception)
        {
            string[] fields = exception is InvalidOperationException && exception.Message.Length <= 128
                ? exception.Message.Split(':') : [];
            bool closedInputReason = fields.Length == 6 && fields[0] == "test_managed_input_invalid"
                && fields[1] is "executable" or "entry" or "deps" or "runtimeconfig"
                && fields[2] is "before" or "after" && fields[3] is "missing" or "link" or "reparse"
                && fields[4] is "file" or "ancestor"
                && int.TryParse(fields[5], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int depth) && depth is >= 0 and <= 4096;
            if (closedInputReason) return string.Join(':', fields.Skip(1));
            return exception switch
            {
                InvalidOperationException { Message: "test_managed_process_not_owned" } => "process_not_owned",
                InvalidOperationException { Message: "test_managed_process_changed" } => "process_changed",
                InvalidOperationException { Message: "test_managed_inputs_changed" } => "inputs_changed",
                InvalidOperationException { Message: "test_managed_entry_unsupported" } => "entry_unsupported",
                InvalidOperationException { Message: "test_managed_process_identity_invalid" } => "identity_invalid",
                InvalidOperationException { Message: "test_managed_process_path_invalid" } => "path_invalid",
                InvalidOperationException { Message: "test_managed_process_input_limit" } => "process_input_limit",
                InvalidOperationException { Message: "test_managed_input_invalid" } => "input_invalid",
                InvalidOperationException { Message: "test_managed_input_limit" } => "input_limit",
                InvalidOperationException invalid when invalid.Message.StartsWith("test_resource_start_failed:", StringComparison.Ordinal)
                    => "resource_start_failed",
                IOException => "io",
                UnauthorizedAccessException => "access",
                ArgumentException => "argument",
                _ => "guard_unclassified"
            };
        }

        internal static bool TryGetPid(IReadOnlyList<IResource> resources, IResource resource, string? instance,
            ImmutableArray<ResourcePropertySnapshot> properties, out int pid)
        {
            pid = 0;
            if (!resources.Any(item => ReferenceEquals(item, resource)) || string.IsNullOrEmpty(instance)
                || instance.Length > 256 || properties.IsDefaultOrEmpty || properties.Length > 4096) return false;
            int matches = 0;
            foreach (var property in properties)
            {
                if (property.Name != "executable.pid") continue;
                if (++matches != 1 || property.IsSensitive || property.Value is not int value || value <= 0)
                    return false;
                pid = value;
            }
            return matches == 1;
        }

        internal async Task CaptureAsync(IResource resource, string? instance,
            ImmutableArray<ResourcePropertySnapshot> properties, CancellationToken cancellationToken)
        {
            if (!selected.Any(item => ReferenceEquals(item, resource))) return;
            if (!OperatingSystem.IsLinux()) return;
            if (!TryGetPid(selected, resource, instance, properties, out int pid)) return;
            string digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(instance!)));
            ManagedProcessIdentity before;
            try
            {
                before = await ReadIdentityAsync(pid, cancellationToken);
                var owner = await ReadIdentityAsync(Environment.ProcessId, cancellationToken);
                if (before.Start == 0 || before.State is 'Z' or 'X' || before.User != owner.User) return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // Missing or unreadable kernel identity cannot supply a positive incarnation lead.
                return;
            }
            if (HasObservation(resource, digest, pid, before.Start)) return;
            ManagedProcessInputs? inputs = null;
            string diagnostic = "managed_launch_inputs_unavailable";
            try
            {
                inputs = await ReadProcessInputsAsync(pid, Environment.ProcessId, cancellationToken);
                diagnostic = "managed_launch_os_inputs_observed";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // Expected OS gaps are an explicit observation outcome, never launch-success evidence.
                diagnostic = $"managed_launch_inputs_unavailable:{FailureReason(exception)}";
            }
            try
            {
                var after = await ReadIdentityAsync(pid, cancellationToken);
                if (!SameAncestry([before], [after]) || inputs is not null
                    && (inputs.Ancestry.Length == 0 || !SameAncestry([before], [inputs.Ancestry[0]]))) return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // A lost or changed process cannot be reconstructed from its earlier snapshot.
                return;
            }
            await PublishObservedIncarnationAsync(resource, digest, pid, before.Start, diagnostic, inputs, cancellationToken);
        }

        private bool HasObservation(IResource resource, string digest, int pid, ulong start) =>
            observations.TryGetValue(resource, out var history) && history.ContainsKey((digest, pid, start));

        internal async Task PublishObservedIncarnationAsync(IResource resource, string digest, int pid, ulong start,
            string diagnostic, ManagedProcessInputs? inputs, CancellationToken cancellationToken)
        {
            if (!selected.Any(item => ReferenceEquals(item, resource))) return;
            if (pid <= 0 || start == 0 || inputs is not null && (inputs.Ancestry.Length == 0
                || inputs.Ancestry[0].Pid != pid || inputs.Ancestry[0].Start != start))
                throw new InvalidOperationException("test_managed_launch_hint_invalid");
            if (HasObservation(resource, digest, pid, start)) return;
            if (records >= 64) throw new InvalidOperationException("test_managed_launch_observation_limit");
            var hint = new ManagedLaunchHint(resource.Name, digest, pid, start, diagnostic, inputs);
            VerifyPrivateRoot(root, runId);
            string name = $"managed-launch-{digest}-{pid}-{start}-{records}.json";
            await WritePrivateJsonAsync(Path.Combine(root, name), new { Schema = 1, RunId = runId, Hint = hint }, cancellationToken);
            if (!observations.TryGetValue(resource, out var history))
            {
                history = [];
                observations.Add(resource, history);
            }
            history.Add((digest, pid, start), hint);
            records++;
        }

        internal static async Task<ManagedProcessInputs> ReadProcessInputsAsync(int pid, int owner,
            CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsLinux() || pid <= 0 || pid == owner)
                throw new InvalidOperationException("test_managed_process_not_owned");
            ManagedProcessIdentity[] ancestry = await ReadAncestryAsync(pid, owner, cancellationToken);
            string executable = ProcLink(pid, "exe");
            string cwd = ProcLink(pid, "cwd");
            string entry = ParseManagedEntry(await ReadBoundedTextAsync($"/proc/{pid}/cmdline", 65_536, cancellationToken), cwd, executable);
            string[] files = [executable, entry, Path.ChangeExtension(entry, ".deps.json"), Path.ChangeExtension(entry, ".runtimeconfig.json")];
            string[] kinds = ["executable", "entry", "deps", "runtimeconfig"];
            var before = new List<ManagedFileInput>();
            for (int index = 0; index < files.Length; index++)
                before.Add(await HashInputAsync(files[index], kinds[index], "before", cancellationToken));
            ManagedProcessIdentity[] after = await ReadAncestryAsync(pid, owner, cancellationToken);
            if (!SameAncestry(ancestry, after) || ProcLink(pid, "exe") != executable || ProcLink(pid, "cwd") != cwd
                || ParseManagedEntry(await ReadBoundedTextAsync($"/proc/{pid}/cmdline", 65_536, cancellationToken), cwd, executable) != entry)
                throw new InvalidOperationException("test_managed_process_changed");
            var afterFiles = new List<ManagedFileInput>();
            for (int index = 0; index < files.Length; index++)
                afterFiles.Add(await HashInputAsync(files[index], kinds[index], "after", cancellationToken));
            var finalAncestry = await ReadAncestryAsync(pid, owner, cancellationToken);
            string finalExecutable = ProcLink(pid, "exe"), finalCwd = ProcLink(pid, "cwd");
            string finalEntry = ParseManagedEntry(await ReadBoundedTextAsync($"/proc/{pid}/cmdline", 65_536, cancellationToken), finalCwd, finalExecutable);
            var observed = new ManagedProcessInputs(ancestry, executable, cwd, entry, before.ToArray());
            if (!SameProcessInputs(observed, new(finalAncestry, finalExecutable, finalCwd, finalEntry, afterFiles.ToArray())))
                throw new InvalidOperationException("test_managed_inputs_changed");
            return observed;
        }

        internal static bool SameProcessInputs(ManagedProcessInputs left, ManagedProcessInputs right) =>
            SameAncestry(left.Ancestry, right.Ancestry) && left.Executable == right.Executable
                && left.WorkingDirectory == right.WorkingDirectory && left.Entry == right.Entry
                && left.Files.SequenceEqual(right.Files);

        internal static bool SameAncestry(ManagedProcessIdentity[] left, ManagedProcessIdentity[] right) =>
            left.Length == right.Length && left.Zip(right).All(pair =>
                pair.First.State is not ('Z' or 'X') && pair.Second.State is not ('Z' or 'X') &&
                (pair.First with { State = '?' }) == (pair.Second with { State = '?' }));

        internal static string ParseManagedEntry(string commandLine, string cwd, string executable)
        {
            if (!commandLine.EndsWith('\0') || Path.GetFileName(executable) != "dotnet")
                throw new InvalidOperationException("test_managed_entry_unsupported");
            string[] arguments = commandLine[..^1].Split('\0');
            if (arguments.Length is < 2 or > 256 || Path.GetFileName(arguments[0]) != "dotnet")
                throw new InvalidOperationException("test_managed_entry_unsupported");
            int slot = arguments[1] == "exec" ? 2 : 1;
            if (slot >= arguments.Length || arguments[slot].Length == 0 || arguments[slot].StartsWith('-')
                || !arguments[slot].EndsWith(".dll", StringComparison.Ordinal))
                throw new InvalidOperationException("test_managed_entry_unsupported");
            string entry = Path.GetFullPath(arguments[slot], cwd);
            if (entry.Length > 4096) throw new InvalidOperationException("test_managed_entry_unsupported");
            return entry;
        }

        private static async Task<ManagedProcessIdentity[]> ReadAncestryAsync(int pid, int owner, CancellationToken token)
        {
            var result = new List<ManagedProcessIdentity>();
            var visited = new HashSet<int>();
            ManagedProcessIdentity ownerIdentity = await ReadIdentityAsync(owner, token);
            for (int current = pid; current > 0 && result.Count < 64 && visited.Add(current);)
            {
                ManagedProcessIdentity identity = await ReadIdentityAsync(current, token);
                if (identity.User != ownerIdentity.User || identity.State is 'Z' or 'X')
                    throw new InvalidOperationException("test_managed_process_not_owned");
                result.Add(identity);
                if (current == owner)
                {
                    if (!SameAncestry([identity], [ownerIdentity])) throw new InvalidOperationException("test_managed_process_changed");
                    return result.ToArray();
                }
                current = identity.Parent;
            }
            throw new InvalidOperationException("test_managed_process_not_owned");
        }

        internal static async Task<ManagedProcessIdentity> ReadIdentityAsync(int pid, CancellationToken token)
        {
            string stat = await ReadBoundedTextAsync($"/proc/{pid}/stat", 4096, token);
            int close = stat.LastIndexOf(')');
            if (close < 0) throw new InvalidOperationException("test_managed_process_identity_invalid");
            string[] fields = stat[(close + 2)..].Split(' ');
            string status = await ReadBoundedTextAsync($"/proc/{pid}/status", 65_536, token);
            string[]? user = status.Split('\n').SingleOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 || fields[0].Length != 1 || user is null || user.Length != 5
                || !uint.TryParse(user[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint uid)
                || !user.Skip(2).All(value => value == user[1])
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parent)
                || !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int group)
                || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int session)
                || !ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out ulong start))
                throw new InvalidOperationException("test_managed_process_identity_invalid");
            return new(pid, parent, uid, group, session, start, fields[0][0]);
        }

        private static string ProcLink(int pid, string name)
        {
            string? path = File.ResolveLinkTarget($"/proc/{pid}/{name}", returnFinalTarget: true)?.FullName;
            if (path is null || path.Length > 4096 || !Path.IsPathFullyQualified(path) || path.EndsWith(" (deleted)", StringComparison.Ordinal))
                throw new InvalidOperationException("test_managed_process_path_invalid");
            return path;
        }

        private static async Task<string> ReadBoundedTextAsync(string path, int maximum, CancellationToken token)
        {
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);
            byte[] bytes = new byte[maximum + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = await input.ReadAsync(bytes.AsMemory(count), token)) != 0) count += read;
            if (count > maximum) throw new InvalidOperationException("test_managed_process_input_limit");
            return new System.Text.UTF8Encoding(false, true).GetString(bytes, 0, count);
        }

        internal static async Task<ManagedFileInput> HashInputAsync(string path, string kind, string pass, CancellationToken token)
        {
            if (kind is not ("executable" or "entry" or "deps" or "runtimeconfig") || pass is not ("before" or "after"))
                throw new InvalidOperationException("test_managed_input_diagnostic_invalid");
            int depth = 0;
            for (FileSystemInfo? component = new FileInfo(path); component is not null;
                component = component is FileInfo file ? file.Directory : ((DirectoryInfo)component).Parent, depth++)
            {
                string location = depth == 0 ? "file" : "ancestor";
                int boundedDepth = Math.Min(depth, 4096);
                if (!component.Exists)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"test_managed_input_invalid:{kind}:{pass}:missing:{location}:{boundedDepth}"));
                if (component.LinkTarget is not null)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"test_managed_input_invalid:{kind}:{pass}:link:{location}:{boundedDepth}"));
                if ((component.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"test_managed_input_invalid:{kind}:{pass}:reparse:{location}:{boundedDepth}"));
            }
            var info = new FileInfo(path);
            long length = info.Length;
            DateTime modified = info.LastWriteTimeUtc;
            DateTime created = info.CreationTimeUtc;
            if (length is <= 0 or > 16 * 1024 * 1024) throw new InvalidOperationException("test_managed_input_limit");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            byte[] bytes = new byte[(int)length + 1];
            int count = 0, read;
            while (count < bytes.Length && (read = await input.ReadAsync(bytes.AsMemory(count), token)) != 0) count += read;
            info.Refresh();
            if (count != length || info.Length != length || info.LastWriteTimeUtc != modified
                || info.CreationTimeUtc != created || info.LinkTarget is not null)
                throw new InvalidOperationException("test_managed_inputs_changed");
            return new(path, length, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, count))), modified.Ticks, created.Ticks);
        }

        private static void VerifyPrivateRoot(string directory, string expectedRunId)
        {
            if (!Guid.TryParseExact(expectedRunId, "N", out _) || Path.GetFileName(directory) != expectedRunId)
                throw new InvalidOperationException("test_managed_launch_root_untrusted");
            new TestingResourceIsolation(expectedRunId, directory).VerifyOwnership();
            if (!OperatingSystem.IsLinux() || File.GetUnixFileMode(directory) !=
                (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
                throw new InvalidOperationException("test_managed_launch_root_untrusted");
        }

        internal async Task RetainAsync(CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) return;
            try
            {
                VerifyPrivateRoot(root, runId);
                string temporary = Path.GetFullPath(Path.GetTempPath());
                const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
                for (DirectoryInfo? component = new(temporary); component is not null; component = component.Parent)
                    if (!component.Exists || component.LinkTarget is not null
                        || (component.Attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("test_managed_launch_root_untrusted");
                if (File.GetUnixFileMode(temporary) != mode)
                    throw new InvalidOperationException("test_managed_launch_root_untrusted");
                string directory = Path.Combine(temporary, $"islamu-assurance-managed-launches-{runId}");
                if (Directory.Exists(directory) || File.Exists(directory) || new DirectoryInfo(directory).LinkTarget is not null)
                    throw new InvalidOperationException("test_managed_launch_destination_exists");
                Directory.CreateDirectory(directory, mode);
                if (new DirectoryInfo(directory).LinkTarget is not null || File.GetUnixFileMode(directory) != mode)
                    throw new InvalidOperationException("test_managed_launch_root_untrusted");
                await WritePrivateJsonAsync(Path.Combine(directory, "managed-launches.json"), new
                {
                    Schema = 1, RunId = runId, LaunchInputs = "not-attested", HostContentRoot = "unknown",
                    ExpectedResources = selected.Select(resource => resource.Name).ToArray(), StartupFailureDiagnostic, Rows
                }, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { throw new InvalidOperationException("test_managed_launch_write_failed"); }
        }

        private static async Task WritePrivateJsonAsync<T>(string path, T value, CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("test_managed_launch_platform_unsupported");
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
            if (bytes.Length > 1_048_576) throw new InvalidOperationException("test_managed_launch_record_limit");
            try
            {
                await using var output = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                    Options = FileOptions.Asynchronous, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                await output.WriteAsync(bytes, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            { throw new InvalidOperationException("test_managed_launch_write_failed"); }
        }
    }

    // Temporary discovery only: remove this collector and its checks before final admission.
    internal sealed class ResourcePropertyShapeObservation(IReadOnlyList<IResource> resources)
    {
        private readonly List<ResourcePropertyShape> rows = [];
        internal IReadOnlyList<ResourcePropertyShape> Rows => rows;
        internal bool HasOmittedPropertyShapes { get; private set; }
        internal string LaunchInputs => "not-attested";

        internal void Capture(IResource resource, string? resourceId, ImmutableArray<ResourcePropertySnapshot> properties)
        {
            if (!resources.Any(selected => ReferenceEquals(selected, resource)) || string.IsNullOrEmpty(resourceId)
                || resourceId.Length > 256 || properties.IsDefaultOrEmpty) return;
            if (properties.Length > 32) HasOmittedPropertyShapes = true;
            string instance = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(resourceId)));
            foreach (var property in properties.Take(32))
            {
                if (rows.Count >= 128) { HasOmittedPropertyShapes = true; break; }
                if (!SafePropertyLabel(property.Name)) { HasOmittedPropertyShapes = true; continue; }
                string? primitive = property.IsSensitive ? "not-inspected" : property.Value switch
                {
                    null => "null", bool => "bool", byte => "byte", sbyte => "sbyte", short => "short", ushort => "ushort",
                    int => "int", uint => "uint", long => "long", ulong => "ulong", float => "float", double => "double",
                    decimal => "decimal", string => "string", Guid => "guid", DateTime => "datetime",
                    DateTimeOffset => "datetimeoffset", TimeSpan => "timespan", _ => null
                };
                if (primitive is null) { HasOmittedPropertyShapes = true; continue; }
                var shape = new ResourcePropertyShape(resource.Name, instance, property.Name, primitive, property.IsSensitive);
                if (!rows.Contains(shape)) rows.Add(shape);
            }
        }

        private static bool SafePropertyLabel(string? name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64
                || !name.All(character => char.IsAsciiLetter(character) || character is '.' or '_' or '-')) return false;
            string normalized = string.Concat(name.Where(char.IsAsciiLetter)).ToLowerInvariant();
            return normalized is "pid" or "processid" or "processstarttime" or "starttime" or "starttimestamp"
                or "executable" or "executablepath" or "workingdirectory" or "projectpath";
        }
    }

    internal sealed record ResourcePropertyShape(string Resource, string InstanceSha256, string Name,
        string PrimitiveType, bool IsSensitive);

    private async Task WaitForReadyAsync(CancellationToken cancellationToken)
    {
        await Application.ResourceNotifications.WaitForResourceAsync(
            "event-migrationservice", KnownResourceStates.Exited, cancellationToken);
        List<string> services = topology == "Split" ? ["explore-api", "explore-blazor"] : ["event-standalone"];
        if (profile == OwnedAppHostProfile.ExternalIdentitySharedBff)
            services.Add("explore-blazor-peer");
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
        try { await DisposeOwnedResourcesAsync(); }
        finally
        {
            // Nonterminal owned readers retain their dependencies for the finite outer process boundary.
            if (cleanupObservation.IsCompleted && cleanupObservationCancellation.IsCompleted
                && runningStartup?.IsCompleted != false)
            {
                cleanupObservationLifetime.Dispose();
                lifetime.Dispose();
            }
        }
    }

    private async Task DisposeOwnedResourcesAsync()
    {
        var failures = new List<Exception>();
        bool applicationSettled = true;
        try { await CompleteCleanupProofBeforeStopAsync(); }
        catch (Exception exception) { failures.Add(exception); }
        using var stopDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await lifetime.CancelAsync(); }
        catch (Exception exception) { failures.Add(exception); }
        if (Application is not null)
        {
            try { applicationLifetime!.StopApplication(); }
            catch (Exception exception) { failures.Add(exception); }
            try { await Application.StopAsync(stopDeadline.Token); }
            catch (Exception exception) { failures.Add(exception); }
            finally
            {
                try { await JoinCleanupObservationAsync(); }
                catch (Exception exception) { failures.Add(exception); }
                if (runningStartup is not null)
                {
                    try { await runningStartup.WaitAsync(TimeSpan.FromSeconds(30)); }
                    catch (Exception exception) when (runningStartup.IsCompleted)
                    {
                        if (startupFailure is null) failures.Add(exception);
                    }
                    catch (TimeoutException exception)
                    {
                        failures.Add(new InvalidOperationException("test_resource_cleanup_startup_unsettled", exception));
                    }
                }
                if (!cleanupObservation.IsCompleted || !cleanupObservationCancellation.IsCompleted
                    || runningStartup?.IsCompleted == false)
                {
                    applicationSettled = false;
                    failures.Add(new InvalidOperationException("test_resource_cleanup_dependencies_unsettled"));
                }
                else
                {
                    // Terminal proof/stop faults do not suppress this independent disposal attempt.
                    try { await Application.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(60)); }
                    catch (Exception exception)
                    {
                        applicationSettled = false;
                        failures.Add(exception);
                    }
                }
                try { await execution.WaitForCompletionAsync(TimeSpan.FromSeconds(60)); }
                catch (Exception exception) { failures.Add(exception); }
                if (!execution.IsSettled)
                    applicationSettled = false;
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
        using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var attemptedContainers = new HashSet<string>(StringComparer.Ordinal);
        async Task RemoveOwnedContainerAsync(string containerId)
        {
            if (DockerIdentity(containerId) != containerId)
                throw new InvalidOperationException("test_resource_container_identity_invalid");
            var inspected = await RunDockerAsync(["inspect", "--type", "container", "--format",
                ContainerInspectionFormat, containerId], cleanupDeadline.Token);
            if (inspected.ExitCode != 0)
            {
                if (inspected.Error.Contains("no such", StringComparison.OrdinalIgnoreCase)) return;
                throw new InvalidOperationException("test_resource_container_inspection_failed");
            }
            using var document = JsonDocument.Parse(inspected.Output);
            RequireOwnedContainer(RunId, containerResources, document.RootElement[3].GetRawText());
            ContainerCleanupIdentity? original;
            lock (containerIdentityLock) containerIdentities.TryGetValue(containerId, out original);
            if (original is null)
                throw new InvalidOperationException("test_resource_container_identity_unknown:unjoined");
            if (!TryReadContainerIdentity(cleanupResources, original.Resource,
                original.Instance, RunId, original.Image, original.ImageVolumeTargets, initialContainerIds,
                initialVolumeNames, inspected.Output, out _, out string reason, original))
                throw UnknownContainer(original.Resource, reason);
            foreach (var volume in original.Volumes)
            {
                VolumeCleanupIdentity? captured;
                lock (containerIdentityLock) volumeIdentities.TryGetValue(volume.Name, out captured);
                if (captured is null || await ReadVolumeIdentityAsync(volume.Name, cleanupDeadline.Token) != captured)
                    throw UnknownContainer(original.Resource, "volume-metadata-changed");
                string[] consumers = await ReadVolumeConsumersAsync(captured, cleanupDeadline.Token);
                RequireExclusiveVolumeConsumer(containerId, consumers);
            }
            if (!attemptedContainers.Add(containerId)) return;
            if (document.RootElement[5].GetBoolean())
            {
                var stopped = await RunDockerAsync(["stop", "--time", "10", containerId], cleanupDeadline.Token);
                if (stopped.ExitCode != 0 && !stopped.Error.Contains("no such", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("test_resource_container_stop_failed");
            }
            var waited = await RunDockerAsync(["wait", containerId], cleanupDeadline.Token);
            if (waited.ExitCode != 0 && !waited.Error.Contains("no such", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("test_resource_container_wait_failed");
            var removal = await RunDockerAsync(["rm", containerId], cleanupDeadline.Token);
            if (removal.ExitCode != 0 && !removal.Error.Contains("no such container", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("test_resource_container_cleanup_failed");
        }
        if (cleanupResources.Length != 0)
        {
            try
            {
                var candidates = await RunDockerAsync(["ps", "--no-trunc", "-aq", "--filter",
                    $"label=islamu.assurance.owner={RunId}"], cleanupDeadline.Token);
                if (candidates.ExitCode != 0)
                    throw new InvalidOperationException("test_resource_container_inventory_failed");
                foreach (string id in candidates.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    try { await RemoveOwnedContainerAsync(id); }
                    catch (Exception exception) { failures.Add(exception); }
                }
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        string[] selectedVolumes;
        lock (containerIdentityLock)
            selectedVolumes = volumes.Concat(volumeIdentities.Keys).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string volume in selectedVolumes)
        {
            try
            {
                var existing = await ReadVolumeIdentityAsync(volume, cleanupDeadline.Token);
                if (existing is null) continue;
                // Read foreign named AND bind consumers before checking origin, without granting deletion authority.
                foreach (string containerId in await ReadVolumeConsumersAsync(existing, cleanupDeadline.Token))
                    await RemoveOwnedContainerAsync(containerId);
                VolumeCleanupIdentity? captured;
                lock (containerIdentityLock) volumeIdentities.TryGetValue(volume, out captured);
                if (initialVolumeNames.Contains(volume) || captured is null
                    || existing != captured || await ReadVolumeIdentityAsync(volume, cleanupDeadline.Token) != captured)
                    throw new InvalidOperationException("test_resource_volume_origin_unknown");
                if ((await ReadVolumeConsumersAsync(captured, cleanupDeadline.Token)).Length != 0)
                    throw new InvalidOperationException("test_resource_foreign_container");
                var volumeRemoval = await RunDockerAsync(["volume", "rm", volume], cleanupDeadline.Token);
                if (volumeRemoval.ExitCode != 0 && !volumeRemoval.Error.Contains("no such volume", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("test_resource_volume_cleanup_failed");
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        ContainerInspectionObservation[] inspections;
        (IResource Resource, string Instance)[] observedCandidates;
        (IResource Resource, string Instance)[] joined;
        lock (containerIdentityLock)
        {
            inspections = [.. cleanupInspections];
            observedCandidates = cleanupCandidates.Select(candidate => ((IResource)candidate.Resource, candidate.Instance)).ToArray();
            joined = containerIdentities.Values.Select(identity => ((IResource)identity.Resource, identity.Instance)).ToArray();
        }
        async Task<CleanupInventory> ReadInventoryAsync(CancellationToken token)
        {
            var containers = await RunDockerAsync(["ps", "--no-trunc", "-aq"], token);
            var currentVolumes = await RunDockerAsync(["volume", "ls", "--format", "{{.Name}}"], token);
            return new(containers.ExitCode, containers.Output, currentVolumes.ExitCode, currentVolumes.Output);
        }
        try
        {
            await ReleaseCleanupRootAsync(RunId, Root,
                [cleanupObservation, cleanupObservationCancellation, runningStartup ?? Task.CompletedTask], failures,
                cleanupProofFailures, observedCandidates, inspections, joined,
                (initialContainerIds, initialVolumeNames), ReadInventoryAsync, cleanupDeadline.Token);
        }
        catch (Exception exception) when (startupFailure is not null)
        {
            throw new AggregateException(startupFailure, exception);
        }
    }

    internal sealed record CleanupInventory(int ContainerExitCode, string Containers, int VolumeExitCode, string Volumes);

    internal static async Task ReleaseCleanupRootAsync(string runId, string root, IReadOnlyList<Task> ownedWork,
        IReadOnlyList<Exception> originalFailures, CleanupProofFailures proofFailures,
        IReadOnlyList<(IResource Resource, string Instance)> candidates,
        IReadOnlyList<ContainerInspectionObservation> inspections, IReadOnlyList<(IResource Resource, string Instance)> joined,
        (IReadOnlySet<string> Containers, IReadOnlySet<string> Volumes) baseline,
        Func<CancellationToken, Task<CleanupInventory>> readInventory, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>(originalFailures);
        foreach (var failure in proofFailures.Snapshot())
            if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
        if (ownedWork.Any(task => !task.IsCompleted))
            failures.Add(new InvalidOperationException("test_resource_cleanup_dependencies_unsettled"));
        if (failures.Count == 0)
        {
            try
            {
                bool unresolvedAbsence = false;
                foreach (var candidate in candidates)
                {
                    if (joined.Any(identity => ReferenceEquals(identity.Resource, candidate.Resource)
                        && identity.Instance == candidate.Instance)) continue;
                    var inspection = inspections.LastOrDefault(observation => ReferenceEquals(observation.Resource, candidate.Resource)
                        && observation.Instance == candidate.Instance);
                    if (inspection?.Outcome != ContainerInspectionOutcome.Absent)
                        throw UnknownContainer(candidate.Resource, "candidate-unproved");
                    unresolvedAbsence = true;
                }
                void RequireBaseline(CleanupInventory inventory)
                {
                    if (inventory.ContainerExitCode != 0 || inventory.VolumeExitCode != 0)
                        throw new InvalidOperationException("test_resource_cleanup_absence_inventory_failed");
                    var containers = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string id in inventory.Containers.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (DockerIdentity(id) != id || !containers.Add(id))
                            throw new InvalidOperationException("test_resource_cleanup_absence_container_invalid");
                    var currentVolumes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string volume in inventory.Volumes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!SafeVolumeName(volume) || !currentVolumes.Add(volume))
                            throw new InvalidOperationException("test_resource_cleanup_absence_volume_invalid");
                    if (!containers.SetEquals(baseline.Containers) || !currentVolumes.SetEquals(baseline.Volumes))
                        throw new InvalidOperationException("test_resource_cleanup_absence_inventory_drift");
                }
                if (unresolvedAbsence)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireBaseline(await readInventory(cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                }
                new TestingResourceIsolation(runId, root).VerifyOwnership();
                if (unresolvedAbsence)
                {
                    RequireBaseline(await readInventory(cancellationToken));
                    cancellationToken.ThrowIfCancellationRequested();
                    new TestingResourceIsolation(runId, root).VerifyOwnership();
                }
                Directory.Delete(root, recursive: true);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count > 0) throw new AggregateException(failures);
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

    private ExternalDataProtectionInput CreatePeerProtection(OwnedBffPeerProtection protection)
    {
        if (protection == OwnedBffPeerProtection.IndependentValidAuthority)
            return new ExternalDataProtectionTestAuthority("bff").Input;
        string partition = protection == OwnedBffPeerProtection.WrongPartition ? "API" : "BFF";
        string prefix = $"EVENT_DP_{partition}_";
        var input = new ExternalDataProtectionInput(EnvironmentAuthority[prefix + "RING"]!,
            EnvironmentAuthority[prefix + "ADMISSION"]!, EnvironmentAuthority[prefix + "ADMISSION_SHA256"]!,
            long.Parse(EnvironmentAuthority[prefix + "MINIMUM_GENERATION"]!, CultureInfo.InvariantCulture));
        if (protection == OwnedBffPeerProtection.ExpiredAdmission)
        {
            var now = DateTimeOffset.UtcNow;
            var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
            string expired = JsonSerializer.Serialize(admission with
            {
                NotBeforeUtc = now.AddHours(-2), NotAfterUtc = now.AddHours(-1)
            });
            return input with { Admission = expired, AdmissionSha256 = ExternalDataProtectionInput.Hash(expired) };
        }
        return protection switch
        {
            OwnedBffPeerProtection.Shared or OwnedBffPeerProtection.WrongPartition => input,
            OwnedBffPeerProtection.GenerationBelowFloor => input with { MinimumGeneration = 2 },
            OwnedBffPeerProtection.RevokedActiveKey => ExternalDataProtectionTestAuthority.RevokeActiveKey(input),
            _ => throw new InvalidOperationException("test_resource_profile_invalid")
        };
    }

    private static FrozenDictionary<string, string?> CreateEnvironmentAuthority(string runId, OwnedAppHostProfile profile)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["KEYCLOAK_ADMIN"] = "assurance_admin", ["STORAGE_S3_ACCESS_KEY_ID"] = "assurance_storage",
            ["SECRET_PROVIDER"] = "Environment", ["DOTNET_ENVIRONMENT"] = "Assurance",
            ["ASPNETCORE_ENVIRONMENT"] = "Assurance", ["SecretProvider:Provider"] = "Environment",
            ["DATABASE_PROVIDER"] = "PostgreSql", ["DATABASE_NAME"] = "islamu_assurance",
            ["DATABASE_SCHEMA"] = "assurance", ["AUTHENTICATION_PROVIDER"] = "Local",
            ["AUTHORIZATION_PROVIDER"] = "Local", ["BFF_SESSION_NAMESPACE"] = runId
        };
        foreach (string purpose in DatabasePurposes.Append("MIGRATOR"))
        {
            values[$"DATABASE_{purpose}_USERNAME"] = "assurance_" + purpose.ToLowerInvariant();
            values[$"DATABASE_{purpose}_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }
        foreach (string key in new[] { "CERBOS_POSTGRES_PASSWORD", "KEYCLOAK_ADMIN_PASSWORD",
            "STORAGE_S3_SECRET_ACCESS_KEY", "AUTHENTICATION_LOCAL_JWT_KEY", "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
            "ISLAMU_TEST_REPLACEMENT_PASSWORD", "BFF_SESSION_REDIS_PASSWORD" })
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
        if (profile == OwnedAppHostProfile.ClosedCompositionModel)
        {
            foreach (string key in new[] { "FORMBRICKS_NEXTAUTH_SECRET", "FORMBRICKS_ENCRYPTION_KEY",
                "FORMBRICKS_CRON_SECRET", "FORMBRICKS_HUB_API_KEY", "FORMBRICKS_CUBEJS_API_SECRET",
                "FORMBRICKS_DATABASE_PASSWORD" })
                values[key] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + ":/?#[]@!$&'()*+,;=%";
            values["ERASURE_DATABASE_HOST"] = "localhost";
            values["ERASURE_DATABASE_NAME"] = "islamu_assurance_erasure";
            foreach (string purpose in new[] { "MIGRATOR", "RUNTIME" })
            {
                values[$"ERASURE_DATABASE_{purpose}_USERNAME"] = "assurance_erasure_" + purpose.ToLowerInvariant();
                values[$"ERASURE_DATABASE_{purpose}_PASSWORD"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            }
        }
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
