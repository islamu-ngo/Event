// ABOUTME: Testcontainers integration tests for projection coordination: dirty-scope upsert, drain, and rebuild status tracking.
// ABOUTME: Covers D1 correctness invariants required by CTO Rule 17 before Milestone D1 can exit.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Projections;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Projections;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
public class CustomPropertyProjectionCoordinationTests
{
    private readonly ProjectionTestContainerFixture _fixture;

    public CustomPropertyProjectionCoordinationTests(ProjectionTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task DirtyScopeRepository_UpsertAsync_IsIdempotentForSameScopeKey()
    {
        using var context = _fixture.CreateDbContext();
        var repo = new CustomPropertyProjectionDirtyScopeRepository(context);

        var tenantId = await SeedTenantAsync(context);
        var eventId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();

        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            eventId,
            definitionId,
            "rebuild_in_progress",
            CancellationToken.None);
        await context.SaveChangesAsync();

        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            eventId,
            definitionId,
            "rebuild_in_progress",
            CancellationToken.None);
        await context.SaveChangesAsync();

        using var verify = _fixture.CreateDbContext();
        var rows = await verify.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .Where(r =>
                r.TenantId == tenantId
                && r.ScopeId == eventId
                && r.DefinitionId == definitionId
                && r.DrainedAt == null)
            .ToListAsync();

