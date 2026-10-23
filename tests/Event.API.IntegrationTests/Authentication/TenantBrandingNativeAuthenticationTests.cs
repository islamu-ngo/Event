using System.Data;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[Category("Runtime")]
[NotInParallel]
public sealed partial class TenantBrandingNativeAuthenticationTests(PostgreSqlContainerFixture fixture)
{
    [Before(Test)]
    public async Task ResetDatabaseBeforeEachCase() => await fixture.ResetAsync();

    [Test]
    [Arguments("displayName")]
    [Arguments("logoUrl")]
    [Arguments("faviconUrl")]
    [Arguments("customCssUrl")]
    [Arguments("whiteLabeling")]
    [Arguments("otherField")]
    [Arguments("unlocked")]
    [Arguments("documentCas")]
    [Arguments("documentWriteCas")]
    public async Task Patch_AfterPreflight_UsesCommittedFieldLocksAndDocumentStamp(string change)
    {
        Guid tenantId = await SeedTenantAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var updateGate = new DocumentUpdateGate();
        await using var setup = fixture.CreateDbContext();
        await using var settingsIdentity = await AuthenticatedSettingsFixture.CreateAsync(
            () => fixture.CreateDbContext(), fixture);
        await SetInstanceValueAsync(settingsIdentity, GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"");
        await SetInstanceValueAsync(settingsIdentity, GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "true");
        foreach (string key in new[]
        {
            GovernanceSettingKeys.Branding.DisplayName, GovernanceSettingKeys.Branding.LogoUrl,
            GovernanceSettingKeys.Branding.FaviconUrl, GovernanceSettingKeys.Branding.CustomCssUrl
        })
            await SetInstanceLockAsync(settingsIdentity, key, false);
        var setupResolver = Resolver(setup, cache);
        var original = await Provisioner(setup, setupResolver).EnsureTenantBrandingDocumentAsync(tenantId, "Original");
        await setup.Entry(original).ReloadAsync();
        Guid originalStamp = original.ConcurrencyStamp;
        string originalJson = original.PayloadJson;
        setup.Entry(original).State = EntityState.Detached;
        await ResolveAsync(setupResolver, tenantId);
        await using var identity = await TenantBrandingNativeAuthenticationFixture.CreateAsync(fixture, tenantId, updateGate);
        var owner = identity.Owner;
        var unit = new EfCoreUnitOfWork(owner);
        var mutationLock = new RelationalSettingMutationLock(owner, unit);
        var resolver = Resolver(owner, cache);
        var gate = new PreflightLockGate(new TenantBrandingSettingsDocumentLockService(
            SystemSettingRepository.CreateReadOnly(owner, mutationLock), mutationLock));
        var handler = new PatchTenantBrandingSettingsDocumentCommandHandler(
            new TenantSettingsDocumentRepository(owner), unit, resolver, gate,
            identity.HumanScope, identity.TenantContext, identity.Authority, identity.Preparation, identity.Finalizer);
        string requestedField = change is "logoUrl" or "faviconUrl" or "customCssUrl" or "whiteLabeling"
            ? change == "whiteLabeling" ? "logoUrl" : change
            : "displayName";
        var patch = new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = originalStamp,
            DisplayName = requestedField == "displayName"
                ? new() { Value = OptionalUpdate<string?>.Set("Patched") } : null,
            Assets = requestedField == "displayName" ? null : new()
            {
                LogoUrl = requestedField == "logoUrl" ? OptionalUpdate<string?>.Set("https://branding.example/logo")
                    : OptionalUpdate<string?>.Unspecified(),
                FaviconUrl = requestedField == "faviconUrl" ? OptionalUpdate<string?>.Set("https://branding.example/favicon")
                    : OptionalUpdate<string?>.Unspecified(),
                CustomCssUrl = requestedField == "customCssUrl" ? OptionalUpdate<string?>.Set("https://branding.example/style")
                    : OptionalUpdate<string?>.Unspecified()
            }
        };
        var pending = identity.InOriginalRequestAsync(() => handler.ExecuteAsync(new PatchTenantBrandingSettingsDocumentCommand
        {
            TenantId = tenantId,
            Patch = patch,
            IsLockedByInstance = false
        }, default));
        try
        {
            await gate.Observed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var writer = fixture.CreateDbContext();
            if (change is "documentCas" or "documentWriteCas")
            {
                if (change == "documentWriteCas")
                {
                    gate.Release.TrySetResult();
                    await updateGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                }
                var winner = (await new TenantSettingsDocumentRepository(writer)
                    .GetTrackedByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!;
                winner.UpdatePayload(winner.SchemaVersion, winner.DefaultsVersion,
                    JsonSerializer.Serialize(new BrandingSettings { DisplayName = "Winner" }));
                await writer.SaveChangesAsync();
            }
            else if (change == "whiteLabeling")
            {
                await SetInstanceValueAsync(settingsIdentity, GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "false");
            }
            else if (change != "unlocked")
            {
                string key = change switch
                {
                    "displayName" => GovernanceSettingKeys.Branding.DisplayName,
                    "logoUrl" => GovernanceSettingKeys.Branding.LogoUrl,
                    "faviconUrl" => GovernanceSettingKeys.Branding.FaviconUrl,
                    "customCssUrl" => GovernanceSettingKeys.Branding.CustomCssUrl,
                    _ => GovernanceSettingKeys.Branding.LogoUrl
                };
                await SetInstanceLockAsync(settingsIdentity, key, true);
            }
            gate.Release.TrySetResult();
            updateGate.Release.TrySetResult();
            if (change is "documentCas" or "documentWriteCas")
            {
                await Assert.ThrowsAsync<ConcurrencyConflictException>(
                    async () => await pending.WaitAsync(TimeSpan.FromMinutes(2)));
            }
            else
            {
                var response = await pending.WaitAsync(TimeSpan.FromMinutes(2));
                bool allowed = change is "otherField" or "unlocked";
                await Assert.That(response.IsSuccess).IsEqualTo(allowed);
                await using var reader = fixture.CreateDbContext();
                var persisted = (await new TenantSettingsDocumentRepository(reader)
                    .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!;
                if (allowed)
                {
                    await Assert.That(Deserialize(persisted).DisplayName).IsEqualTo("Patched");
                    await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(originalStamp);
                    await Assert.That(response.Id!.CanChangeDisplayName).IsTrue();
                    await Assert.That(response.Id.CanChangeLogoUrl).IsEqualTo(change != "otherField");
                    await Assert.That((await ResolveAsync(resolver, tenantId))!.Payload.DisplayName).IsEqualTo("Patched");
                }
                else
                {
                    await Assert.That(response.Errors!.Count).IsGreaterThan(0);
                    await Assert.That(persisted.PayloadJson).IsEqualTo(originalJson);
                    await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(originalStamp);
                    await Assert.That((await ResolveAsync(resolver, tenantId))!.Payload.DisplayName).IsEqualTo("Original");
                }
            }
            if (change is "documentCas" or "documentWriteCas")
            {
                await using var reader = fixture.CreateDbContext();
                var persisted = (await new TenantSettingsDocumentRepository(reader)
                    .GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!;
                await Assert.That(Deserialize(persisted).DisplayName).IsEqualTo("Winner");
            }
        }
        finally
        {
            gate.Release.TrySetResult();
            updateGate.Release.TrySetResult();
            try
            {
                await pending.WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch when (pending.IsCompleted)
            {
                // The assertion above owns the operation's failure; observe it during cleanup.
            }
        }
    }

    private static BrandingSettings Deserialize(TenantSettingsDocument document) =>
        JsonSerializer.Deserialize<BrandingSettings>(document.PayloadJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed class DocumentUpdateGate : SaveChangesInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TenantSettingsDocument>()
                .Any(entry => entry.State == EntityState.Modified && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.Branding))
            {
                var transaction = eventData.Context.Database.CurrentTransaction;
                await Assert.That(transaction).IsNotNull();
                await Assert.That(transaction!.GetDbTransaction().IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
            }
            return result;
        }
    }

    private sealed class PreflightLockGate(ITenantBrandingSettingsDocumentLockService inner)
        : ITenantBrandingSettingsDocumentLockService
    {
        private bool _observed;
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TenantBrandingSettingsDocumentLockState> GetLockStateAsync(CancellationToken cancellationToken = default)
        {
            var state = await inner.GetLockStateAsync(cancellationToken);
            if (!_observed)
            {
                _observed = true;
                Observed.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromMinutes(2), cancellationToken);
            }
            return state;
        }

        public Task<T> ExecuteWithGovernanceLocksAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            inner.ExecuteWithGovernanceLocksAsync(operation, cancellationToken);

        public IReadOnlyList<string> ValidateAllowedChanges(
            BrandingSettings currentPayload, BrandingSettings requestedPayload, TenantBrandingSettingsDocumentLockState lockState) =>
            inner.ValidateAllowedChanges(currentPayload, requestedPayload, lockState);
    }

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
        var sourceGate = new PendingTenantSavepointGate(otherTenantId);
        await using var owner = fixture.CreateDbContext(loserGate, sourceGate);
        await owner.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            // Document writes share the provisioning-source fence, even across tenants.
            // Borrow unrelated persisted work so the independent winner can commit.
            var marker = new Tag
            {
                Id = Guid.CreateVersion7(),
                TenantId = otherTenantId,
                Tenant = null!,
                MasterCode = $"caller-{Guid.CreateVersion7():N}",
                FullName = "Caller marker"
            };
            // Seed only the unrelated row. SaveChanges would also retain tenant/source
            // fences before the race, preventing the independent winner from committing.
            await using (var markerCommand = owner.Database.GetDbConnection().CreateCommand())
            {
                markerCommand.Transaction = transaction.GetDbTransaction();
                var table = owner.Model.FindEntityType(typeof(Tag))!;
                var sql = owner.GetService<ISqlGenerationHelper>();
                markerCommand.CommandText = $"""
                    INSERT INTO {sql.DelimitIdentifier(table.GetTableName()!, table.GetSchema())}
                        (id,tenant_id,master_code,full_name) VALUES (@id,@tenant,@code,@name)
                    """;
                foreach (var (name, value) in new (string, object)[]
                {
                    ("id", marker.Id), ("tenant", marker.TenantId),
                    ("code", marker.MasterCode), ("name", marker.FullName)
                })
                {
                    var parameter = markerCommand.CreateParameter();
                    parameter.ParameterName = name;
                    parameter.Value = value;
                    markerCommand.Parameters.Add(parameter);
                }
                await Assert.That(await markerCommand.ExecuteNonQueryAsync()).IsEqualTo(1);
            }
            owner.Attach(marker);
            var trackedTenant = (await owner.Tenants.FindAsync(otherTenantId))!;
            string originalName = trackedTenant.FullName;
            trackedTenant.FullName = "Caller pending change";
            Task<TenantSettingsDocument> losing = Provisioner(owner, Resolver(owner, cache))
                .EnsureTenantBrandingDocumentAsync(tenantId, "Losing proposal");
            try
            {
                // Pause before source/disclosure locks, not after SaveChanges has
                // fenced the caller's pending Tenant mutation against the winner.
                await sourceGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await using var writer = fixture.CreateDbContext();
                var winner = await Provisioner(writer, Resolver(writer, cache))
                    .EnsureTenantBrandingDocumentAsync(tenantId, "Committed winner");
                sourceGate.Release.TrySetResult();
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
                var persistedMarker = await reader.Tags.AsNoTracking().SingleOrDefaultAsync(row => row.Id == marker.Id);
                if (rollback) await Assert.That(persistedMarker).IsNull();
                else await Assert.That(persistedMarker).IsNotNull();
                await Assert.That((await reader.Tenants.FindAsync(otherTenantId))!.FullName)
                    .IsEqualTo(rollback ? originalName : "Caller pending change");
                await AssertSameDocumentAsync((await repository.GetByTenantAndDocumentKey(tenantId, SettingsDocumentKeys.Tenant.Branding))!, winner);
            }
            finally
            {
                sourceGate.Release.TrySetResult();
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

    private sealed class PendingTenantSavepointGate(Guid tenantId) : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task CreatedSavepointAsync(System.Data.Common.DbTransaction transaction,
            TransactionEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Tenant>()
                .Any(entry => entry.Entity.Id == tenantId && entry.State == EntityState.Modified)
                && Entered.TrySetResult())
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
    }
}
