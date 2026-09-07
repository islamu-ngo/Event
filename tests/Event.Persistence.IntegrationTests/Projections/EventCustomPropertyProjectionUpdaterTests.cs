using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Projections;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Projections;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
public class EventCustomPropertyProjectionUpdaterTests
{
    private readonly ProjectionTestContainerFixture _fixture;

    public EventCustomPropertyProjectionUpdaterTests(ProjectionTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task UpdateForValueAsync_InsertsProjectionRow_WhenRuntimeValueCreated()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Text);
        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = scope.DefinitionId,
            EventId = scope.EventId,
            TenantId = scope.TenantId,
            TextValue = "Hello World",
            Ordinal = 0,
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();

        await updater.UpdateForValueAsync(value.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        using var verify = _fixture.CreateDbContext();
        var projection = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.EventCustomPropertyValueId == value.Id);

        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.TextValue).IsEqualTo("Hello World");
        await Assert.That(projection.NormalizedValue).IsEqualTo("hello world");
        await Assert.That(projection.Namespace).IsEqualTo("tenant.community");
        await Assert.That(projection.IsSearchable).IsTrue();
    }

    [Test]
    public async Task UpdateForValueAsync_UpsertsSingleRow_WhenValueUpdatedTwice()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Number);
        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = scope.DefinitionId,
            EventId = scope.EventId,
            TenantId = scope.TenantId,
            NumberValue = 42m,
            Ordinal = 0,
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();

        await updater.UpdateForValueAsync(value.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        value.NumberValue = 99m;
        await context.SaveChangesAsync();
        await updater.UpdateForValueAsync(value.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        using var verify = _fixture.CreateDbContext();
        var rows = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.EventCustomPropertyValueId == value.Id)
            .ToListAsync();

        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].NumberValue).IsEqualTo(99m);
        await Assert.That(rows[0].NormalizedValue).IsEqualTo("99");
    }

    [Test]
    public async Task UpdateForDefinitionAsync_RefreshesExposureFlags_AcrossAllValues()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Text);
        for (var i = 0; i < 3; i++)
        {
            var value = new EventCustomPropertyValue
            {
                EventCustomPropertyDefinitionId = scope.DefinitionId,
                EventId = scope.EventId,
                TenantId = scope.TenantId,
                TextValue = $"row-{i}",
                Ordinal = i,
            };
            context.EventCustomPropertyValues.Add(value);
        }
        await context.SaveChangesAsync();

        await updater.UpdateForDefinitionAsync(scope.DefinitionId, CancellationToken.None);
        await context.SaveChangesAsync();

        var definition = await context.EventCustomPropertyDefinitions.FirstAsync(d => d.Id == scope.DefinitionId);
        definition.IsSearchable = false;
        definition.IsFilterable = false;
        definition.ExposureLevel = ExposureLevel.Internal;
        await context.SaveChangesAsync();

        await updater.UpdateForDefinitionAsync(scope.DefinitionId, CancellationToken.None);
        await context.SaveChangesAsync();

        using var verify = _fixture.CreateDbContext();
        var rows = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.EventCustomPropertyDefinitionId == scope.DefinitionId)
            .ToListAsync();

        await Assert.That(rows.Count).IsEqualTo(3);
        foreach (var row in rows)
        {
            await Assert.That(row.IsSearchable).IsFalse();
            await Assert.That(row.IsFilterable).IsFalse();
            await Assert.That(row.ExposureLevel).IsEqualTo(ExposureLevel.Internal);
        }
    }

    [Test]
    public async Task RemoveForDefinitionAsync_DeletesAllProjectionRows()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Text);
        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = scope.DefinitionId,
            EventId = scope.EventId,
            TenantId = scope.TenantId,
            TextValue = "to be deleted",
            Ordinal = 0,
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();
        await updater.UpdateForValueAsync(value.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        await updater.RemoveForDefinitionAsync(scope.DefinitionId, CancellationToken.None);

        using var verify = _fixture.CreateDbContext();
        var remaining = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .CountAsync(p => p.EventCustomPropertyDefinitionId == scope.DefinitionId);

        await Assert.That(remaining).IsEqualTo(0);
    }

    [Test]
    public async Task RefreshForEventAsync_RebuildsAllRowsForEvent()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Text);
        for (var i = 0; i < 2; i++)
        {
            var value = new EventCustomPropertyValue
            {
                EventCustomPropertyDefinitionId = scope.DefinitionId,
                EventId = scope.EventId,
                TenantId = scope.TenantId,
                TextValue = $"value-{i}",
                Ordinal = i,
            };
            context.EventCustomPropertyValues.Add(value);
        }
        await context.SaveChangesAsync();

        await updater.RefreshForEventAsync(scope.EventId, CancellationToken.None);

        using var verify = _fixture.CreateDbContext();
        var rows = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.EventId == scope.EventId)
            .OrderBy(p => p.Ordinal)
            .ToListAsync();

        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0].NormalizedValue).IsEqualTo("value-0");
        await Assert.That(rows[1].NormalizedValue).IsEqualTo("value-1");
    }

    [Test]
    public async Task RebuildForTenantAsync_PopulatesStatusRowAndProcessesEvents()
    {
        using var context = _fixture.CreateDbContext();
        var updater = CreateUpdater(context);

        var scope = await SeedEventWithDefinitionAsync(context, PropertyType.Text);
        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = scope.DefinitionId,
            EventId = scope.EventId,
            TenantId = scope.TenantId,
            TextValue = "seeded",
            Ordinal = 0,
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();

        var result = await updater.RebuildForTenantAsync(scope.TenantId, batchSize: null, CancellationToken.None);

        await Assert.That(result.LockAcquired).IsTrue();
        await Assert.That(result.RowsProcessed).IsGreaterThanOrEqualTo(1);
        await Assert.That(result.RowsFailed).IsEqualTo(0);

        using var verify = _fixture.CreateDbContext();
        var status = await verify.CustomPropertyProjectionStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == scope.TenantId);
        await Assert.That(status).IsNotNull();
        await Assert.That(status!.State).IsEqualTo(CustomPropertyProjectionState.Idle);

        var projection = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.EventCustomPropertyValueId == value.Id);
        await Assert.That(projection).IsNotNull();
        await Assert.That(projection!.NormalizedValue).IsEqualTo("seeded");
    }

    [Test]
    public async Task RebuildForTenantAsync_WithOptionValueMatchesRefreshProjection_WhenOptionIsNotTracked()
    {
        EventCustomPropertyProjection refreshed;

        using (var seedContext = _fixture.CreateDbContext())
        {
            var scope = await SeedEventWithDefinitionAsync(seedContext, PropertyType.Option);
            var option = new EventCustomPropertyOption
            {
                EventCustomPropertyDefinitionId = scope.DefinitionId,
                Namespace = "tenant.community",
                Key = "premium_access",
                DisplayName = "Premium Access",
                Value = "Premium Access",
                IsActive = true,
                IsDefault = true,
                SortOrder = 1,
                ConcurrencyStamp = Guid.NewGuid(),
            };
            seedContext.EventCustomPropertyOptions.Add(option);
            await seedContext.SaveChangesAsync();

            seedContext.EventCustomPropertyValues.Add(new EventCustomPropertyValue
            {
                EventCustomPropertyDefinitionId = scope.DefinitionId,
                EventId = scope.EventId,
                TenantId = scope.TenantId,
                OptionId = option.Id,
                Ordinal = 0,
            });
            await seedContext.SaveChangesAsync();

            using (var refreshContext = _fixture.CreateDbContext())
            {
                await CreateUpdater(refreshContext).RefreshForEventAsync(scope.EventId, CancellationToken.None);
            }

            using (var verifyRefresh = _fixture.CreateDbContext())
            {
                refreshed = await verifyRefresh.EventCustomPropertyProjections
                    .AsNoTracking()
                    .SingleAsync(p => p.EventId == scope.EventId);
            }

            using var rebuildContext = _fixture.CreateDbContext();
            var result = await CreateUpdater(rebuildContext)
                .RebuildForTenantAsync(scope.TenantId, batchSize: null, CancellationToken.None);
            await Assert.That(result.LockAcquired).IsTrue();
            await Assert.That(result.RowsFailed).IsEqualTo(0);

            using var verifyRebuild = _fixture.CreateDbContext();
            var rebuilt = await verifyRebuild.EventCustomPropertyProjections
                .AsNoTracking()
                .SingleAsync(p => p.EventId == scope.EventId);

            await AssertProjectionIdentityAsync(refreshed, rebuilt);
            await Assert.That(rebuilt.NormalizedValue).IsEqualTo("premium access");
        }
    }

    [Test]
    public async Task UpdateForDefinitionAsync_WhenDirtyScopeBacklogQuotaExceeded_ThrowsQuotaExceededException()
    {
        using var seedContext = _fixture.CreateDbContext();
        var scope = await SeedEventWithDefinitionAsync(seedContext, PropertyType.Text);
        await SetDirtyScopeQuotaAsync(seedContext, scope.TenantId, quota: 0);

        using var lockerContext = _fixture.CreateDbContext();
        await using var lockerTransaction = await lockerContext.Database.BeginTransactionAsync();
        await AcquireExclusiveProjectionLockAsync(
            lockerContext,
            IEventCustomPropertyProjectionUpdater.ProjectionName,
            scope.TenantId,
            lockerTransaction,
            CancellationToken.None);

        using var updateContext = _fixture.CreateDbContext();
        var updater = CreateUpdater(updateContext);

        var exception = await Assert.ThrowsAsync<QuotaExceededException>(() =>
            updater.UpdateForDefinitionAsync(scope.DefinitionId, CancellationToken.None));

        await Assert.That(exception.Details.QuotaKey)
            .IsEqualTo(CustomPropertyQuotaSettingDefinitions.MaxDirtyScopePendingPerTenant.Key);
        await Assert.That(exception.Details.Limit).IsEqualTo(0);
        await Assert.That(exception.Details.Actual).IsEqualTo(0);
        await Assert.That(exception.Details.Attempted).IsEqualTo(1);
        await Assert.That(exception.Details.Scope).IsEqualTo("event_custom_property_projection_dirty_scope");
        await Assert.That(exception.Details.TenantId).IsEqualTo(scope.TenantId);

        using var verify = _fixture.CreateDbContext();
        var pendingCount = await verify.CustomPropertyProjectionDirtyScopes
            .AsNoTracking()
            .CountAsync(r =>
                r.TenantId == scope.TenantId
                && r.ProjectionName == IEventCustomPropertyProjectionUpdater.ProjectionName
                && r.DrainedAt == null);
        await Assert.That(pendingCount).IsEqualTo(0);
    }

    #region Helpers

    private static EventCustomPropertyProjectionUpdater CreateUpdater(ExploreDbContext context)
    {
        var statusRepo = new CustomPropertyProjectionStatusRepository(context);
        var dirtyScopeRepo = new CustomPropertyProjectionDirtyScopeRepository(context);
        var tenantSettingRepo = new TenantSettingRepository(context);
        var systemSettingRepo = new SystemSettingRepository(
            context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var quotaResolver = new CustomPropertyQuotaResolver(tenantSettingRepo, systemSettingRepo);

        return new EventCustomPropertyProjectionUpdater(
            context,
            dirtyScopeRepo,
            statusRepo,
            quotaResolver,
            new ProjectionMetrics(new TestMeterFactory()));
    }

    private static async Task<ProjectionTestScope> SeedEventWithDefinitionAsync(
        ExploreDbContext context,
        PropertyType propertyType)
    {
        var tenant = new Tenant
        {
            FullName = "Proj Test Tenant",
            Slug = "proj-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = 2,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"proj-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Proj",
                LastName = "Tester",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Proj Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Proj Event",
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
            Key = $"field-{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Field",
            PropertyType = propertyType,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            InstantiatedAt = DateTimeOffset.UtcNow,
        };
        context.EventCustomPropertyDefinitions.Add(definition);
        await context.SaveChangesAsync();

        return new ProjectionTestScope(tenant.Id, @event.Id, definition.Id);
    }

    private static async Task SetDirtyScopeQuotaAsync(ExploreDbContext context, Guid tenantId, int quota)
    {
        var tenant = await context.Tenants.FirstAsync(t => t.Id == tenantId);
        context.TenantSettingOverrides.Add(new TenantSetting
        {
            TenantId = tenantId,
            Tenant = tenant,
            SettingKey = CustomPropertyQuotaSettingDefinitions.MaxDirtyScopePendingPerTenant.Key,
            Value = quota.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IsLocked = false,
        });
        await context.SaveChangesAsync();
    }

    private static async Task AcquireExclusiveProjectionLockAsync(
        ExploreDbContext context,
        string projectionName,
        Guid tenantId,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT pg_try_advisory_xact_lock(@key1, @key2)";

        var key1 = command.CreateParameter();
        key1.ParameterName = "@key1";
        key1.Value = ComputeStableKey(projectionName);
        command.Parameters.Add(key1);

        var key2 = command.CreateParameter();
        key2.ParameterName = "@key2";
        key2.Value = ComputeStableKey(tenantId.ToString("N"));
        command.Parameters.Add(key2);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        await Assert.That(result).IsEqualTo(true);
    }

    private static int ComputeStableKey(string value)
    {
        unchecked
        {
            const int fnvOffsetBasis = unchecked((int)2166136261);
            const int fnvPrime = 16777619;
            var hash = fnvOffsetBasis;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    private static async Task AssertProjectionIdentityAsync(
        EventCustomPropertyProjection expected,
        EventCustomPropertyProjection actual)
    {
        await Assert.That(actual.EventCustomPropertyDefinitionId).IsEqualTo(expected.EventCustomPropertyDefinitionId);
        await Assert.That(actual.EventCustomPropertyValueId).IsEqualTo(expected.EventCustomPropertyValueId);
        await Assert.That(actual.EventId).IsEqualTo(expected.EventId);
        await Assert.That(actual.TenantId).IsEqualTo(expected.TenantId);
        await Assert.That(actual.Namespace).IsEqualTo(expected.Namespace);
        await Assert.That(actual.Key).IsEqualTo(expected.Key);
        await Assert.That(actual.PropertyType).IsEqualTo(expected.PropertyType);
        await Assert.That(actual.ExposureLevel).IsEqualTo(expected.ExposureLevel);
        await Assert.That(actual.IsSearchable).IsEqualTo(expected.IsSearchable);
        await Assert.That(actual.IsFilterable).IsEqualTo(expected.IsFilterable);
        await Assert.That(actual.IsExportable).IsEqualTo(expected.IsExportable);
        await Assert.That(actual.IsModerationRelevant).IsEqualTo(expected.IsModerationRelevant);
        await Assert.That(actual.IsAnalyticsRelevant).IsEqualTo(expected.IsAnalyticsRelevant);
        await Assert.That(actual.Ordinal).IsEqualTo(expected.Ordinal);
        await Assert.That(actual.OptionId).IsEqualTo(expected.OptionId);
        await Assert.That(actual.TextValue).IsEqualTo(expected.TextValue);
        await Assert.That(actual.NumberValue).IsEqualTo(expected.NumberValue);
        await Assert.That(actual.BooleanValue).IsEqualTo(expected.BooleanValue);
        await Assert.That(actual.DateTimeValue).IsEqualTo(expected.DateTimeValue);
        await Assert.That(actual.NormalizedValue).IsEqualTo(expected.NormalizedValue);
    }

    private sealed record ProjectionTestScope(Guid TenantId, Guid EventId, Guid DefinitionId);

    #endregion
}
