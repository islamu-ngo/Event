using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantBrandingProvisioningConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TwoMissingDocuments_WithDifferentNames_ConvergeWithoutOverwritingWinnerOrWarmCache()
    {
        Guid tenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var firstGate = new InsertGate();
        var secondGate = new InsertGate();
        await using var firstContext = fixture.CreateDbContext(firstGate);
        await using var secondContext = fixture.CreateDbContext(secondGate);
        var firstResolver = Resolver(firstContext, cache);
        var secondResolver = Resolver(secondContext, cache);
        await Assert.That(await ResolveAsync(firstResolver, tenantId)).IsNull();
        Task<TenantSettingsDocument> first = Provisioner(firstContext, firstResolver)
            .EnsureTenantBrandingDocumentAsync(tenantId, "First writer");
        Task<TenantSettingsDocument> second = Provisioner(secondContext, secondResolver)
            .EnsureTenantBrandingDocumentAsync(tenantId, "Must not replace winner");
        try
        {
            await Task.WhenAll(firstGate.Entered.Task, secondGate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(15));
            firstGate.Release.TrySetResult();
            var winner = await first.WaitAsync(TimeSpan.FromSeconds(15));
            Guid revision = winner.ConcurrencyStamp;
            secondGate.Release.TrySetResult();
            var converged = await second.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertSameDocumentAsync(converged, winner);
            await Assert.That(converged.ConcurrencyStamp).IsEqualTo(revision);
            await Assert.That(secondContext.Entry(secondGate.Candidate!).State).IsEqualTo(EntityState.Detached);
            await Assert.That(secondContext.Entry(converged).State).IsEqualTo(EntityState.Unchanged);
            await Assert.That((await ResolveAsync(secondResolver, tenantId))!.Payload.DisplayName).IsEqualTo("First writer");
            var documents = await new TenantSettingsDocumentRepository(secondContext)
                .GetManyForTenant(tenantId, [SettingsDocumentKeys.Tenant.Branding]);
            await Assert.That(documents.Count).IsEqualTo(1);
            await Assert.That(documents[0].ConcurrencyStamp).IsEqualTo(revision);
        }
        finally
        {
            firstGate.Release.TrySetResult();
            secondGate.Release.TrySetResult();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AmbientConflict_PreservesSavepointTrackingAndCallerCommitOrRollback(bool rollback)
    {
        Guid tenantId = await SeedTenantAsync();
        Guid otherTenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var loserGate = new InsertGate();
        await using var owner = fixture.CreateDbContext(loserGate);
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var marker = TenantSettingsDocument.Create(otherTenantId, SettingsDocumentKeys.Tenant.PublicExperience, 1, "test", "{}");
            owner.TenantSettingsDocuments.Add(marker);
            await owner.SaveChangesAsync();
            var trackedTenant = (await owner.Tenants.FindAsync(otherTenantId))!;
            string originalName = trackedTenant.FullName;
            trackedTenant.FullName = "Caller pending change";
            Task<TenantSettingsDocument> losing = Provisioner(owner, Resolver(owner, cache))
                .EnsureTenantBrandingDocumentAsync(tenantId, "Losing proposal");
            try
            {
                await loserGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await using var writer = fixture.CreateDbContext();
                var winner = await Provisioner(writer, Resolver(writer, cache))
                    .EnsureTenantBrandingDocumentAsync(tenantId, "Committed winner");
                loserGate.Release.TrySetResult();
                var converged = await losing.WaitAsync(TimeSpan.FromSeconds(15));
                await AssertSameDocumentAsync(converged, winner);
                await Assert.That(owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(owner.Entry(loserGate.Candidate!).State).IsEqualTo(EntityState.Detached);
                await Assert.That(owner.Entry(trackedTenant).State).IsEqualTo(EntityState.Modified);
                await Assert.That(trackedTenant.FullName).IsEqualTo("Caller pending change");
                await Assert.That(owner.Entry(marker).State).IsEqualTo(EntityState.Unchanged);
                await owner.SaveChangesAsync();
                if (rollback) await transaction.RollbackAsync();
                else await transaction.CommitAsync();

                await using var reader = fixture.CreateDbContext();
                var repository = new TenantSettingsDocumentRepository(reader);
                var persistedMarker = await repository.GetByTenantAndDocumentKey(otherTenantId, SettingsDocumentKeys.Tenant.PublicExperience);
                if (rollback) await Assert.That(persistedMarker).IsNull();
                else await Assert.That(persistedMarker).IsNotNull();
                await Assert.That((await reader.Tenants.FindAsync(otherTenantId))!.FullName)
                    .IsEqualTo(rollback ? originalName : "Caller pending change");
                await AssertSameDocumentAsync((await repository.GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!, winner);
            }
            finally
            {
                loserGate.Release.TrySetResult();
            }
        });
    }

    [Test]
    public async Task SnapshotConflict_EscapesForOwnerRollbackAndFreshTransactionRetry()
    {
        Guid tenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gate = new InsertGate();
        await using var owner = fixture.CreateDbContext(gate);
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using (var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
            {
                var pending = Provisioner(owner, Resolver(owner, cache)).EnsureTenantBrandingDocumentAsync(tenantId, "Stale snapshot");
                try
                {
                    await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    await using var writer = fixture.CreateDbContext();
                    await Provisioner(writer, Resolver(writer, cache)).EnsureTenantBrandingDocumentAsync(tenantId, "Snapshot winner");
                    gate.Release.TrySetResult();
                    await Assert.ThrowsAsync<DbUpdateException>(() => pending);
                    await Assert.That(owner.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                    await Assert.That(owner.Entry(gate.Candidate!).State).IsEqualTo(EntityState.Detached);
                    await Assert.That(await new TenantSettingsDocumentRepository(owner)
                        .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
                    await transaction.RollbackAsync();
                }
                finally
                {
                    gate.Release.TrySetResult();
                }
            }
            await using var retry = fixture.CreateDbContext();
            var resolved = await Provisioner(retry, Resolver(retry, cache)).EnsureTenantBrandingDocumentAsync(tenantId);
            await Assert.That((await ResolveAsync(Resolver(retry, cache), tenantId))!.Payload.DisplayName).IsEqualTo("Snapshot winner");
            await Assert.That(retry.Entry(resolved).State).IsEqualTo(EntityState.Unchanged);
        });
    }

    [Test]
    public async Task MissingTenantAndPrimaryKeyCollision_AreNotMistakenForProvisioningRaces()
    {
        Guid tenantId = await SeedTenantAsync();
        Guid otherTenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await using var context = fixture.CreateDbContext();
        var repository = new TenantSettingsDocumentRepository(context);
        var winner = await Provisioner(context, Resolver(context, cache)).EnsureTenantBrandingDocumentAsync(tenantId, "Original");
        Guid winnerId = winner.Id;
        context.Entry(winner).State = EntityState.Detached;
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var collision = TenantBrandingSettingsDocumentDefaults.Create(otherTenantId, "Other tenant");
            collision.Id = winnerId;
            await Assert.ThrowsAsync<DbUpdateException>(() => repository.CreateIfMissingAsync(collision));
            await Assert.That(context.Entry(collision).State).IsEqualTo(EntityState.Detached);
            Guid missingTenantId = Guid.CreateVersion7();
            await Assert.ThrowsAsync<DbUpdateException>(() => Provisioner(context, Resolver(context, cache))
                .EnsureTenantBrandingDocumentAsync(missingTenantId));
            await Assert.That(await repository.GetByTenantAndDocumentKey(missingTenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
            await Assert.That(await repository.GetByTenantAndDocumentKey(otherTenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
            await AssertSameDocumentAsync((await repository.GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!, winner);
            var other = await Provisioner(context, Resolver(context, cache)).EnsureTenantBrandingDocumentAsync(otherTenantId, "Other tenant");
            await transaction.CommitAsync();
            await Assert.That(other.Id).IsNotEqualTo(winnerId);
        });
    }

    [Test]
    public async Task CancelledInsert_RollsBackOnlyAttemptAndRetainsUsableOwnerTransaction()
    {
        Guid tenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var gate = new InsertGate();
        await using var context = fixture.CreateDbContext(gate);
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            using var cancellation = new CancellationTokenSource();
            var pending = Provisioner(context, Resolver(context, cache)).EnsureTenantBrandingDocumentAsync(tenantId, cancellationToken: cancellation.Token);
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            await Assert.That(context.Entry(gate.Candidate!).State).IsEqualTo(EntityState.Detached);
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(await new TenantSettingsDocumentRepository(context).GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
            gate.Release.TrySetResult();
            await Provisioner(context, Resolver(context, cache)).EnsureTenantBrandingDocumentAsync(tenantId, "After cancellation");
            await transaction.CommitAsync();
            await using var reader = fixture.CreateDbContext();
            await Assert.That((await ResolveAsync(Resolver(reader, cache), tenantId))!.Payload.DisplayName).IsEqualTo("After cancellation");
        });
    }

    private async Task<Guid> SeedTenantAsync()
    {
        await using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Provisioning tenant",
            Slug = $"provision-{Guid.CreateVersion7():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static TypedSettingsDocumentResolver Resolver(ExploreDbContext context, IMemoryCache cache) =>
        new(new TenantSettingsDocumentRepository(context), cache);

    private static TenantBrandingSettingsDocumentProvisioningService Provisioner(ExploreDbContext context, ITypedSettingsDocumentResolver resolver) =>
        new(new TenantRepository(context), new TenantSettingsDocumentRepository(context), resolver);

    private static Task<ResolvedSettingsDocument<BrandingSettings>?> ResolveAsync(ITypedSettingsDocumentResolver resolver, Guid tenantId) =>
        resolver.ResolveTenantDocumentAsync<BrandingSettings>(new SettingsResolutionContext(tenantId,
            RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]), SettingsDocumentKeys.Tenant.Branding);

    private static async Task AssertSameDocumentAsync(TenantSettingsDocument actual, TenantSettingsDocument expected)
    {
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.TenantId).IsEqualTo(expected.TenantId);
        await Assert.That(actual.DocumentKey).IsEqualTo(expected.DocumentKey);
        using var actualPayload = JsonDocument.Parse(actual.PayloadJson);
        using var expectedPayload = JsonDocument.Parse(expected.PayloadJson);
        await Assert.That(JsonElement.DeepEquals(actualPayload.RootElement, expectedPayload.RootElement)).IsTrue();
        await Assert.That(actual.ConcurrencyStamp).IsEqualTo(expected.ConcurrencyStamp);
        await Assert.That(actual.SchemaVersion).IsEqualTo(expected.SchemaVersion);
        await Assert.That(actual.DefaultsVersion).IsEqualTo(expected.DefaultsVersion);
        // PostgreSQL stores timestamps at microsecond precision.
        await Assert.That(actual.CreatedAt.Ticks / 10).IsEqualTo(expected.CreatedAt.Ticks / 10);
        await Assert.That(actual.UpdatedAt).IsEqualTo(expected.UpdatedAt);
    }

    private sealed class InsertGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TenantSettingsDocument? Candidate { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Candidate = eventData.Context!.ChangeTracker.Entries<TenantSettingsDocument>()
                .SingleOrDefault(entry => entry.State == EntityState.Added && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.Branding)?.Entity;
            if (Candidate is not null)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
