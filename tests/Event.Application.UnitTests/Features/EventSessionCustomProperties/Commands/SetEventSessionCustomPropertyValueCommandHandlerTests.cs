using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventSessionCustomProperties.Commands;

public class SetEventSessionCustomPropertyValueCommandHandlerTests
{
    [Test]
    public async Task Handle_WhenSingleValueDefinitionReceivesOrdinalOne_ReturnsFailure()
    {
        var definitionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();

        repository.GetDefinitionWithDetails(definitionId).Returns(CreateDefinition(definitionId, sessionId, isMulti: false));
        var handler = CreateSut(repository);

        var result = await handler.Handle(
            CreateCommand(definitionId, sessionId, ordinal: 1, textValue: "Arabic"),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That((result.Errors ?? []).Any(error => error.Contains("ordinal 0", StringComparison.Ordinal))).IsTrue();
        await repository.DidNotReceiveWithAnyArgs().SetValue(default!, default);
    }

    [Test]
    public async Task Handle_WhenMultiValueDefinitionReceivesDuplicateNormalizedValue_ReturnsFailure()
    {
        var definitionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var definition = CreateDefinition(definitionId, sessionId, isMulti: true);
        SetValues(definition,
        [
            new EventSessionCustomPropertyValue
            {
                Id = Guid.NewGuid(),
                EventSessionCustomPropertyDefinitionId = definitionId,
                EventSessionId = sessionId,
                Ordinal = 0,
                TextValue = "Alpha"
            }
        ]);
        repository.GetDefinitionWithDetails(definitionId).Returns(definition);
        var handler = CreateSut(repository);

        var result = await handler.Handle(
            CreateCommand(definitionId, sessionId, ordinal: 1, textValue: " alpha "),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That((result.Errors ?? []).Any(error => error.Contains("Duplicate normalized values", StringComparison.Ordinal))).IsTrue();
        await repository.DidNotReceiveWithAnyArgs().SetValue(default!, default);
    }

    [Test]
    public async Task Handle_WhenOptionDefinitionReceivesInactiveOption_ReturnsFailure()
    {
        var definitionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var definition = CreateDefinition(definitionId, sessionId, isMulti: false, propertyType: PropertyType.Option);
        SetOptions(definition,
        [
            new EventSessionCustomPropertyOption
            {
                Id = optionId,
                EventSessionCustomPropertyDefinitionId = definitionId,
                Namespace = "tenant.session",
                Key = "arabic",
                DisplayName = "Arabic",
                Value = "arabic",
                IsActive = false
            }
        ]);
        repository.GetDefinitionWithDetails(definitionId).Returns(definition);
        var handler = CreateSut(repository);

        var result = await handler.Handle(
            CreateOptionCommand(definitionId, sessionId, optionId),
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That((result.Errors ?? []).Any(error => error.Contains("active option", StringComparison.Ordinal))).IsTrue();
        await repository.DidNotReceiveWithAnyArgs().SetValue(default!, default);
    }

    private static SetEventSessionCustomPropertyValueCommandHandler CreateSut(IEventSessionCustomPropertyRepository repository)
    {
        return new SetEventSessionCustomPropertyValueCommandHandler(
            repository,
            Substitute.For<IEventSessionCustomPropertyProjectionUpdater>(),
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ITenantContext>(),
            Substitute.For<ICurrentUserService>(),
            Substitute.For<IMapper>());
    }

    private static SetEventSessionCustomPropertyValueCommand CreateCommand(Guid definitionId, Guid sessionId, int ordinal, string textValue)
    {
        return new SetEventSessionCustomPropertyValueCommand
        {
            ValueDto = new SetEventSessionCustomPropertyValueDto
            {
                EventSessionCustomPropertyDefinitionId = definitionId,
                EventSessionId = sessionId,
                Ordinal = ordinal,
                TextValue = textValue
            }
        };
    }

    private static SetEventSessionCustomPropertyValueCommand CreateOptionCommand(Guid definitionId, Guid sessionId, Guid optionId)
    {
        return new SetEventSessionCustomPropertyValueCommand
        {
            ValueDto = new SetEventSessionCustomPropertyValueDto
            {
                EventSessionCustomPropertyDefinitionId = definitionId,
                EventSessionId = sessionId,
                OptionId = optionId
            }
        };
    }

    private static EventSessionCustomPropertyDefinition CreateDefinition(
        Guid definitionId,
        Guid sessionId,
        bool isMulti,
        PropertyType propertyType = PropertyType.Text)
    {
        return new EventSessionCustomPropertyDefinition
        {
            Id = definitionId,
            EventSessionId = sessionId,
            TenantId = Guid.NewGuid(),
            Namespace = "tenant.session",
            Key = "primary_language",
            DisplayName = "Primary Language",
            PropertyType = propertyType,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            IsActive = true,
            IsMulti = isMulti
        };
    }

    private static void SetValues(EventSessionCustomPropertyDefinition definition, IEnumerable<EventSessionCustomPropertyValue> values)
    {
        foreach (EventSessionCustomPropertyValue value in values)
        {
            definition.AddValue(value);
        }
    }

    private static void SetOptions(EventSessionCustomPropertyDefinition definition, IEnumerable<EventSessionCustomPropertyOption> options)
    {
        foreach (EventSessionCustomPropertyOption option in options)
        {
            definition.AddOption(option);
        }
    }
}
