using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventTemplateSync;

public class EventTemplateDiffServiceTests
{
    private readonly IEventTemplateRepository _templateRepository = Substitute.For<IEventTemplateRepository>();
    private readonly IEventRepository _eventRepository = Substitute.For<IEventRepository>();
    private readonly IEventCustomPropertyRepository _runtimeRepository = Substitute.For<IEventCustomPropertyRepository>();

    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();

    private EventTemplateDiffService CreateSut() => new(_templateRepository, _eventRepository, _runtimeRepository);

    [Test]
    public async Task ComputeDiffAsync_WhenTemplateMatchesRuntime_ReturnsEmptyDiff()
    {
        var currentEvent = CreateEvent();
        var runtimeDefinition = CreateRuntimeDefinition();
        var template = CreateTemplate(1, CreateTemplateDefinition(runtimeDefinition.Namespace, runtimeDefinition.Key, runtimeDefinition.SourceTemplateDefinitionId!.Value));
        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([runtimeDefinition]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 1, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 1, CancellationToken.None);

        await Assert.That(result.AddedDefinitions.Count).IsEqualTo(0);
        await Assert.That(result.ModifiedDefinitions.Count).IsEqualTo(0);
        await Assert.That(result.RetiredDefinitions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ComputeDiffAsync_WhenTemplateAddsDefinition_ReturnsAddedDefinition()
    {
        var currentEvent = CreateEvent();
        var template = CreateTemplate(2,
            CreateTemplateDefinition("tenant.sync", "existing", Guid.NewGuid()),
            CreateTemplateDefinition("tenant.sync", "new_field", Guid.NewGuid()));

        var existingRuntime = CreateRuntimeDefinition(key: "existing");
        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([existingRuntime]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 2, CancellationToken.None);

        await Assert.That(result.AddedDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.AddedDefinitions[0].Key).IsEqualTo("new_field");
    }

    [Test]
    public async Task ComputeDiffAsync_WhenDefinitionFieldsDiffer_ReturnsModifiedDefinition()
    {
        var currentEvent = CreateEvent();
        var runtimeDefinition = CreateRuntimeDefinition(displayName: "Old Name");
        var templateDefinition = CreateTemplateDefinition(runtimeDefinition.Namespace, runtimeDefinition.Key, runtimeDefinition.SourceTemplateDefinitionId!.Value, displayName: "New Name");
        var template = CreateTemplate(2, templateDefinition);

        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([runtimeDefinition]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 2, CancellationToken.None);

        await Assert.That(result.ModifiedDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.ModifiedDefinitions[0].FieldChanges.Any(x => x.FieldName == "DisplayName")).IsTrue();
    }

    [Test]
    public async Task ComputeDiffAsync_WhenRuntimeDefinitionMissingFromTemplate_ReturnsRetiredDefinition()
    {
        var currentEvent = CreateEvent();
        var runtimeDefinition = CreateRuntimeDefinition();
        var template = CreateTemplate(2);

        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([runtimeDefinition]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 2, CancellationToken.None);

        await Assert.That(result.RetiredDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.RetiredDefinitions[0].Key).IsEqualTo(runtimeDefinition.Key);
    }

    [Test]
    public async Task ComputeDiffAsync_WhenRuntimeDefinitionIsLocal_ReturnsUntouchedLocalDefinition()
    {
        var currentEvent = CreateEvent();
        var localDefinition = CreateRuntimeDefinition(sourceTemplateDefinitionId: null, sourceTemplateId: null);
        localDefinition.SourceTemplateDefinitionId = null;
        localDefinition.SourceTemplateId = null;
        var template = CreateTemplate(2);

        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([localDefinition]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 2, CancellationToken.None);

        await Assert.That(result.UntouchedLocalDefinitions.Count).IsEqualTo(1);
        await Assert.That(result.UntouchedLocalDefinitions[0].Reason).IsEqualTo("LocallyAdded");
    }

    [Test]
    public async Task ComputeDiffAsync_WhenOptionChangesExist_ReturnsOptionAddsModifiesAndRetires()
    {
        var currentEvent = CreateEvent();
        var runtimeDefinition = CreateRuntimeDefinition(propertyType: PropertyType.Option);
        var runtimeOption = CreateRuntimeOption(runtimeDefinition.Id, runtimeDefinition.Namespace, "old_option", sourceTemplateOptionId: Guid.NewGuid(), displayName: "Old Label");
        var retiredRuntimeOption = CreateRuntimeOption(runtimeDefinition.Id, runtimeDefinition.Namespace, "retired_option", sourceTemplateOptionId: Guid.NewGuid(), displayName: "Retired Label");
        SetRuntimeOptions(runtimeDefinition, [runtimeOption, retiredRuntimeOption]);

        var templateDefinition = CreateTemplateDefinition(runtimeDefinition.Namespace, runtimeDefinition.Key, runtimeDefinition.SourceTemplateDefinitionId!.Value, propertyType: PropertyType.Option);
        var modifiedTemplateOption = CreateTemplateOption(templateDefinition.Id, runtimeDefinition.Namespace, "old_option", runtimeOption.SourceTemplateOptionId!.Value, displayName: "New Label");
        var addedTemplateOption = CreateTemplateOption(templateDefinition.Id, runtimeDefinition.Namespace, "new_option", Guid.NewGuid(), displayName: "Brand New");
        SetTemplateOptions(templateDefinition, [modifiedTemplateOption, addedTemplateOption]);
        var template = CreateTemplate(2, templateDefinition);

        _eventRepository.GetById(_eventId).Returns(currentEvent);
        _runtimeRepository.GetAllDefinitionsForEvent(_eventId).Returns([runtimeDefinition]);
        _templateRepository.GetPublishedTemplateVersion(_tenantId, currentEvent.SourceTemplateKey!, 2, Arg.Any<CancellationToken>()).Returns(template);

        var result = await CreateSut().ComputeDiffAsync(_eventId, 2, CancellationToken.None);

        await Assert.That(result.ModifiedOptions.Count).IsEqualTo(1);
        await Assert.That(result.AddedOptions.Count).IsEqualTo(1);
        await Assert.That(result.RetiredOptions.Count).IsEqualTo(1);
        await Assert.That(result.ModifiedOptions[0].FieldChanges.Any(x => x.FieldName == "DisplayName")).IsTrue();
        await Assert.That(result.RetiredOptions[0].Key).IsEqualTo("retired_option");
        await Assert.That(result.RetiredOptions[0].CurrentConcurrencyStamp).IsEqualTo(retiredRuntimeOption.ConcurrencyStamp);
    }

    private Explore.Domain.Event CreateEvent() => new()
    {
        Id = _eventId,
        TenantId = _tenantId,
        SourceTemplateKey = "event-template",
        SourceTemplateVersion = 1,
        Title = "Event",
        Actor = null!,
        Tenant = null!,
        VisibilityType = null!,
        EventStatus = null!,
        EventFormat = null!
    };

    private static EventCustomPropertyDefinition CreateRuntimeDefinition(
        string ns = "tenant.sync",
        string key = "field",
        Guid? sourceTemplateDefinitionId = null,
        Guid? sourceTemplateId = null,
        string displayName = "Field",
        PropertyType propertyType = PropertyType.Text)
        => new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            PropertyType = propertyType,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public,
            SourceTemplateDefinitionId = sourceTemplateDefinitionId ?? Guid.NewGuid(),
            SourceTemplateId = sourceTemplateId ?? Guid.NewGuid(),
            SourceTemplateKey = "event-template",
            SourceTemplateVersion = 1,
            InstantiatedAt = DateTimeOffset.UtcNow
        };

    private static EventTemplate CreateTemplate(int version, params EventTemplateCustomPropertyDefinition[] definitions)
    {
        var template = new EventTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TemplateKey = "event-template",
            DisplayName = "Template",
            Version = version,
            IsPublished = true,
            IsActive = true
        };
        SetTemplateDefinitions(template, definitions);
        return template;
    }

