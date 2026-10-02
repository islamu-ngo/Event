using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CarpaNet;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.StorageObjects.Handlers.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Federation;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Tests.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel("StorageTargetSqlite")]
public sealed class ManagedStorageBindingTests
{
    internal static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==");

    [Test]
    public async Task GenericReservationFinalizationReadAndDeleteKeepTheReservedRoot()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        var reserve = new CreateStorageUploadSessionCommandHandler(env.Policy, env.Sessions, env.Counters,
            new PrivacyErasureStateRepository(env.Context), env.Database, env.User, env.Unit, env.Metrics, env.Bindings);
        var reservation = await reserve.ExecuteAsync(new CreateStorageUploadSessionCommand
        {
            UploadSessionDto = new CreateStorageUploadSessionDto
            {
                ContentType = "image/png", ExpectedSizeBytes = Png.Length, SafeDisplayName = "image.png",
                Extension = "png", Purpose = StorageObjectPurposes.EventImage,
                Visibility = StorageObjectVisibilities.PublicImage, IdempotencyKey = Guid.CreateVersion7().ToString("N")
            }
        }, default);
        await Assert.That(reservation.IsSuccess).IsTrue();
        var saved = await env.Sessions.GetForAuthorizationAsync(reservation.Id!.Id, default);
        var original = saved!.StorageProviderBindingId!.Value;
        env.Options.RootPath = env.OtherRoot;
        var finalizer = new FinalizeStorageUploadSessionCommandHandler(env.Bindings, env.Policy, env.Sessions,
            env.Counters, env.Objects, new PrivacyErasureStateRepository(env.Context), env.Database, env.User,
            env.Unit, env.Metrics);
        using var content = new MemoryStream(Png);
        var finalized = await finalizer.ExecuteAsync(new FinalizeStorageUploadSessionCommand
        {
            UploadSessionId = saved.Id, Content = content, ContentLength = Png.Length, ContentType = "image/png"
        }, default);
        await Assert.That(finalized.IsSuccess).IsTrue();
        var metadata = (await env.Objects.GetForGenericAccessAsync(finalized.Id!.StorageObjectId!.Value, default))!;
        await Assert.That(metadata.StorageProviderBindingId).IsEqualTo(original);
        await Assert.That(File.Exists(Path.Combine(env.Root, metadata.ObjectKey!))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(env.OtherRoot, metadata.ObjectKey!))).IsFalse();
        var reader = new StorageObjectContentReader(env.Objects, env.Bindings, env.User,
            NullLogger<StorageObjectContentReader>.Instance, env.Metrics);
        var opened = await reader.OpenAsync(metadata.Id, true, default);
        await Assert.That(opened).IsNotNull();
        await using (var stream = opened!.Content)
        {
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            await Assert.That(bytes.ToArray()).IsEquivalentTo(Png);
        }
        // A later HTTP request has a fresh identity map.
        env.Context.ChangeTracker.Clear();
        var deletion = new DeleteStorageObjectCommandHandler(env.Objects, env.Bindings, env.Metrics);
        await Assert.That(await deletion.ExecuteAsync(new DeleteStorageObjectCommand { Id = metadata.Id }, default)).IsTrue();
        await Assert.That(File.Exists(Path.Combine(env.Root, metadata.ObjectKey!))).IsFalse();
    }

    [Test]
    public async Task CsvSinkCommitsCapturedTargetAndMetadataWithoutInventingAUser()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        var request = CsvRequest(env.Database.TenantId);
        var sink = new CsvRegistrationProviderSubmissionSink(env.Producer, env.Objects, env.Unit);
        await sink.AcceptAsync(request, default);
        env.Options.RootPath = env.OtherRoot;
        var stored = (await env.Objects.GetAll()).Single();
        await Assert.That(stored.StorageProviderBindingId).IsNotNull();
        await Assert.That(stored.CreatedBy).IsNull();
        await Assert.That(stored.OwningResourceId).IsEqualTo(request.RegistrationSubmissionId);
        await Assert.That(await env.Context.Set<StorageProducerOperation>().CountAsync()).IsEqualTo(0);
        var target = await env.Bindings.ResolveTargetAsync(stored.StorageProviderBindingId, stored.Provider, default);
        var read = await target.OpenReadAsync(new(stored.ObjectKey!, stored.ContentType), default);
        await using var stream = read.Content;
        using var text = new StreamReader(stream);
        await Assert.That(await text.ReadToEndAsync()).IsEqualTo("\"name\"\n\"Amina\"\n");
        await Assert.That(File.Exists(Path.Combine(env.OtherRoot, stored.ObjectKey!))).IsFalse();
    }

    [Test]
    public async Task FederationStagingAndAbandonmentKeepTheExactCapturedTarget()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        const string did = "did:plc:z72i7hdynmk6r22z27h6tvur";
        string cid = ATCid.FromSha256Hash(SHA256.HashData(Png)).Value;
        var gateway = new AtprotoThumbnailBlobGateway(_ => new ThumbnailHandler(did), env.Producer,
            env.Policy, Png.Length, TimeSpan.FromSeconds(10));
        var staged = await gateway.FetchAndStageAsync(new(did, cid, "image/png", Png.Length),
            env.Database.TenantId, default);
        await Assert.That(staged).IsNotNull();
        env.Options.RootPath = env.OtherRoot;
        var operation = await env.Context.Set<StorageProducerOperation>().AsNoTracking().SingleAsync();
        await Assert.That(operation.ProviderBindingId).IsEqualTo(staged!.BindingId);
        await Assert.That(operation.ProducerSettled).IsTrue();
        await gateway.CleanupAsync(staged, default);
        var work = await env.Context.StorageObjectDeletionTombstones.AsNoTracking().SingleAsync();
        await Assert.That(work.ProviderBindingId).IsEqualTo(staged.BindingId);
        await Assert.That(work.ObjectKey).IsEqualTo(staged.Write.ObjectKey);
        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(await env.Context.Set<StorageProducerOperation>().CountAsync()).IsEqualTo(0);
        var cleanup = new EventResourceStorageCleanupService(new StorageObjectDeletionTombstoneRepository(env.Context),
            env.Bindings, TimeProvider.System, NullLogger<EventResourceStorageCleanupService>.Instance,
            new EventResourceStorageLifecycleRepository(env.Context), env.Unit);
        var result = await cleanup.ProcessDueAsync(10, false, default);
        await Assert.That(result.DeletedCount).IsEqualTo(1);
        await Assert.That(File.Exists(Path.Combine(env.Root, staged.Write.ObjectKey))).IsFalse();
    }

    [Test]
    public async Task InterruptedProducerKeepsDurableUnsettledCleanupAuthority()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        using var content = new InspectingStream(async () =>
        {
            await Assert.That(env.Context.Database.CurrentTransaction).IsNull();
            await Assert.That(await env.Context.Set<StorageProducerOperation>().AsNoTracking().CountAsync()).IsEqualTo(1);
            throw new IOException("Interrupted producer.");
        });
        await Assert.ThrowsAsync<IOException>(() => env.Producer.WriteAsync(StorageProviders.Local,
            new(env.Database.TenantId, content, "text/plain", "file.txt", "txt", 1, 1), default));
        var work = await env.Context.StorageObjectDeletionTombstones.AsNoTracking().SingleAsync();
        await Assert.That(work.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
        await Assert.That(work.ProviderBindingId).IsNotEqualTo(Guid.Empty);
        await Assert.That(await env.Context.Set<StorageProducerOperation>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task RetiredProducerCannotActivateAndBindingMismatchFailsClosed()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        using var content = new MemoryStream("data"u8.ToArray());
        var staged = await env.Producer.WriteAsync(StorageProviders.Local,
            new(env.Database.TenantId, content, "text/plain", "file.txt", "txt", 4, 4), default);
        await env.Producer.RetireAsync(staged.OperationId, staged.TenantId, default);
        var operation = await env.Unit.ExecuteSerializableAsync(
            ct => env.Objects.FenceProducerAsync(staged.OperationId, staged.TenantId, ct));
        await Assert.That(operation).IsNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Bindings.ResolveTargetAsync(null, StorageProviders.Local, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Bindings.ResolveTargetAsync(staged.BindingId, StorageProviders.S3Compatible, default));
        var known = await env.Objects.ListKnownObjectKeysAsync([staged.BindingId], [staged.Write.ObjectKey], default);
        await Assert.That(known).Contains(staged.Write.ObjectKey);
    }

    [Test]
    public async Task DeletedMetadataRetainsItsBindingWhenExistingErasureClearsTheKey()
    {
        await using var env = await StorageTestEnvironment.CreateAsync();
        var binding = StorageProviderBinding.Local(env.Root);
        env.Context.Add(binding);
        var stored = new StorageObject
        {
            Id = Guid.CreateVersion7(), TenantId = env.Database.TenantId, Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Document, FileType = null!, Provider = binding.Provider,
            StorageProviderBindingId = binding.Id, ObjectKey = "objects/retired.pdf",
            FullName = "file.pdf", SafeDisplayName = "file.pdf", Extension = "pdf",
            Purpose = StorageObjectPurposes.Document, Visibility = StorageObjectVisibilities.AuthenticatedTenant,
            LifecycleState = StorageObjectLifecycleStates.Active
        };
        await env.Objects.Create(stored);
        stored.ObjectKey = null;
        stored.MarkDeleted(null, DateTime.UtcNow);
        await env.Objects.Update(stored);
        env.Context.ChangeTracker.Clear();
        var retained = await env.Context.StorageObjects.IgnoreQueryFilters().SingleAsync(value => value.Id == stored.Id);
        await Assert.That(retained.ObjectKey).IsNull();
        await Assert.That(retained.StorageProviderBindingId).IsEqualTo(binding.Id);
        await Assert.That(await env.Objects.GetForGenericAccessAsync(stored.Id, default)).IsNull();
    }

    internal static RegistrationProviderSubmissionSinkRequest CsvRequest(Guid tenantId)
    {
        var tuple = CsvRegistrationProviderSubmissionSink.SupportedTuple;
        var connection = RegistrationProviderConnection.Create(tenantId, "CSV", RegistrationProviderKindEnum.ExternalApi,
            RegistrationProviderDeploymentKindEnum.HostedSaas, tuple.ProviderCode, tuple.ProviderDeploymentCode,
            tuple.ApiVersion, tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision,
            "https://operator.example.test", "https://operator.example.test", StorageProviders.Local, null, null, DateTime.UtcNow);
        var binding = RegistrationProviderBinding.Create(tenantId, connection.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
            RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, DateTime.UtcNow);
        return new(tenantId, binding, connection, tuple, Guid.CreateVersion7(), Guid.CreateVersion7(),
            new Dictionary<string, string> { ["name"] = "Amina" }, null);
    }

    private sealed class ThumbnailHandler(string did) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpContent content;
            if (request.RequestUri!.Host == "plc.directory")
                content = new StringContent($$"""{"id":"{{did}}","service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.test"}]}""");
            else
            {
                content = new ByteArrayContent(Png);
                content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class InspectingStream(Func<Task> inspect) : MemoryStream(new byte[1])
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inspect();
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}

internal sealed class StorageTestEnvironment : IAsyncDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private StorageTestEnvironment(SmtpSettingsDatabase database)
    {
        Database = database;
        Context.TenantContext = database;
        Root = Directory.CreateTempSubdirectory("storage-target-").FullName;
        OtherRoot = Directory.CreateTempSubdirectory("storage-replacement-").FullName;
        Options = new LocalFileStorageOptions { RootPath = Root, CreateRootIfMissing = false };
        User = new CurrentUser(database.ActorId);
        Bindings = new StorageProviderBindingService(new StorageProviderBindingRepository(Context), database.Settings,
            Substitute.For<IRetainedSecretResolver>(), Microsoft.Extensions.Options.Options.Create(Options),
            Substitute.For<IS3ClientFactory>(), NullLoggerFactory.Instance);
        Objects = new StorageObjectRepository(Context);
        Unit = new EfCoreUnitOfWork(Context);
        Producer = new ManagedStorageProducer(Bindings, Objects, Unit);
        Sessions = new StorageUploadSessionRepository(Context);
        Counters = new StorageUsageCounterRepository(Context);
        Policy = new StoragePolicyResolver(database.Settings,
            new FileStorageProviderResolver([new LocalFileStorageProvider(
                Microsoft.Extensions.Options.Options.Create(Options), NullLogger<LocalFileStorageProvider>.Instance)]));
        Metrics = new BusinessMetrics(_services.GetRequiredService<IMeterFactory>());
    }

    public SmtpSettingsDatabase Database { get; }
    public ExploreDbContext Context => Database.Context;
    public string Root { get; }
    public string OtherRoot { get; }
    public LocalFileStorageOptions Options { get; }
    public ICurrentUserService User { get; }
    public StorageProviderBindingService Bindings { get; }
    public StorageObjectRepository Objects { get; }
    public EfCoreUnitOfWork Unit { get; }
    public ManagedStorageProducer Producer { get; }
    public StorageUploadSessionRepository Sessions { get; }
    public StorageUsageCounterRepository Counters { get; }
    public StoragePolicyResolver Policy { get; }
    public BusinessMetrics Metrics { get; }

    public static async Task<StorageTestEnvironment> CreateAsync()
    {
        var database = await SmtpSettingsDatabase.CreateAsync();
        database.Context.FileTypes.AddRange(
            new FileType { Id = (int)FileTypeEnum.Image, MasterCode = "Image", FullName = "Image" },
            new FileType { Id = (int)FileTypeEnum.Document, MasterCode = "Document", FullName = "Document" });
        await database.Context.SaveChangesAsync();
        return new(database);
    }

    public async ValueTask DisposeAsync()
    {
        Metrics.Dispose();
        await _services.DisposeAsync();
        await Database.DisposeAsync();
        Directory.Delete(Root, true);
        Directory.Delete(OtherRoot, true);
    }

    private sealed record CurrentUser(Guid Id) : ICurrentUserService
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => true;
    }
}
