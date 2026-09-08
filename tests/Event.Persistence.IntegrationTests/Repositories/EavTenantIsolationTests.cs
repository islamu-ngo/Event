using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<ProjectionTestContainerFixture>(Shared = SharedType.PerAssembly)]
public class EavTenantIsolationTests(ProjectionTestContainerFixture fixture)
{
    [Test]
    public async Task EventCustomPropertyRows_WhenTenantContextIsSet_AreIsolatedByTenantFilter()
    {
        using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedEventCustomPropertyGraphAsync(seedContext, "tenant-a", "A value");
        var tenantB = await SeedEventCustomPropertyGraphAsync(seedContext, "tenant-b", "B value");

        using var tenantAContext = fixture.CreateDbContext(new TestTenantContext(tenantA.TenantId));

        var tenantADefinitions = await tenantAContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Where(d => d.Id == tenantA.DefinitionId || d.Id == tenantB.DefinitionId)
            .Select(d => d.Id)
            .ToListAsync();
        var tenantAValues = await tenantAContext.EventCustomPropertyValues
            .AsNoTracking()
            .Where(v => v.Id == tenantA.ValueId || v.Id == tenantB.ValueId)
            .Select(v => v.Id)
            .ToListAsync();
        var tenantAProjections = await tenantAContext.EventCustomPropertyProjections
            .AsNoTracking()
            .Where(p => p.Id == tenantA.ProjectionId || p.Id == tenantB.ProjectionId)
            .Select(p => p.Id)
            .ToListAsync();

        await Assert.That(tenantADefinitions).IsEquivalentTo([tenantA.DefinitionId]);
        await Assert.That(tenantAValues).IsEquivalentTo([tenantA.ValueId]);
        await Assert.That(tenantAProjections).IsEquivalentTo([tenantA.ProjectionId]);

        using var tenantBContext = fixture.CreateDbContext(new TestTenantContext(tenantB.TenantId));
        var tenantBProjectionCount = await tenantBContext.EventCustomPropertyProjections
            .AsNoTracking()
            .CountAsync(p => p.Id == tenantA.ProjectionId || p.Id == tenantB.ProjectionId);

        await Assert.That(tenantBProjectionCount).IsEqualTo(1);
    }

    [Test]
    public async Task EventCustomPropertyDefinitions_WhenIncludingDeleted_KeepTenantFilterActive()
    {
        using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedEventCustomPropertyGraphAsync(seedContext, "deleted-tenant-a", "A deleted");
        var tenantB = await SeedEventCustomPropertyGraphAsync(seedContext, "deleted-tenant-b", "B deleted");

        var tenantADefinition = await seedContext.EventCustomPropertyDefinitions
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .FirstAsync(d => d.Id == tenantA.DefinitionId);
        tenantADefinition.IsDeleted = true;
        tenantADefinition.DeletedAt = DateTime.UtcNow;

        var tenantBDefinition = await seedContext.EventCustomPropertyDefinitions
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .FirstAsync(d => d.Id == tenantB.DefinitionId);
        tenantBDefinition.IsDeleted = true;
        tenantBDefinition.DeletedAt = DateTime.UtcNow;
        await seedContext.SaveChangesAsync();

        using var tenantAContext = fixture.CreateDbContext(new TestTenantContext(tenantA.TenantId));

        var normalCount = await tenantAContext.EventCustomPropertyDefinitions
            .AsNoTracking()
            .CountAsync(d => d.Id == tenantA.DefinitionId || d.Id == tenantB.DefinitionId);
        var includeDeletedIds = await tenantAContext.EventCustomPropertyDefinitions
            .IncludeDeleted()
            .AsNoTracking()
            .Where(d => d.Id == tenantA.DefinitionId || d.Id == tenantB.DefinitionId)
            .Select(d => d.Id)
            .ToListAsync();
        var tenantFilterDisabledCount = await tenantAContext.EventCustomPropertyDefinitions
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .CountAsync(d => d.Id == tenantA.DefinitionId || d.Id == tenantB.DefinitionId);
        var allFiltersDisabledCount = await tenantAContext.EventCustomPropertyDefinitions
            .IgnoreAllFilters(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .AsNoTracking()
            .CountAsync(d => d.Id == tenantA.DefinitionId || d.Id == tenantB.DefinitionId);

        await Assert.That(normalCount).IsEqualTo(0);
        await Assert.That(includeDeletedIds).IsEquivalentTo([tenantA.DefinitionId]);
        await Assert.That(tenantFilterDisabledCount).IsEqualTo(0);
        await Assert.That(allFiltersDisabledCount).IsEqualTo(2);
    }

    private static async Task<EavTenantScope> SeedEventCustomPropertyGraphAsync(
        ExploreDbContext context,
        string slugPrefix,
        string textValue)
    {
        var tenant = new Tenant
        {
            FullName = $"EAV Tenant {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Eav",
                LastName = "Tester",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = $"Actor {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = $"EAV Isolation {slugPrefix}",
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
            Namespace = "tenant.certification",
            Key = $"field-{Guid.NewGuid().ToString("N")[..6]}",
            DisplayName = "Certification Field",
            PropertyType = PropertyType.Text,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            InstantiatedAt = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        context.EventCustomPropertyDefinitions.Add(definition);
        await context.SaveChangesAsync();

        var value = new EventCustomPropertyValue
        {
            EventCustomPropertyDefinitionId = definition.Id,
            EventId = @event.Id,
            TenantId = tenant.Id,
            TextValue = textValue,
            Ordinal = 0,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        context.EventCustomPropertyValues.Add(value);
        await context.SaveChangesAsync();

        var projection = new EventCustomPropertyProjection
        {
            EventCustomPropertyDefinitionId = definition.Id,
            EventCustomPropertyValueId = value.Id,
            EventId = @event.Id,
            TenantId = tenant.Id,
            Namespace = definition.Namespace,
            Key = definition.Key,
            PropertyType = definition.PropertyType,
            ExposureLevel = definition.ExposureLevel,
            IsSearchable = definition.IsSearchable,
            IsFilterable = definition.IsFilterable,
            IsExportable = definition.IsExportable,
            IsModerationRelevant = definition.IsModerationRelevant,
            IsAnalyticsRelevant = definition.IsAnalyticsRelevant,
            Ordinal = value.Ordinal,
            TextValue = value.TextValue,
            NormalizedValue = value.TextValue.ToLowerInvariant(),
            UpdatedAt = DateTime.UtcNow,
        };
        context.EventCustomPropertyProjections.Add(projection);
        await context.SaveChangesAsync();

        return new EavTenantScope(tenant.Id, definition.Id, value.Id, projection.Id);
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record EavTenantScope(Guid TenantId, Guid DefinitionId, Guid ValueId, Guid ProjectionId);
}