    private static EventTemplateCustomPropertyDefinition CreateTemplateDefinition(
        string ns,
        string key,
        Guid id,
        string displayName = "Field",
        PropertyType propertyType = PropertyType.Text)
        => new()
        {
            Id = id,
            EventTemplateId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            PropertyType = propertyType,
            IsActive = true,
            ExposureLevel = ExposureLevel.Public
        };

    private static EventTemplateCustomPropertyOption CreateTemplateOption(Guid definitionId, string ns, string key, Guid id, string displayName)
        => new()
        {
            Id = id,
            EventTemplateCustomPropertyDefinitionId = definitionId,
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            Value = key,
            IsActive = true
        };

    private static EventCustomPropertyOption CreateRuntimeOption(Guid definitionId, string ns, string key, Guid? sourceTemplateOptionId, string displayName)
        => new()
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definitionId,
            Namespace = ns,
            Key = key,
            DisplayName = displayName,
            Value = key,
            IsActive = true,
            SourceTemplateOptionId = sourceTemplateOptionId,
            SourceTemplateVersion = 1
        };

    private static void SetTemplateDefinitions(EventTemplate template, IEnumerable<EventTemplateCustomPropertyDefinition> definitions)
        => template.ReplaceDefinitions(definitions);

    private static void SetTemplateOptions(EventTemplateCustomPropertyDefinition definition, IEnumerable<EventTemplateCustomPropertyOption> options)
        => definition.ReplaceOptions(options);

    private static void SetRuntimeOptions(EventCustomPropertyDefinition definition, IEnumerable<EventCustomPropertyOption> options)
    {
        foreach (EventCustomPropertyOption option in options)
        {
            definition.AddOption(option);
        }
    }
}
