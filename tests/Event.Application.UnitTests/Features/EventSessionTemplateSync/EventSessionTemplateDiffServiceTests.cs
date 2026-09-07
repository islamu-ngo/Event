using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessionTemplateSync;

public class EventSessionTemplateDiffServiceTests
{
    [Test]
    public async Task ComputeDiffAsync_WhenSessionTemplateAddsDefinition_ReturnsAddedDefinition()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var templateRepository = Substitute.For<IEventSessionTemplateRepository>();
        var sessionRepository = Substitute.For<IEventSessionRepository>();
        var runtimeRepository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var service = new EventSessionTemplateDiffService(templateRepository, sessionRepository, runtimeRepository);

        var session = new EventSession { Id = sessionId, Event = null!, Tenant = null!, TenantId = tenantId, SourceTemplateId = Guid.NewGuid(), SourceTemplateKey = "session-template", SourceTemplateVersion = 1 };
        var templateDefinition = new EventSessionTemplateCustomPropertyDefinition { Id = Guid.NewGuid(), EventSessionTemplateId = Guid.NewGuid(), TenantId = tenantId, Namespace = "tenant.sync", Key = "session_field", DisplayName = "Session Field", PropertyType = PropertyType.Text, IsActive = true, ExposureLevel = ExposureLevel.Public };
        var template = new EventSessionTemplate { Id = session.SourceTemplateId.Value, EventTemplateId = Guid.NewGuid(), TenantId = tenantId, SessionTemplateKey = "session-template", DisplayName = "Session Template", Version = 2, IsPublished = true, IsActive = true };
        SetSessionTemplateDefinitions(template, [templateDefinition]);

        sessionRepository.GetById(sessionId).Returns(session);
        runtimeRepository.GetAllDefinitionsForSession(sessionId).Returns([]);
        templateRepository.GetPublishedSessionTemplateVersion(session.SourceTemplateId.Value, session.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await service.ComputeDiffAsync(sessionId, 2, CancellationToken.None);

        await Assert.That(result.AddedDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.AddedDefinitions[0].Key).IsEqualTo("session_field");
    }

    [Test]
    public async Task ComputeDiffAsync_WhenSessionDefinitionIsLocal_ReturnsUntouchedWarning()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var templateRepository = Substitute.For<IEventSessionTemplateRepository>();
        var sessionRepository = Substitute.For<IEventSessionRepository>();
        var runtimeRepository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var service = new EventSessionTemplateDiffService(templateRepository, sessionRepository, runtimeRepository);

        var session = new EventSession { Id = sessionId, Event = null!, Tenant = null!, TenantId = tenantId, SourceTemplateId = Guid.NewGuid(), SourceTemplateKey = "session-template", SourceTemplateVersion = 1 };
        var localDefinition = new EventSessionCustomPropertyDefinition { Id = Guid.NewGuid(), EventSessionId = sessionId, TenantId = tenantId, Namespace = "tenant.local", Key = "notes", DisplayName = "Notes", PropertyType = PropertyType.Text, IsActive = true, ExposureLevel = ExposureLevel.Public };
        var template = new EventSessionTemplate { Id = session.SourceTemplateId.Value, EventTemplateId = Guid.NewGuid(), TenantId = tenantId, SessionTemplateKey = "session-template", DisplayName = "Session Template", Version = 2, IsPublished = true, IsActive = true };

        sessionRepository.GetById(sessionId).Returns(session);
        runtimeRepository.GetAllDefinitionsForSession(sessionId).Returns([localDefinition]);
        templateRepository.GetPublishedSessionTemplateVersion(session.SourceTemplateId.Value, session.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await service.ComputeDiffAsync(sessionId, 2, CancellationToken.None);

        await Assert.That(result.UntouchedLocalDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.UntouchedLocalDefinitions[0].Reason).IsEqualTo("LocallyAdded");
    }

