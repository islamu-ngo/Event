using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    /// <summary>
    /// Reuses the actual fresh-case/procedural/reviewer fixture and its native review writer.
    /// Only the two adoption steps are authored: inspection cannot rely on a safe attestation.
    /// Storage, protection, policy reads, primary custody and retained authority are real owners.
    /// Unused external S3/secret boundaries are the only substitutes.
    /// </summary>
    private sealed class InspectionFixture(
        Fixture native, SqliteConnection retainedConnection,
        DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> retainedOptions) : IAsyncDisposable
    {
        public Fixture Native { get; } = native;
        public EventResourceDestinationProtector Protector { get; } = new(new EphemeralDataProtectionProvider());
        public byte[] Expected { get; private set; } = [];
        public Guid PurposeId { get; private set; }
        public Guid? StorageActorId { get; private set; }
        public ModerationResourceQuarantineInspectionIntent Intent { get; private set; } = null!;
        private LocalFileStorageProvider? local;
        private string? objectKey;
        private string? filePath;

        public EmbeddedPrivacyErasureAuthorityRepository Retained() =>
            new(new InspectionAuthorityFactory(retainedOptions), Native.Clock, Options.Create(new PrivacyErasureOptions()));

        public static async Task<InspectionFixture> CreateAsync(
            bool file = false, int length = 131_073, bool separateStorageActor = false)
        {
            var native = await Fixture.CreateAsync(storedFile: file, prepare: false);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention().Options;
            await using (var authority = new EmbeddedPrivacyErasureAuthorityDbContext(options))
                await authority.Database.EnsureCreatedAsync();
            var fixture = new InspectionFixture(native, connection, options);
            await using (var context = native.Context())
            {
                var unitOfWork = new EfCoreUnitOfWork(context);
                var writer = new EventResourceSettingsWriter(context,
                    new RelationalSettingMutationLock(context, unitOfWork), unitOfWork);
                var policy = await writer.ApplyInternalAsync([
                    new(null, GovernanceSettingKeys.EventResources.AllowUnscannedDocuments,
                        EventResourceSettingMutationKind.SetValue, "true"),
                    new(null, GovernanceSettingKeys.EventResources.ExternalOrigins,
                        EventResourceSettingMutationKind.SetValue, "[\"https://example.invalid\"]")
                ], native.Author);
                await Assert.That(policy.Success).IsTrue();
                var resource = await context.EventResources.SingleAsync(row => row.Id == native.ResourceId);
                if (file)
                {
                    fixture.Expected = new byte[length];
                    "%PDF-1.7\n"u8.CopyTo(fixture.Expected);
                    var storage = await context.Set<StorageObject>().SingleAsync(row => row.Id == resource.StorageObjectId);
                    if (separateStorageActor)
                    {
                        var eventActorType = await context.Events.Where(row => row.Id == resource.EventId)
                            .Select(row => row.Actor.ActorTypeId).SingleAsync();
                        var storageActor = new Actor
                        {
                            Id = Guid.CreateVersion7(),
                            ActorTypeId = eventActorType,
                            ActorType = null!,
                            UserId = native.Author,
                            Pii = null!
                        };
                        context.Actors.Add(storageActor);
                        storage.ActorId = storageActor.Id;
                        fixture.StorageActorId = storageActor.Id;
                    }
                    var binding = await context.Set<StorageProviderBinding>().SingleAsync(row => row.Id == storage.StorageProviderBindingId);
                    fixture.local = new LocalFileStorageProvider(
                        Options.Create(new LocalFileStorageOptions { RootPath = binding.LocalRootPath! }),
                        NullLogger<LocalFileStorageProvider>.Instance, boundTarget: true);
                    using var source = new MemoryStream(fixture.Expected, writable: false);
                    var written = await fixture.local.WriteAsync(new(native.Database.Scope.Id, source,
                        "application/pdf", "inspection.pdf", "pdf", fixture.Expected.Length, fixture.Expected.Length), default);
                    fixture.objectKey = written.ObjectKey;
                    fixture.filePath = Path.Combine(binding.LocalRootPath!, written.ObjectKey.Replace('/', Path.DirectorySeparatorChar));
                    storage.ObjectKey = written.ObjectKey;
                    storage.Size = written.SizeBytes;
                    storage.ContentType = written.ContentType;
                    storage.Sha256Checksum = written.Sha256Checksum
                        ?? throw new InvalidOperationException("The native local provider must return a content hash.");
                    storage.ProviderVersionId = written.ProviderVersionId;
                    storage.RecordEventResourceInspection(storage.Id, storage.Sha256Checksum);
                }
                else
                {
                    const string destination = "https://example.invalid/material?inspection=finite";
                    fixture.Expected = System.Text.Encoding.UTF8.GetBytes(destination);
                    // Complete the seed's deliberately opaque envelope before any fresh
                    // purpose captures this material. No qualification row is fabricated.
                    context.Entry(resource).Property(row => row.ExternalDestinationCiphertext).CurrentValue =
                        fixture.Protector.Protect(destination, resource.TenantId, resource.Id, 1);
                }
                await context.SaveChangesAsync();
            }
            var purpose = native.Intent(native.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
            {
                RequiredApprovals = 1,
                MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
            };
            await native.AcceptAsync(native.Author, purpose);
            fixture.PurposeId = purpose.OperationId;
            var adoption = native.Intent(native.Approver, ResourceSafetyReviewStep.AdoptPurpose, purpose.OperationId);
            await native.AcceptAsync(native.Approver, adoption);
            await using (var context = native.Context())
            {
                context.ModerationTrustDomainId = native.Database.Scope.TrustDomainId;
                var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
                    .Where(row => row.Id == native.Database.EpochId).Select(row => row.Revision).SingleAsync();
                fixture.Intent = new(native.CauseId, native.ResourceId, native.MaterialId, native.CaseId, 1,
                    adoption.OperationId, native.Assignments[native.Author], 3, epoch);
            }
            return fixture;
        }

        public InspectionSession Session(Guid? human = null) => new(this, human ?? Native.Author);

        public Task CorruptProviderBytesAsync() =>
            File.WriteAllBytesAsync(filePath ?? throw new InvalidOperationException("A native file is required."),
                new byte[Expected.Length]);

        public async Task DiscardProviderBytesAsync()
        {
            if (local is null || objectKey is null) throw new InvalidOperationException("A native file is required.");
            await local.DeleteAsync(new(objectKey), default);
        }

        public async ValueTask DisposeAsync()
        {
            if (local is not null && objectKey is not null)
                await local.DeleteAsync(new(objectKey), default);
            await retainedConnection.DisposeAsync();
            await Native.DisposeAsync();
        }
    }

    private sealed class InspectionSession : IAsyncDisposable
    {
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        public ExploreDbContext Context { get; }
        public ModerationResourceQuarantineInspectionRepository Owner { get; }
        public ModerationResourceQuarantineStateRepository StateOwner { get; }

        public InspectionSession(InspectionFixture fixture, Guid human)
        {
            Context = fixture.Native.Context();
            var mutationLock = new RelationalSettingMutationLock(Context, new EfCoreUnitOfWork(Context));
            var systemSettings = SystemSettingRepository.CreateReadOnly(Context, mutationLock);
            var tenantSettings = TenantSettingRepository.CreateReadOnly(Context, mutationLock);
            var settings = new HierarchicalSettingsReader(systemSettings, tenantSettings,
                new OrganizationSettingRepository(Context), new GroupSettingRepository(Context),
                UserPreferenceRepository.CreateReadOnly(Context), cache,
                new AtprotoFederationSettingRepository(Context, null));
            var identity = Identity(Context, human);
            var bindings = new StorageProviderBindingService(new StorageProviderBindingRepository(Context),
                settings, Substitute.For<IRetainedSecretResolver>(),
                Options.Create(new LocalFileStorageOptions()),
                Substitute.For<IS3ClientFactory>(), NullLoggerFactory.Instance);
            var policy = new EventResourceGovernancePolicyReader(systemSettings, tenantSettings);
            var release = new ModerationResourceQuarantineReleaseRepository(
                Context, identity.Admin, identity.Machine, fixture.Native.Clock);
            Owner = new(release, fixture.Retained(),
                new ModerationResourceInspectionMaterialReader(bindings, fixture.Protector, policy));
            StateOwner = new(release, fixture.Retained());
        }

        public Task<ModerationResourceQuarantineInspectionResult> PendingAsync(ModerationResourceQuarantineInspectionIntent intent) =>
            new InspectModerationResourceQuarantineQueryHandler(Owner).QueryAsync(new(intent));

        public Task<ModerationResourceQuarantineStateResult> PendingStateAsync(ModerationResourceQuarantineStateIntent intent) =>
            new ReadModerationResourceQuarantineStateQueryHandler(StateOwner).QueryAsync(new(intent));

        public Task<ModerationResourceQuarantineReviewStateResult> PendingReviewStateAsync(
            ModerationResourceQuarantineReviewStateIntent intent) =>
            new ReadModerationResourceQuarantineReviewStateQueryHandler(StateOwner).QueryAsync(new(intent));

        public async ValueTask DisposeAsync()
        {
            cache.Dispose();
            await Context.DisposeAsync();
        }
    }

    private sealed class InspectionAuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }
}
