using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class CustomPropertyOptionLifecyclePostgreSqlTests(PostgreSqlContainerFixture fixture)
{
    private readonly PostgreSqlContainerFixture _fixture = fixture;

    [Test]
    public async Task SharedUpdateWithOptions_OnPostgreSql_PreservesIdentityRevivesRetiresReordersAndRemapsDefault()
    {
        await _fixture.ResetAsync();

        await using var seedContext = _fixture.CreateDbContext();
        var tenant = CreateTenant();
        await seedContext.Tenants.AddAsync(tenant);
        await seedContext.SaveChangesAsync();

        var definition = CreateSharedDefinition(tenant.Id);
        var preserved = CreateSharedOption(definition.Id, "format", isDefault: false, sortOrder: 20);
        var revived = CreateSharedOption(definition.Id, "vip_access", isDefault: false, sortOrder: 30);
        revived.IsActive = false;
        revived.IsDeleted = true;
        revived.DeletedAt = DateTime.UtcNow.AddMinutes(-5);
        revived.DeletedBy = Guid.NewGuid();
        var omitted = CreateSharedOption(definition.Id, "legacy", isDefault: true, sortOrder: 10);

        await seedContext.CustomPropertyDefinitions.AddAsync(definition);
        await seedContext.SaveChangesAsync();
        await seedContext.CustomPropertyOptions.AddRangeAsync(preserved, revived, omitted);
        await seedContext.SaveChangesAsync();
        definition.DefaultOptionId = omitted.Id;
        await seedContext.SaveChangesAsync();

        await using var updateContext = _fixture.CreateDbContext();
        var repository = new CustomPropertyDefinitionRepository(updateContext);
        var trackedDefinition = await updateContext.CustomPropertyDefinitions
            .SingleAsync(x => x.Id == definition.Id);

        var updatedPreserved = CreateSharedOption(
            definition.Id,
            CustomPropertyIdentity.NormalizeKey("Format"),
            isDefault: false,
            sortOrder: 2);
        updatedPreserved.Namespace = CustomPropertyIdentity.NormalizeNamespace("Event");
        updatedPreserved.DisplayName = "Renamed Format";
        updatedPreserved.Value = "format-renamed";

        var updatedRevived = CreateSharedOption(
            definition.Id,
            CustomPropertyIdentity.NormalizeKey("VIP Access"),
            isDefault: true,
            sortOrder: 1);
        updatedRevived.Namespace = CustomPropertyIdentity.NormalizeNamespace("Event");
        updatedRevived.DisplayName = "VIP Access";
        updatedRevived.Value = "vip-access";

        await repository.UpdateWithOptions(
            trackedDefinition,
            [updatedRevived, updatedPreserved],
            updatedRevived.Id,
            CancellationToken.None);

        await using var verifyContext = _fixture.CreateDbContext();
        var persistedDefinition = await verifyContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var options = await verifyContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == definition.Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        var persistedPreserved = options.Single(x => x.Key == "format");
        var persistedRevived = options.Single(x => x.Key == "vip_access");
        var persistedOmitted = options.Single(x => x.Key == "legacy");

        await Assert.That(persistedPreserved.Id).IsEqualTo(preserved.Id);
        await Assert.That(persistedPreserved.DisplayName).IsEqualTo("Renamed Format");
        await Assert.That(persistedPreserved.Value).IsEqualTo("format-renamed");
        await Assert.That(persistedPreserved.SortOrder).IsEqualTo(2);
        await Assert.That(persistedPreserved.IsActive).IsTrue();
        await Assert.That(persistedPreserved.IsDefault).IsFalse();

        await Assert.That(persistedRevived.Id).IsEqualTo(revived.Id);
        await Assert.That(persistedRevived.DisplayName).IsEqualTo("VIP Access");
        await Assert.That(persistedRevived.Value).IsEqualTo("vip-access");
        await Assert.That(persistedRevived.SortOrder).IsEqualTo(1);
        await Assert.That(persistedRevived.IsActive).IsTrue();
        await Assert.That(persistedRevived.IsDefault).IsTrue();
        await Assert.That(persistedRevived.IsDeleted).IsFalse();
        await Assert.That(persistedRevived.DeletedAt).IsNull();
        await Assert.That(persistedRevived.DeletedBy).IsNull();

        await Assert.That(persistedOmitted.Id).IsEqualTo(omitted.Id);
        await Assert.That(persistedOmitted.IsActive).IsFalse();
        await Assert.That(persistedOmitted.IsDefault).IsFalse();
        await Assert.That(persistedOmitted.IsDeleted).IsFalse();

        await Assert.That(persistedDefinition.DefaultOptionId).IsEqualTo(revived.Id);
        await Assert.That(options.Select(x => x.Id)).IsEquivalentTo([revived.Id, preserved.Id, omitted.Id]);
    }

    [Test]
    public async Task SharedUpdateWithOptions_OnPostgreSql_RotatesConcurrencyStamp()
    {
        await _fixture.ResetAsync();

        Guid definitionId;
        Guid optionId;
        Guid originalStamp;

        await using (var seedContext = _fixture.CreateDbContext())
        {
            var tenant = CreateTenant();
            await seedContext.Tenants.AddAsync(tenant);
            await seedContext.SaveChangesAsync();

            var definition = CreateSharedDefinition(tenant.Id);
            var option = CreateSharedOption(definition.Id, "format", isDefault: true, sortOrder: 1);

            await seedContext.CustomPropertyDefinitions.AddAsync(definition);
            await seedContext.SaveChangesAsync();
            await seedContext.CustomPropertyOptions.AddAsync(option);
            await seedContext.SaveChangesAsync();
            definition.DefaultOptionId = option.Id;
            await seedContext.SaveChangesAsync();

            definitionId = definition.Id;
            optionId = option.Id;
            originalStamp = definition.ConcurrencyStamp;
        }

        await using (var updateContext = _fixture.CreateDbContext())
        {
            var repository = new CustomPropertyDefinitionRepository(updateContext);
            var trackedDefinition = await updateContext.CustomPropertyDefinitions
                .SingleAsync(x => x.Id == definitionId);
            trackedDefinition.DisplayName = "Updated Format";

            var updatedOption = CreateSharedOption(definitionId, "format", isDefault: true, sortOrder: 1);
            updatedOption.DisplayName = "Updated Format";
            updatedOption.Value = "updated-format";

            await repository.UpdateWithOptions(
                trackedDefinition,
                [updatedOption],
                updatedOption.Id,
                CancellationToken.None);
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var persistedDefinition = await verifyContext.CustomPropertyDefinitions
            .SingleAsync(x => x.Id == definitionId);
        var persistedOption = await verifyContext.CustomPropertyOptions
            .SingleAsync(x => x.CustomPropertyDefinitionId == definitionId && x.Key == "format");

        await Assert.That(persistedDefinition.DisplayName).IsEqualTo("Updated Format");
        await Assert.That(persistedDefinition.DefaultOptionId).IsEqualTo(optionId);
        await Assert.That(persistedDefinition.ConcurrencyStamp).IsNotEqualTo(originalStamp);
        await Assert.That(persistedOption.Id).IsEqualTo(optionId);
        await Assert.That(persistedOption.Value).IsEqualTo("updated-format");
    }

    [Test]
    public async Task SharedPurgeDefinition_OnPostgreSql_PhysicallyDeletesDependencyFreeDefinitionAndOptions()
    {
        await _fixture.ResetAsync();

        Guid definitionId;
        Guid optionId;

        await using (var seedContext = _fixture.CreateDbContext())
        {
            var tenant = CreateTenant();
            await seedContext.Tenants.AddAsync(tenant);
            await seedContext.SaveChangesAsync();

            var definition = CreateSharedDefinition(tenant.Id);
            var option = CreateSharedOption(definition.Id, "format", isDefault: true, sortOrder: 1);

            await seedContext.CustomPropertyDefinitions.AddAsync(definition);
            await seedContext.SaveChangesAsync();
            await seedContext.CustomPropertyOptions.AddAsync(option);
            await seedContext.SaveChangesAsync();
            definition.DefaultOptionId = option.Id;
            await seedContext.SaveChangesAsync();

            definitionId = definition.Id;
            optionId = option.Id;
        }

        await using (var purgeContext = _fixture.CreateDbContext())
        {
            var repository = new CustomPropertyDefinitionRepository(purgeContext);
            var summary = await repository.GetPurgeDependencies(definitionId, CancellationToken.None);

            await Assert.That(summary).IsNotNull();
            await Assert.That(summary!.HasBlockingDependencies).IsFalse();
            await Assert.That(summary.OptionCount).IsEqualTo(1);

            var purged = await repository.PurgeDefinition(definitionId, CancellationToken.None);

            await Assert.That(purged).IsTrue();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var definitionExists = await verifyContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == definitionId);
        var optionExists = await verifyContext.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == optionId);

        await Assert.That(definitionExists).IsFalse();
        await Assert.That(optionExists).IsFalse();
    }

    [Test]
    public async Task SharedPurgeDefinition_OnPostgreSql_ReturnsFalseWhenValuesExistAndKeepsRows()
    {
        await _fixture.ResetAsync();

        Guid definitionId;
        Guid valueId;

        await using (var seedContext = _fixture.CreateDbContext())
        {
            var tenant = CreateTenant();
            await seedContext.Tenants.AddAsync(tenant);
            await seedContext.SaveChangesAsync();

            var definition = CreateSharedDefinition(tenant.Id);
            var value = new CustomPropertyValue
            {
                Id = Guid.NewGuid(),
                CustomPropertyDefinitionId = definition.Id,
                EntityId = Guid.NewGuid(),
                TenantId = tenant.Id,
                Ordinal = 0,
                TextValue = "historical",
                ConcurrencyStamp = Guid.NewGuid(),
            };

            await seedContext.CustomPropertyDefinitions.AddAsync(definition);
            await seedContext.SaveChangesAsync();
            await seedContext.CustomPropertyValues.AddAsync(value);
            await seedContext.SaveChangesAsync();

            definitionId = definition.Id;
            valueId = value.Id;
        }

        await using (var purgeContext = _fixture.CreateDbContext())
        {
            var repository = new CustomPropertyDefinitionRepository(purgeContext);
            var purged = await repository.PurgeDefinition(definitionId, CancellationToken.None);

            await Assert.That(purged).IsFalse();
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var definitionExists = await verifyContext.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == definitionId);
        var valueExists = await verifyContext.CustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == valueId);

        await Assert.That(definitionExists).IsTrue();
        await Assert.That(valueExists).IsTrue();
    }

    [Test]
    public async Task EventPurgeDependencies_OnPostgreSql_BlockWhenValuesAndProjectionsExist()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(context);
        var definition = CreateEventDefinition(graph.TenantId, graph.EventId);
        var option = CreateEventOption(definition.Id, "format", isDefault: true, sortOrder: 1);

        await context.EventCustomPropertyDefinitions.AddAsync(definition);
        await context.SaveChangesAsync();
        await context.EventCustomPropertyOptions.AddAsync(option);
        await context.SaveChangesAsync();
        definition.DefaultOptionId = option.Id;
        await context.SaveChangesAsync();

        var value = new EventCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definition.Id,
            EventId = graph.EventId,
            TenantId = graph.TenantId,
            Ordinal = 0,
            OptionId = option.Id,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        await context.EventCustomPropertyValues.AddAsync(value);
        await context.SaveChangesAsync();
        await context.EventCustomPropertyProjections.AddAsync(new EventCustomPropertyProjection
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definition.Id,
            EventCustomPropertyValueId = value.Id,
            EventId = graph.EventId,
            TenantId = graph.TenantId,
            Namespace = definition.Namespace,
            Key = definition.Key,
            PropertyType = definition.PropertyType,
            ExposureLevel = definition.ExposureLevel,
            IsSearchable = definition.IsSearchable,
            IsFilterable = definition.IsFilterable,
            IsExportable = definition.IsExportable,
            IsModerationRelevant = definition.IsModerationRelevant,
            IsAnalyticsRelevant = definition.IsAnalyticsRelevant,
            Ordinal = 0,
            OptionId = option.Id,
            NormalizedValue = option.Value,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var repository = new EventCustomPropertyRepository(context);
        var summary = await repository.GetPurgeDependencies(definition.Id, CancellationToken.None);

        await Assert.That(summary).IsNotNull();
        await Assert.That(summary!.HasBlockingDependencies).IsTrue();
        await Assert.That(summary.ValueCount).IsEqualTo(1);
        await Assert.That(summary.ProjectionCount).IsEqualTo(1);

        var purged = await repository.PurgeDefinition(definition.Id, CancellationToken.None);

        await Assert.That(purged).IsFalse();
        await Assert.That(await context.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == definition.Id)).IsTrue();
        await Assert.That(await context.EventCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == value.Id)).IsTrue();
        await Assert.That(await context.EventCustomPropertyProjections
            .AnyAsync(x => x.EventCustomPropertyDefinitionId == definition.Id)).IsTrue();
    }

    [Test]
    public async Task EventSessionPurgeDependencies_OnPostgreSql_BlockWhenValuesAndProjectionsExist()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(context);
        var session = CreateEventSession(graph.TenantId, graph.EventId);
        await context.EventSessions.AddAsync(session);
        await context.SaveChangesAsync();
        var definition = CreateEventSessionDefinition(graph.TenantId, session.Id);
        var option = CreateEventSessionOption(definition.Id, "format", isDefault: true, sortOrder: 1);

        await context.EventSessionCustomPropertyDefinitions.AddAsync(definition);
        await context.SaveChangesAsync();
        await context.EventSessionCustomPropertyOptions.AddAsync(option);
        await context.SaveChangesAsync();
        definition.DefaultOptionId = option.Id;
        await context.SaveChangesAsync();

        var value = new EventSessionCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definition.Id,
            EventSessionId = session.Id,
            TenantId = graph.TenantId,
            Ordinal = 0,
            OptionId = option.Id,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        await context.EventSessionCustomPropertyValues.AddAsync(value);
        await context.SaveChangesAsync();
        await context.EventSessionCustomPropertyProjections.AddAsync(new EventSessionCustomPropertyProjection
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definition.Id,
            EventSessionCustomPropertyValueId = value.Id,
            EventSessionId = session.Id,
            TenantId = graph.TenantId,
            Namespace = definition.Namespace,
            Key = definition.Key,
            PropertyType = definition.PropertyType,
            ExposureLevel = definition.ExposureLevel,
            IsSearchable = definition.IsSearchable,
            IsFilterable = definition.IsFilterable,
            IsExportable = definition.IsExportable,
            IsModerationRelevant = definition.IsModerationRelevant,
            IsAnalyticsRelevant = definition.IsAnalyticsRelevant,
            Ordinal = 0,
            OptionId = option.Id,
            NormalizedValue = option.Value,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var repository = new EventSessionCustomPropertyRepository(context);
        var summary = await repository.GetPurgeDependencies(definition.Id, CancellationToken.None);

        await Assert.That(summary).IsNotNull();
        await Assert.That(summary!.HasBlockingDependencies).IsTrue();
        await Assert.That(summary.ValueCount).IsEqualTo(1);
        await Assert.That(summary.ProjectionCount).IsEqualTo(1);

        var purged = await repository.PurgeDefinition(definition.Id, CancellationToken.None);

        await Assert.That(purged).IsFalse();
        await Assert.That(await context.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == definition.Id)).IsTrue();
        await Assert.That(await context.EventSessionCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AnyAsync(x => x.Id == value.Id)).IsTrue();
        await Assert.That(await context.EventSessionCustomPropertyProjections
            .AnyAsync(x => x.EventSessionCustomPropertyDefinitionId == definition.Id)).IsTrue();
    }

    [Test]
    public async Task EventUpdateWithOptions_OnPostgreSql_PreservesIdentityRevivesRetiresReordersAndRemapsDefault()
    {
        await _fixture.ResetAsync();

        await using var seedContext = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(seedContext);
        var definition = CreateEventDefinition(graph.TenantId, graph.EventId);
        var preserved = CreateEventOption(definition.Id, "format", isDefault: false, sortOrder: 20);
        var revived = CreateEventOption(definition.Id, "vip_access", isDefault: false, sortOrder: 30);
        revived.IsActive = false;
        revived.IsDeleted = true;
        revived.DeletedAt = DateTime.UtcNow.AddMinutes(-5);
        revived.DeletedBy = Guid.NewGuid();
        var omitted = CreateEventOption(definition.Id, "legacy", isDefault: true, sortOrder: 10);

        await seedContext.EventCustomPropertyDefinitions.AddAsync(definition);
        await seedContext.SaveChangesAsync();
        await seedContext.EventCustomPropertyOptions.AddRangeAsync(preserved, revived, omitted);
        await seedContext.SaveChangesAsync();
        definition.DefaultOptionId = omitted.Id;
        await seedContext.SaveChangesAsync();

        await using var updateContext = _fixture.CreateDbContext();
        var repository = new EventCustomPropertyRepository(updateContext);
        var trackedDefinition = await updateContext.EventCustomPropertyDefinitions
            .SingleAsync(x => x.Id == definition.Id);
        var updatedPreserved = CreateEventOption(definition.Id, CustomPropertyIdentity.NormalizeKey("Format"), false, 2);
        updatedPreserved.DisplayName = "Renamed Format";
        updatedPreserved.Value = "format-renamed";
        var updatedRevived = CreateEventOption(definition.Id, CustomPropertyIdentity.NormalizeKey("VIP Access"), true, 1);
        updatedRevived.DisplayName = "VIP Access";
        updatedRevived.Value = "vip-access";

        await repository.UpdateWithOptions(
            trackedDefinition,
            [updatedRevived, updatedPreserved],
            updatedRevived.Id,
            CancellationToken.None);

        await using var verifyContext = _fixture.CreateDbContext();
        var persistedDefinition = await verifyContext.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var options = await verifyContext.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == definition.Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        await AssertEventOptionLifecycleAsync(
            options,
            persistedDefinition.DefaultOptionId,
            preserved.Id,
            revived.Id,
            omitted.Id);
    }

    [Test]
    public async Task EventSessionUpdateWithOptions_OnPostgreSql_PreservesIdentityRevivesRetiresReordersAndRemapsDefault()
    {
        await _fixture.ResetAsync();

        await using var seedContext = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(seedContext);
        var session = CreateEventSession(graph.TenantId, graph.EventId);
        await seedContext.EventSessions.AddAsync(session);
        await seedContext.SaveChangesAsync();
        var definition = CreateEventSessionDefinition(graph.TenantId, session.Id);
        var preserved = CreateEventSessionOption(definition.Id, "format", isDefault: false, sortOrder: 20);
        var revived = CreateEventSessionOption(definition.Id, "vip_access", isDefault: false, sortOrder: 30);
        revived.IsActive = false;
        revived.IsDeleted = true;
        revived.DeletedAt = DateTime.UtcNow.AddMinutes(-5);
        revived.DeletedBy = Guid.NewGuid();
        var omitted = CreateEventSessionOption(definition.Id, "legacy", isDefault: true, sortOrder: 10);

        await seedContext.EventSessionCustomPropertyDefinitions.AddAsync(definition);
        await seedContext.SaveChangesAsync();
        await seedContext.EventSessionCustomPropertyOptions.AddRangeAsync(preserved, revived, omitted);
        await seedContext.SaveChangesAsync();
        definition.DefaultOptionId = omitted.Id;
        await seedContext.SaveChangesAsync();

        await using var updateContext = _fixture.CreateDbContext();
        var repository = new EventSessionCustomPropertyRepository(updateContext);
        var trackedDefinition = await updateContext.EventSessionCustomPropertyDefinitions
            .SingleAsync(x => x.Id == definition.Id);
        var updatedPreserved = CreateEventSessionOption(definition.Id, CustomPropertyIdentity.NormalizeKey("Format"), false, 2);
        updatedPreserved.DisplayName = "Renamed Format";
        updatedPreserved.Value = "format-renamed";
        var updatedRevived = CreateEventSessionOption(definition.Id, CustomPropertyIdentity.NormalizeKey("VIP Access"), true, 1);
        updatedRevived.DisplayName = "VIP Access";
        updatedRevived.Value = "vip-access";

        await repository.UpdateWithOptions(
            trackedDefinition,
            [updatedRevived, updatedPreserved],
            updatedRevived.Id,
            CancellationToken.None);

        await using var verifyContext = _fixture.CreateDbContext();
        var persistedDefinition = await verifyContext.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var options = await verifyContext.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == definition.Id)
            .OrderBy(x => x.SortOrder)
            .ToListAsync();

        await AssertEventSessionOptionLifecycleAsync(
            options,
            persistedDefinition.DefaultOptionId,
            preserved.Id,
            revived.Id,
            omitted.Id);
    }

    private static Tenant CreateTenant()
    {
        return new Tenant
        {
            FullName = "Option Lifecycle Tenant",
            Slug = "option-life-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static CustomPropertyDefinition CreateSharedDefinition(Guid tenantId)
    {
        return new CustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            EntityTypeName = EntityTypeName.Organization,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Event"),
            Key = CustomPropertyIdentity.NormalizeKey("Format"),
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static CustomPropertyOption CreateSharedOption(
        Guid definitionId,
        string key,
        bool isDefault,
        int sortOrder)
    {
        return new CustomPropertyOption
        {
            Id = Guid.NewGuid(),
            CustomPropertyDefinitionId = definitionId,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Event"),
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static async Task<EventGraph> SeedEventGraphAsync(ExploreDbContext context)
    {
        var tenant = CreateTenant();
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"option-life-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Option",
                LastName = "Tester",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Option Lifecycle Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Option Lifecycle Event",
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
        await context.SaveChangesAsync();

        return new EventGraph(tenant.Id, @event.Id);
    }

    private static EventSession CreateEventSession(Guid tenantId, Guid eventId)
    {
        return new EventSession
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Event = null!,
            TenantId = tenantId,
            Tenant = null!,
            Title = "Option Lifecycle Session",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventCustomPropertyDefinition CreateEventDefinition(Guid tenantId, Guid eventId)
    {
        return new EventCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventId = eventId,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Event"),
            Key = CustomPropertyIdentity.NormalizeKey("Format"),
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
            InstantiatedAt = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventSessionCustomPropertyDefinition CreateEventSessionDefinition(Guid tenantId, Guid eventSessionId)
    {
        return new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventSessionId = eventSessionId,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Session"),
            Key = CustomPropertyIdentity.NormalizeKey("Format"),
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
            InstantiatedAt = DateTimeOffset.UtcNow,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventCustomPropertyOption CreateEventOption(
        Guid definitionId,
        string key,
        bool isDefault,
        int sortOrder)
    {
        return new EventCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definitionId,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Event"),
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventSessionCustomPropertyOption CreateEventSessionOption(
        Guid definitionId,
        string key,
        bool isDefault,
        int sortOrder)
    {
        return new EventSessionCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definitionId,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Session"),
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static async Task AssertEventOptionLifecycleAsync(
        IReadOnlyCollection<EventCustomPropertyOption> options,
        Guid? defaultOptionId,
        Guid preservedId,
        Guid revivedId,
        Guid omittedId)
    {
        var persistedPreserved = options.Single(x => x.Key == "format");
        var persistedRevived = options.Single(x => x.Key == "vip_access");
        var persistedOmitted = options.Single(x => x.Key == "legacy");

        await Assert.That(persistedPreserved.Id).IsEqualTo(preservedId);
        await Assert.That(persistedPreserved.DisplayName).IsEqualTo("Renamed Format");
        await Assert.That(persistedPreserved.Value).IsEqualTo("format-renamed");
        await Assert.That(persistedPreserved.SortOrder).IsEqualTo(2);
        await Assert.That(persistedPreserved.IsActive).IsTrue();
        await Assert.That(persistedPreserved.IsDefault).IsFalse();

        await Assert.That(persistedRevived.Id).IsEqualTo(revivedId);
        await Assert.That(persistedRevived.DisplayName).IsEqualTo("VIP Access");
        await Assert.That(persistedRevived.Value).IsEqualTo("vip-access");
        await Assert.That(persistedRevived.SortOrder).IsEqualTo(1);
        await Assert.That(persistedRevived.IsActive).IsTrue();
        await Assert.That(persistedRevived.IsDefault).IsTrue();
        await Assert.That(persistedRevived.IsDeleted).IsFalse();
        await Assert.That(persistedRevived.DeletedAt).IsNull();
        await Assert.That(persistedRevived.DeletedBy).IsNull();

        await Assert.That(persistedOmitted.Id).IsEqualTo(omittedId);
        await Assert.That(persistedOmitted.IsActive).IsFalse();
        await Assert.That(persistedOmitted.IsDefault).IsFalse();
        await Assert.That(persistedOmitted.IsDeleted).IsFalse();
        await Assert.That(defaultOptionId).IsEqualTo(revivedId);
        await Assert.That(options.Select(x => x.Id)).IsEquivalentTo([revivedId, preservedId, omittedId]);
    }

    private static async Task AssertEventSessionOptionLifecycleAsync(
        IReadOnlyCollection<EventSessionCustomPropertyOption> options,
        Guid? defaultOptionId,
        Guid preservedId,
        Guid revivedId,
        Guid omittedId)
    {
        var persistedPreserved = options.Single(x => x.Key == "format");
        var persistedRevived = options.Single(x => x.Key == "vip_access");
        var persistedOmitted = options.Single(x => x.Key == "legacy");

        await Assert.That(persistedPreserved.Id).IsEqualTo(preservedId);
        await Assert.That(persistedPreserved.DisplayName).IsEqualTo("Renamed Format");
        await Assert.That(persistedPreserved.Value).IsEqualTo("format-renamed");
        await Assert.That(persistedPreserved.SortOrder).IsEqualTo(2);
        await Assert.That(persistedPreserved.IsActive).IsTrue();
        await Assert.That(persistedPreserved.IsDefault).IsFalse();

        await Assert.That(persistedRevived.Id).IsEqualTo(revivedId);
        await Assert.That(persistedRevived.DisplayName).IsEqualTo("VIP Access");
        await Assert.That(persistedRevived.Value).IsEqualTo("vip-access");
        await Assert.That(persistedRevived.SortOrder).IsEqualTo(1);
        await Assert.That(persistedRevived.IsActive).IsTrue();
        await Assert.That(persistedRevived.IsDefault).IsTrue();
        await Assert.That(persistedRevived.IsDeleted).IsFalse();
        await Assert.That(persistedRevived.DeletedAt).IsNull();
        await Assert.That(persistedRevived.DeletedBy).IsNull();

        await Assert.That(persistedOmitted.Id).IsEqualTo(omittedId);
        await Assert.That(persistedOmitted.IsActive).IsFalse();
        await Assert.That(persistedOmitted.IsDefault).IsFalse();
        await Assert.That(persistedOmitted.IsDeleted).IsFalse();
        await Assert.That(defaultOptionId).IsEqualTo(revivedId);
        await Assert.That(options.Select(x => x.Id)).IsEquivalentTo([revivedId, preservedId, omittedId]);
    }

    private sealed record EventGraph(Guid TenantId, Guid EventId);
}