        await Assert.That(rows.Count).IsEqualTo(1);
    }

    [Test]
    public async Task DirtyScopeRepository_MarkDrainedAsync_OnlyUpdatesTargetedRows()
    {
        using var context = _fixture.CreateDbContext();
        var repo = new CustomPropertyProjectionDirtyScopeRepository(context);

        var tenantId = await SeedTenantAsync(context);
        var keepId = Guid.NewGuid();
        var drainId = Guid.NewGuid();

        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            keepId,
            definitionId: null,
            "rebuild_in_progress",
            CancellationToken.None);
        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            drainId,
            definitionId: null,
            "rebuild_in_progress",
            CancellationToken.None);
        await context.SaveChangesAsync();

        var pending = await repo.GetPendingAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            batchSize: 100,
            CancellationToken.None);
        var drainRow = pending.Single(p => p.ScopeId == drainId);

        await repo.MarkDrainedAsync(new[] { drainRow.Id }, DateTimeOffset.UtcNow, CancellationToken.None);

        using var verify = _fixture.CreateDbContext();
        var kept = await verify.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .FirstAsync(r => r.ScopeId == keepId);
        var drained = await verify.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .FirstAsync(r => r.ScopeId == drainId);

        await Assert.That(kept.DrainedAt).IsNull();
        await Assert.That(drained.DrainedAt).IsNotNull();
    }

    [Test]
    public async Task DirtyScopeRepository_CountPendingAsync_IgnoresDrainedRows()
    {
        using var context = _fixture.CreateDbContext();
        var repo = new CustomPropertyProjectionDirtyScopeRepository(context);

        var tenantId = await SeedTenantAsync(context);
        var scopeA = Guid.NewGuid();
        var scopeB = Guid.NewGuid();

        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            scopeA,
            null,
            "rebuild_in_progress",
            CancellationToken.None);
        await repo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CustomPropertyProjectionScopeType.Event,
            scopeB,
            null,
            "rebuild_in_progress",
            CancellationToken.None);
        await context.SaveChangesAsync();

        var pendingBefore = await repo.CountPendingAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CancellationToken.None);
        await Assert.That(pendingBefore).IsEqualTo(2);

        var pending = await repo.GetPendingAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            batchSize: 100,
            CancellationToken.None);
        await repo.MarkDrainedAsync(pending.Select(p => p.Id).ToList(), DateTimeOffset.UtcNow, CancellationToken.None);

        var pendingAfter = await repo.CountPendingAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            tenantId,
            CancellationToken.None);
        await Assert.That(pendingAfter).IsEqualTo(0);
    }

    [Test]
    public async Task DrainDirtyScopesForTenantAsync_RefreshesAndMarksAllPending()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateEventUpdater(context);

        // Seed a full tenant/event/def/value graph so the drain has a real event to refresh.
        var scope = await SeedEventWithDefinitionAsync(context);
        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = scope.DefinitionId,
            EventId = scope.EventId,
            TenantId = scope.TenantId,
            TextValue = "drain-test",
            Ordinal = 0,
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();

        // Register a pending dirty-scope row pointing at this event
        var dirtyScopeRepo = new CustomPropertyProjectionDirtyScopeRepository(context);
        await dirtyScopeRepo.UpsertAsync(
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            scope.TenantId,
            CustomPropertyProjectionScopeType.Event,
            scope.EventId,
            scope.DefinitionId,
            "manual",
            CancellationToken.None);
        await context.SaveChangesAsync();

        var drained = await updater.DrainDirtyScopesForTenantAsync(scope.TenantId, CancellationToken.None);
        await Assert.That(drained).IsGreaterThanOrEqualTo(1);

        using var verify = _fixture.CreateDbContext();
        var projection = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.EventCustomPropertyValueId == value.Id);
        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.NormalizedValue).IsEqualTo("drain-test");

        var pending = await verify.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .CountAsync(r => r.TenantId == scope.TenantId && r.DrainedAt == null);
        await Assert.That(pending).IsEqualTo(0);
    }

    [Test]
    public async Task ProjectionStatusRepository_UpsertAsync_OverwritesExistingRow()
    {
        using var context = _fixture.CreateDbContext();
        var repo = new CustomPropertyProjectionStatusRepository(context);

        var tenantId = await SeedTenantAsync(context);

        await repo.UpsertAsync(new CustomPropertyProjectionStatus
        {
            ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName,
            ProjectionVersion = IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            TenantId = tenantId,
            State = CustomPropertyProjectionState.Rebuilding,
            RowsProcessed = 0,
        }, CancellationToken.None);

        await repo.UpsertAsync(new CustomPropertyProjectionStatus
        {
            ProjectionName = IEventCustomPropertyProjectionUpdater.ProjectionName,
            ProjectionVersion = IEventCustomPropertyProjectionUpdater.ProjectionVersion,
            TenantId = tenantId,
            State = CustomPropertyProjectionState.Idle,
            RowsProcessed = 42,
            LastRebuildCompletedAt = DateTimeOffset.UtcNow,
        }, CancellationToken.None);

        using var verify = _fixture.CreateDbContext();
        var rows = await verify.CustomPropertyProjectionStatuses
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync();

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].State).IsEqualTo(CustomPropertyProjectionState.Idle);
        await Assert.That(rows[0].RowsProcessed).IsEqualTo(42);
    }

    #region Helpers

    private static EventCustomPropertyProjectionUpdater CreateEventUpdater(ExploreDbContext context)
    {
        var statusRepo = new CustomPropertyProjectionStatusRepository(context);
        var dirtyScopeRepo = new CustomPropertyProjectionDirtyScopeRepository(context);
        var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context));
        var tenantSettingRepo = new TenantSettingRepository(context, mutationLock);
        var systemSettingRepo = new SystemSettingRepository(context, mutationLock);
        var quotaResolver = new CustomPropertyQuotaResolver(tenantSettingRepo, systemSettingRepo);

        return new EventCustomPropertyProjectionUpdater(
            context,
            dirtyScopeRepo,
            statusRepo,
            quotaResolver,
            new ProjectionMetrics(new TestMeterFactory()));
    }

    private static async Task<Guid> SeedTenantAsync(ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            FullName = "Coord Tenant",
            Slug = "coord-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = 2,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static async Task<CoordTestScope> SeedEventWithDefinitionAsync(ExploreDbContext context)
    {
        var tenant = new Tenant
        {
            FullName = "Drain Tenant",
            Slug = "drain-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = 2,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"drain-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Drain",
                LastName = "Tester",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Drain Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Drain Event",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            TotalViews = 0,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        context.Events.Add(@event);

        var definition = new EventCustomPropertyDefinition
        {
            EventId = @event.Id,
            TenantId = tenant.Id,
            Namespace = "tenant.community",
            Key = $"drain-field-{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Drain Field",
            PropertyType = PropertyType.Text,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            InstantiatedAt = DateTimeOffset.UtcNow,
        };
        context.EventCustomPropertyDefinitions.Add(definition);
        await context.SaveChangesAsync();

        return new CoordTestScope(tenant.Id, @event.Id, definition.Id);
    }

    private sealed record CoordTestScope(Guid TenantId, Guid EventId, Guid DefinitionId);

    #endregion
}
