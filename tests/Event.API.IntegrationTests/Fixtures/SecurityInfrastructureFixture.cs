using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Containers;
using Event.Testing;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using TUnit.Core.Interfaces;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Composite fixture orchestrating Keycloak + Cerbos container lifecycle.
/// Start both containers in parallel, expose their endpoints for downstream
/// <see cref="SecurityWebApplicationFactory"/> and <see cref="CerbosPolicyContractTests"/>.
/// </summary>
public sealed class SecurityInfrastructureFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly SemaphoreSlim observationPublication = new(1, 1);
    private readonly KeycloakContainerFixture _keycloak = new();
    private readonly CerbosContainerFixture _cerbos = new();

    /// <summary>
    /// Token client delegating to the Keycloak container.
    /// </summary>
    public KeycloakTokenClient TokenClient => _keycloak.TokenClient;

    /// <summary>
    /// The OIDC authority URL from the Keycloak container.
    /// </summary>
    public string KeycloakAuthority => _keycloak.Authority;

    public string KeycloakBaseUrl => _keycloak.BaseUrl;

    /// <summary>
    /// The OIDC metadata address from the Keycloak container.
    /// </summary>
    public string KeycloakMetadataAddress => _keycloak.MetadataAddress;

    /// <summary>
    /// The Cerbos gRPC endpoint for SDK clients.
    /// </summary>
    public string CerbosGrpcEndpoint => _cerbos.GrpcEndpoint;

    /// <summary>
    /// The Cerbos HTTP endpoint for REST API and health checks.
    /// </summary>
    public string CerbosHttpEndpoint => _cerbos.HttpEndpoint;

    public async Task InitializeAsync()
    {
        // Start both containers in parallel for faster fixture setup.
        var keycloakTask = _keycloak.InitializeAsync();
        var cerbosTask = _cerbos.InitializeAsync();

        await Task.WhenAll(keycloakTask, cerbosTask);
    }

    internal static async Task<(ContainerBuilder Builder, AssuranceOwnershipContext? Ownership)> WithAssuranceOwnershipAsync(
        ContainerBuilder builder, string resource)
    {
        if (resource is not ("keycloak" or "cerbos"))
            throw new InvalidOperationException("test_resource_ownership_invalid");
        AssuranceOwnershipContext? context = await AssuranceOwnershipContext.ValidateAssuranceContextAsync(
            Environment.GetEnvironmentVariable("ISLAMU_TEST_RUN_ID"),
            Environment.GetEnvironmentVariable("ISLAMU_TEST_ROOT"), Path.GetTempPath(),
            Environment.GetEnvironmentVariable("DOCKER_HOST"));
        return (context is null ? builder : builder.WithDockerEndpoint(context.DockerEndpoint)
            .WithLabel("islamu.assurance.owner", context.Owner)
            .WithLabel("islamu.assurance.resource", resource).WithImagePullPolicy(PullPolicy.Never), context);
    }

    internal static async Task<ContainerCreationObservation?> BeginCreationObservationAsync(
        AssuranceOwnershipContext? context, string resource, IContainer container, CancellationToken cancellationToken)
    {
        if (context is null) return null;
        if (resource is not ("keycloak" or "cerbos"))
            throw new InvalidOperationException("test_creation_resource_invalid");
        var imageReference = container.Image.FullName;
        if (string.IsNullOrWhiteSpace(imageReference) || imageReference.Length > 1024 || imageReference.Any(char.IsControl))
            throw new InvalidOperationException("test_creation_image_invalid");
        var observation = new ContainerCreationObservation(context, resource, imageReference);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await observationPublication.WaitAsync(deadline.Token);
            try
            {
                await ValidateObservationRootAsync(context);
                if (Directory.EnumerateFiles(context.Root, "api-container-*-declaration.json").Take(65).Count() >= 64)
                    throw new InvalidOperationException("test_creation_observation_limit");
                await observation.WriteDeclarationAsync(deadline.Token);
                return observation;
            }
            finally { observationPublication.Release(); }
        }
        catch (Exception primary)
        {
            try { await observation.DisposeAsync(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    internal static async Task StartObservedContainerAsync(IContainer container,
        ContainerCreationObservation? observation, CancellationToken cancellationToken)
    {
        Exception? primary = null;
        try { await container.StartAsync(cancellationToken); }
        catch (Exception exception) { primary = exception; }
        Exception? observationFailure = null;
        if (observation is not null)
        {
            try
            {
                string? id = null;
                try { id = container.Id; }
                catch (Exception) { throw new InvalidOperationException("test_creation_identity_unavailable"); }
                // Failure to create an ID is unknown; it is never reconstructed from a name or label.
                await observation.RecordCreatedAsync(id, CancellationToken.None);
            }
            catch (Exception exception) { observationFailure = exception; }
        }
        if (primary is not null && observationFailure is not null) throw new AggregateException(primary, observationFailure);
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
        if (observationFailure is not null) ExceptionDispatchInfo.Capture(observationFailure).Throw();
    }

    internal static async ValueTask DisposeObservedContainerAsync(IAsyncDisposable? container, IAsyncDisposable? observation)
    {
        Exception? primary = null;
        try { if (container is not null) await container.DisposeAsync(); }
        catch (Exception exception) { primary = exception; }
        try { if (observation is not null) await observation.DisposeAsync(); }
        catch (Exception cleanup)
        {
            if (primary is not null) throw new AggregateException(primary, cleanup);
            throw;
        }
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private static async Task ValidateObservationRootAsync(AssuranceOwnershipContext context)
    {
        string temporary = Directory.GetParent(context.Root)?.Parent?.FullName
            ?? throw new InvalidOperationException("test_creation_observation_invalid");
        if (await AssuranceOwnershipContext.ValidateAssuranceOwnershipAsync(context.Owner, context.Root, temporary) != context.Owner)
            throw new InvalidOperationException("test_creation_observation_invalid");
    }

    internal sealed class ContainerCreationObservation(AssuranceOwnershipContext context, string resource,
        string imageReference) : IAsyncDisposable
    {
        private FileStream? declaration;
        private FileStream? created;
        private bool disposed;
        private string declarationSha256 = string.Empty;
        internal string CreationId { get; } = Guid.CreateVersion7().ToString("N");

        internal async Task WriteDeclarationAsync(CancellationToken token)
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = 1,
                RunId = context.Owner,
                CreatorPid = Environment.ProcessId,
                CreationId,
                Resource = resource,
                ImageReference = imageReference
            });
            declarationSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            declaration = await WriteRecordAsync($"api-container-{resource}-{CreationId}-declaration.json", bytes, token);
        }

        internal async Task<bool> RecordCreatedAsync(string? containerId, CancellationToken cancellationToken)
        {
            if (containerId is null || containerId.Length != 64
                || containerId.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) return false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await observationPublication.WaitAsync(deadline.Token);
            try
            {
                if (disposed || declaration is null) throw new InvalidOperationException("test_creation_observation_closed");
                if (created is not null) throw new InvalidOperationException("test_creation_identity_duplicate");
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Schema = 1,
                    RunId = context.Owner,
                    CreatorPid = Environment.ProcessId,
                    CreationId,
                    Resource = resource,
                    ImageReference = imageReference,
                    ContainerId = containerId,
                    DeclarationSha256 = declarationSha256
                });
                created = await WriteRecordAsync($"api-container-{resource}-{CreationId}-created-{containerId}.json", bytes, deadline.Token);
                return true;
            }
            finally { observationPublication.Release(); }
        }

        private async Task<FileStream> WriteRecordAsync(string name, byte[] bytes, CancellationToken token)
        {
            if (!OperatingSystem.IsLinux() || bytes.Length is < 1 or > 8192)
                throw new InvalidOperationException("test_creation_observation_invalid");
            await ValidateObservationRootAsync(context);
            string pending = Path.Combine(context.Root, "." + name + ".pending");
            string final = Path.Combine(context.Root, name);
            FileStream? output = null;
            try
            {
                output = new FileStream(pending, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
                await output.WriteAsync(bytes, token);
                await output.FlushAsync(token);
                output.Position = 0;
                byte[] readback = new byte[bytes.Length];
                await output.ReadExactlyAsync(readback, token);
                if (!bytes.AsSpan().SequenceEqual(readback)) throw new InvalidOperationException("test_creation_observation_invalid");
                File.Move(pending, final, overwrite: false);
                var info = new FileInfo(final);
                if (info.LinkTarget is not null || info.Length != bytes.Length
                    || File.GetUnixFileMode(final) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                    throw new InvalidOperationException("test_creation_observation_invalid");
                return output;
            }
            catch (Exception exception)
            {
                Exception failure = exception is OperationCanceledException ? exception
                    : new InvalidOperationException("test_creation_observation_write_failed");
                try { if (output is not null) await output.DisposeAsync(); }
                catch (Exception) { throw new AggregateException(failure, new InvalidOperationException("test_creation_observation_close_failed")); }
                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await observationPublication.WaitAsync();
            try
            {
                if (disposed) return;
                disposed = true;
                if (declaration is not null && created is not null)
                    await DisposePrerequisitesAsync(declaration, created);
                else if (declaration is not null) await declaration.DisposeAsync();
            }
            finally { observationPublication.Release(); }
        }
    }

    public ValueTask DisposeAsync() => DisposePrerequisitesAsync(_keycloak, _cerbos);

    internal static async ValueTask DisposePrerequisitesAsync(IAsyncDisposable first, IAsyncDisposable second)
    {
        Exception?[] outcomes = await Task.WhenAll(DisposeOneAsync(first), DisposeOneAsync(second));
        Exception[] failures = outcomes.OfType<Exception>().ToArray();
        if (failures.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Length > 1) throw new AggregateException(failures);

        static async Task<Exception?> DisposeOneAsync(IAsyncDisposable resource)
        {
            try { await resource.DisposeAsync(); return null; }
            catch (Exception exception) { return exception; }
        }
    }
}