    [Test]
    public async Task ComputeDiffAsync_WhenSessionOptionChangesExist_ReturnsOptionAddsModifiesAndRetires()
    {
        var sessionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var sourceTemplateId = Guid.NewGuid();
        var sourceTemplateDefinitionId = Guid.NewGuid();
        var templateRepository = Substitute.For<IEventSessionTemplateRepository>();
        var sessionRepository = Substitute.For<IEventSessionRepository>();
        var runtimeRepository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var service = new EventSessionTemplateDiffService(templateRepository, sessionRepository, runtimeRepository);

        var session = new EventSession { Id = sessionId, Event = null!, Tenant = null!, TenantId = tenantId, SourceTemplateId = sourceTemplateId, SourceTemplateKey = "session-template", SourceTemplateVersion = 1 };
        var runtimeDefinition = new EventSessionCustomPropertyDefinition { Id = Guid.NewGuid(), EventSessionId = sessionId, TenantId = tenantId, Namespace = "tenant.sync", Key = "session_field", DisplayName = "Session Field", PropertyType = PropertyType.Option, IsActive = true, ExposureLevel = ExposureLevel.Public, SourceTemplateId = sourceTemplateId, SourceTemplateKey = session.SourceTemplateKey, SourceTemplateVersion = 1, SourceTemplateDefinitionId = sourceTemplateDefinitionId, InstantiatedAt = DateTimeOffset.UtcNow, ConcurrencyStamp = Guid.NewGuid() };
        var runtimeOption = CreateRuntimeOption(runtimeDefinition.Id, runtimeDefinition.Namespace, "old_option", sourceTemplateOptionId: Guid.NewGuid(), displayName: "Old Label");
        var retiredRuntimeOption = CreateRuntimeOption(runtimeDefinition.Id, runtimeDefinition.Namespace, "retired_option", sourceTemplateOptionId: Guid.NewGuid(), displayName: "Retired Label");
        SetRuntimeOptions(runtimeDefinition, [runtimeOption, retiredRuntimeOption]);

        var templateDefinition = new EventSessionTemplateCustomPropertyDefinition { Id = sourceTemplateDefinitionId, EventSessionTemplateId = sourceTemplateId, TenantId = tenantId, Namespace = runtimeDefinition.Namespace, Key = runtimeDefinition.Key, DisplayName = runtimeDefinition.DisplayName, PropertyType = PropertyType.Option, IsActive = true, ExposureLevel = ExposureLevel.Public };
        var modifiedTemplateOption = CreateTemplateOption(templateDefinition.Id, runtimeDefinition.Namespace, "old_option", runtimeOption.SourceTemplateOptionId!.Value, displayName: "New Label");
        var addedTemplateOption = CreateTemplateOption(templateDefinition.Id, runtimeDefinition.Namespace, "new_option", Guid.NewGuid(), displayName: "Brand New");
        SetTemplateOptions(templateDefinition, [modifiedTemplateOption, addedTemplateOption]);
        var template = new EventSessionTemplate { Id = sourceTemplateId, EventTemplateId = Guid.NewGuid(), TenantId = tenantId, SessionTemplateKey = "session-template", DisplayName = "Session Template", Version = 2, IsPublished = true, IsActive = true };
        SetSessionTemplateDefinitions(template, [templateDefinition]);

        sessionRepository.GetById(sessionId).Returns(session);
        runtimeRepository.GetAllDefinitionsForSession(sessionId).Returns([runtimeDefinition]);
        templateRepository.GetPublishedSessionTemplateVersion(sourceTemplateId, session.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await service.ComputeDiffAsync(sessionId, 2, CancellationToken.None);

        await Assert.That(result.ModifiedOptions.Count).IsEqualTo(1);
        await Assert.That(result.AddedOptions.Count).IsEqualTo(1);
        await Assert.That(result.RetiredOptions.Count).IsEqualTo(1);
        await Assert.That(result.ModifiedOptions[0].FieldChanges.Any(x => x.FieldName == "DisplayName")).IsTrue();
        await Assert.That(result.RetiredOptions[0].Key).IsEqualTo("retired_option");
        await Assert.That(result.RetiredOptions[0].CurrentConcurrencyStamp).IsEqualTo(retiredRuntimeOption.ConcurrencyStamp);
    }

    private static void SetSessionTemplateDefinitions(EventSessionTemplate template, IEnumerable<EventSessionTemplateCustomPropertyDefinition> definitions)
        => template.ReplaceDefinitions(definitions);

    private static EventSessionTemplateCustomPropertyOption CreateTemplateOption(Guid definitionId, string ns, string key, Guid id, string displayName)
        => new()
        {
            Id = id,
            EventSessionTemplateCustomPropertyDefinitionId = definitionId,
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            Value = key,
            IsActive = true
        };

    private static EventSessionCustomPropertyOption CreateRuntimeOption(Guid definitionId, string ns, string key, Guid? sourceTemplateOptionId, string displayName)
        => new()
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definitionId,
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            Value = key,
            IsActive = true,
            SourceTemplateOptionId = sourceTemplateOptionId,
            SourceTemplateVersion = 1,
            ConcurrencyStamp = Guid.NewGuid()
        };

    private static void SetTemplateOptions(EventSessionTemplateCustomPropertyDefinition definition, IEnumerable<EventSessionTemplateCustomPropertyOption> options)
        => definition.ReplaceOptions(options);

    private static void SetRuntimeOptions(EventSessionCustomPropertyDefinition definition, IEnumerable<EventSessionCustomPropertyOption> options)
    {
        foreach (EventSessionCustomPropertyOption option in options)
        {
            definition.AddOption(option);
        }
    }
}
