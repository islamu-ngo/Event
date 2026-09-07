using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Projections;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TemplateInstantiationPostgreSqlTests(PostgreSqlContainerFixture fixture)
{
    private readonly PostgreSqlContainerFixture _fixture = fixture;

    [Test]
    public async Task EventTemplateInstantiation_OnPostgreSql_PersistsRuntimeDefinitionOptionsDefaultValueAndProjection()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(context, "event-template-runtime");
        var template = CreateEventTemplate(graph.TenantId);
        var templateDefinition = CreateEventTemplateDefinition(graph.TenantId, template.Id);
        var standardOption = CreateEventTemplateOption(templateDefinition.Id, "Standard", isDefault: true, sortOrder: 1);
        var premiumOption = CreateEventTemplateOption(templateDefinition.Id, "Premium", isDefault: false, sortOrder: 2);

        var templateRepository = new EventTemplateRepository(context);
        await templateRepository.CreateWithDefinitions(
            template,
            [new TemplateDefinitionWithOptions(templateDefinition, [standardOption, premiumOption], standardOption.Id)],
            CancellationToken.None);

        var persistedTemplate = await templateRepository.GetTemplateWithDetails(template.Id);
        await Assert.That(persistedTemplate).IsNotNull();

        var result = new EventTemplateInstantiationService().InstantiateFromTemplate(
            graph.EventId,
            graph.TenantId,
            persistedTemplate!,
            graph.UserId.ToString());

        var runtimeRepository = new EventCustomPropertyRepository(context);
        foreach (var runtimeDefinition in result.Definitions)
        {
            runtimeDefinition.Definition.DefaultOptionId = null;
            await runtimeRepository.CreateWithOptions(
                runtimeDefinition.Definition,
                runtimeDefinition.Options,
                runtimeDefinition.DefaultOptionId,
                CancellationToken.None);

            if (runtimeDefinition.DefaultValue is not null)
            {
                await runtimeRepository.SetValue(runtimeDefinition.DefaultValue, CancellationToken.None);
            }
        }

        var updater = CreateEventProjectionUpdater(context);
        await updater.RefreshForEventAsync(graph.EventId, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var runtimeDef = await verify.EventCustomPropertyDefinitions
            .AsNoTracking()
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values)
            .SingleAsync(x => x.EventId == graph.EventId);

        await Assert.That(runtimeDef.TenantId).IsEqualTo(graph.TenantId);
        await Assert.That(runtimeDef.Namespace).IsEqualTo("template.runtime");
        await Assert.That(runtimeDef.Key).IsEqualTo("attendance_tier");
        await Assert.That(runtimeDef.SourceTemplateId).IsEqualTo(template.Id);
        await Assert.That(runtimeDef.SourceTemplateKey).IsEqualTo(template.TemplateKey);
        await Assert.That(runtimeDef.SourceTemplateVersion).IsEqualTo(template.Version);
        await Assert.That(runtimeDef.SourceTemplateDefinitionId).IsEqualTo(templateDefinition.Id);
        await Assert.That(runtimeDef.InstantiatedAt).IsNotEqualTo(default(DateTimeOffset));

        var runtimeOptions = runtimeDef.Options.OrderBy(x => x.SortOrder).ToList();
        await Assert.That(runtimeOptions.Count).IsEqualTo(2);
        var runtimeDefault = runtimeOptions.Single(x => x.Key == "standard");
        await Assert.That(runtimeDefault.Id).IsNotEqualTo(standardOption.Id);
        await Assert.That(runtimeDefault.SourceTemplateOptionId).IsEqualTo(standardOption.Id);
        await Assert.That(runtimeDefault.SourceTemplateVersion).IsEqualTo(template.Version);
        await Assert.That(runtimeDefault.IsDefault).IsTrue();
        await Assert.That(runtimeDef.DefaultOptionId).IsEqualTo(runtimeDefault.Id);
        await Assert.That(runtimeOptions.Single(x => x.Key == "premium").SourceTemplateOptionId).IsEqualTo(premiumOption.Id);

        var value = runtimeDef.Values.Single();
        await Assert.That(value.OptionId).IsEqualTo(runtimeDefault.Id);

        var projection = await verify.EventCustomPropertyProjections
            .AsNoTracking()
            .SingleAsync(x => x.EventCustomPropertyValueId == value.Id);
        await Assert.That(projection.TenantId).IsEqualTo(graph.TenantId);
        await Assert.That(projection.EventId).IsEqualTo(graph.EventId);
        await Assert.That(projection.EventCustomPropertyDefinitionId).IsEqualTo(runtimeDef.Id);
        await Assert.That(projection.OptionId).IsEqualTo(runtimeDefault.Id);
        await Assert.That(projection.Namespace).IsEqualTo("template.runtime");
        await Assert.That(projection.Key).IsEqualTo("attendance_tier");
        await Assert.That(projection.NormalizedValue).IsEqualTo("standard");
    }

    [Test]
    public async Task EventSessionTemplateInstantiation_OnPostgreSql_PersistsRuntimeDefinitionOptionsDefaultValueAndProjection()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateDbContext();
        var graph = await SeedEventGraphAsync(context, "session-template-runtime");
        var session = CreateEventSession(graph.TenantId, graph.EventId);
        await context.EventSessions.AddAsync(session);
        await context.SaveChangesAsync();

        var eventTemplate = CreateEventTemplate(graph.TenantId);
        await context.EventTemplates.AddAsync(eventTemplate);
        await context.SaveChangesAsync();

        var sessionTemplate = CreateEventSessionTemplate(graph.TenantId, eventTemplate.Id);
        var templateDefinition = CreateEventSessionTemplateDefinition(graph.TenantId, sessionTemplate.Id);
        var inPersonOption = CreateEventSessionTemplateOption(templateDefinition.Id, "In Person", isDefault: true, sortOrder: 1);
        var livestreamOption = CreateEventSessionTemplateOption(templateDefinition.Id, "Livestream", isDefault: false, sortOrder: 2);

        var templateRepository = new EventSessionTemplateRepository(context);
        await templateRepository.CreateWithDefinitions(
            sessionTemplate,
            [new SessionTemplateDefinitionWithOptions(templateDefinition, [inPersonOption, livestreamOption], inPersonOption.Id)],
            CancellationToken.None);

        var persistedTemplate = await templateRepository.GetSessionTemplateWithDetails(sessionTemplate.Id);
        await Assert.That(persistedTemplate).IsNotNull();

        var result = new EventSessionTemplateInstantiationService().InstantiateFromSessionTemplate(
            session.Id,
            graph.TenantId,
            persistedTemplate!,
            graph.UserId.ToString());

        var runtimeRepository = new EventSessionCustomPropertyRepository(context);
        foreach (var runtimeDefinition in result.Definitions)
        {
            runtimeDefinition.Definition.DefaultOptionId = null;
            await runtimeRepository.CreateWithOptions(
                runtimeDefinition.Definition,
                runtimeDefinition.Options,
                runtimeDefinition.DefaultOptionId,
                CancellationToken.None);

            if (runtimeDefinition.DefaultValue is not null)
            {
                await runtimeRepository.SetValue(runtimeDefinition.DefaultValue, CancellationToken.None);
            }
        }

        var updater = CreateEventSessionProjectionUpdater(context);
        await updater.RefreshForEventSessionAsync(session.Id, CancellationToken.None);

        await using var verify = _fixture.CreateDbContext();
        var runtimeDef = await verify.EventSessionCustomPropertyDefinitions
            .AsNoTracking()
            .Include(x => x.Options.OrderBy(o => o.SortOrder))
            .Include(x => x.Values)
            .SingleAsync(x => x.EventSessionId == session.Id);

        await Assert.That(runtimeDef.TenantId).IsEqualTo(graph.TenantId);
        await Assert.That(runtimeDef.Namespace).IsEqualTo("session.runtime");
        await Assert.That(runtimeDef.Key).IsEqualTo("delivery_mode");
        await Assert.That(runtimeDef.SourceTemplateId).IsEqualTo(sessionTemplate.Id);
        await Assert.That(runtimeDef.SourceTemplateKey).IsEqualTo(sessionTemplate.SessionTemplateKey);
        await Assert.That(runtimeDef.SourceTemplateVersion).IsEqualTo(sessionTemplate.Version);
        await Assert.That(runtimeDef.SourceTemplateDefinitionId).IsEqualTo(templateDefinition.Id);
        await Assert.That(runtimeDef.InstantiatedAt).IsNotEqualTo(default(DateTimeOffset));

        var runtimeOptions = runtimeDef.Options.OrderBy(x => x.SortOrder).ToList();
        await Assert.That(runtimeOptions.Count).IsEqualTo(2);
        var runtimeDefault = runtimeOptions.Single(x => x.Key == "in_person");
        await Assert.That(runtimeDefault.Id).IsNotEqualTo(inPersonOption.Id);
        await Assert.That(runtimeDefault.SourceTemplateOptionId).IsEqualTo(inPersonOption.Id);
        await Assert.That(runtimeDefault.SourceTemplateVersion).IsEqualTo(sessionTemplate.Version);
        await Assert.That(runtimeDefault.IsDefault).IsTrue();
        await Assert.That(runtimeDef.DefaultOptionId).IsEqualTo(runtimeDefault.Id);
        await Assert.That(runtimeOptions.Single(x => x.Key == "livestream").SourceTemplateOptionId).IsEqualTo(livestreamOption.Id);

        var value = runtimeDef.Values.Single();
        await Assert.That(value.OptionId).IsEqualTo(runtimeDefault.Id);

        var projection = await verify.EventSessionCustomPropertyProjections
            .AsNoTracking()
            .SingleAsync(x => x.EventSessionCustomPropertyValueId == value.Id);
        await Assert.That(projection.TenantId).IsEqualTo(graph.TenantId);
        await Assert.That(projection.EventSessionId).IsEqualTo(session.Id);
        await Assert.That(projection.EventSessionCustomPropertyDefinitionId).IsEqualTo(runtimeDef.Id);
        await Assert.That(projection.OptionId).IsEqualTo(runtimeDefault.Id);
        await Assert.That(projection.Namespace).IsEqualTo("session.runtime");
        await Assert.That(projection.Key).IsEqualTo("delivery_mode");
        await Assert.That(projection.NormalizedValue).IsEqualTo("in-person");
    }

    private static EventCustomPropertyProjectionUpdater CreateEventProjectionUpdater(ExploreDbContext context)
    {
        var statusRepository = new CustomPropertyProjectionStatusRepository(context);
        var dirtyScopeRepository = new CustomPropertyProjectionDirtyScopeRepository(context);
        var tenantSettingRepository = new TenantSettingRepository(context);
        var systemSettingRepository = new SystemSettingRepository(
            context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var quotaResolver = new CustomPropertyQuotaResolver(tenantSettingRepository, systemSettingRepository);

        return new EventCustomPropertyProjectionUpdater(
            context,
            dirtyScopeRepository,
            statusRepository,
            quotaResolver,
            new ProjectionMetrics(new TestMeterFactory()));
    }

    private static EventSessionCustomPropertyProjectionUpdater CreateEventSessionProjectionUpdater(ExploreDbContext context)
    {
        var statusRepository = new CustomPropertyProjectionStatusRepository(context);
        var dirtyScopeRepository = new CustomPropertyProjectionDirtyScopeRepository(context);
        var tenantSettingRepository = new TenantSettingRepository(context);
        var systemSettingRepository = new SystemSettingRepository(
            context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var quotaResolver = new CustomPropertyQuotaResolver(tenantSettingRepository, systemSettingRepository);

        return new EventSessionCustomPropertyProjectionUpdater(
            context,
            dirtyScopeRepository,
            statusRepository,
            quotaResolver,
            new ProjectionMetrics(new TestMeterFactory()));
    }

    private static async Task<EventGraph> SeedEventGraphAsync(ExploreDbContext context, string prefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Template Runtime Tenant {prefix}",
            Slug = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{prefix}-{Guid.NewGuid().ToString("N")[..8]}@example.com",
                FirstName = "Template",
                LastName = "Runtime",
            },
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Template Runtime Actor" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.NewGuid(),
            Title = "Template Runtime Event",
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

        return new EventGraph(tenant.Id, user.Id, @event.Id);
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
            Title = "Template Runtime Session",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(1),
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventTemplate CreateEventTemplate(Guid tenantId)
    {
        return new EventTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Tenant = null!,
            TemplateKey = "template-runtime-event",
            DisplayName = "Template Runtime Event",
            Version = 3,
            IsPublished = true,
            IsActive = true,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventTemplateCustomPropertyDefinition CreateEventTemplateDefinition(Guid tenantId, Guid templateId)
    {
        return new EventTemplateCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventTemplateId = templateId,
            EventTemplate = null!,
            TenantId = tenantId,
            Tenant = null!,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Template Runtime"),
            Key = CustomPropertyIdentity.NormalizeKey("Attendance Tier"),
            DisplayName = "Attendance Tier",
            PropertyType = PropertyType.Option,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventTemplateCustomPropertyOption CreateEventTemplateOption(
        Guid definitionId,
        string key,
        bool isDefault,
        int sortOrder)
    {
        return new EventTemplateCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventTemplateCustomPropertyDefinitionId = definitionId,
            Definition = null!,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Template Runtime"),
            Key = CustomPropertyIdentity.NormalizeKey(key),
            DisplayName = key,
            Value = CustomPropertyIdentity.NormalizeKey(key).Replace('_', '-'),
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventSessionTemplate CreateEventSessionTemplate(Guid tenantId, Guid eventTemplateId)
    {
        return new EventSessionTemplate
        {
            Id = Guid.NewGuid(),
            EventTemplateId = eventTemplateId,
            EventTemplate = null!,
            TenantId = tenantId,
            Tenant = null!,
            SessionTemplateKey = "template-runtime-session",
            DisplayName = "Template Runtime Session",
            Version = 5,
            IsPublished = true,
            IsActive = true,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventSessionTemplateCustomPropertyDefinition CreateEventSessionTemplateDefinition(Guid tenantId, Guid templateId)
    {
        return new EventSessionTemplateCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            EventSessionTemplateId = templateId,
            EventSessionTemplate = null!,
            TenantId = tenantId,
            Tenant = null!,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Session Runtime"),
            Key = CustomPropertyIdentity.NormalizeKey("Delivery Mode"),
            DisplayName = "Delivery Mode",
            PropertyType = PropertyType.Option,
            IsActive = true,
            SortOrder = 1,
            ExposureLevel = ExposureLevel.Public,
            IsSearchable = true,
            IsFilterable = true,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private static EventSessionTemplateCustomPropertyOption CreateEventSessionTemplateOption(
        Guid definitionId,
        string key,
        bool isDefault,
        int sortOrder)
    {
        return new EventSessionTemplateCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventSessionTemplateCustomPropertyDefinitionId = definitionId,
            Definition = null!,
            Namespace = CustomPropertyIdentity.NormalizeNamespace("Session Runtime"),
            Key = CustomPropertyIdentity.NormalizeKey(key),
            DisplayName = key,
            Value = CustomPropertyIdentity.NormalizeKey(key).Replace('_', '-'),
            IsDefault = isDefault,
            IsActive = true,
            SortOrder = sortOrder,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }

    private sealed record EventGraph(Guid TenantId, Guid UserId, Guid EventId);
}
