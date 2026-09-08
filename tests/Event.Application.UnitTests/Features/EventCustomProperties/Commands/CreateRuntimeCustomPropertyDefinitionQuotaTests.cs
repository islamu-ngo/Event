using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Caching.Hybrid;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventCustomProperties.Commands;

public class CreateRuntimeCustomPropertyDefinitionQuotaTests
{
    [Test]
    public async Task EventHandle_WhenDefinitionCountIsOneBelowQuota_DoesNotReturnQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var repository = Substitute.For<IEventCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var mapper = Substitute.For<IMapper>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var definition = new EventCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            Namespace = "tenant.community",
            Key = "prayer_notes",
            DisplayName = "Prayer Notes"
        };
        var handler = CreateEventHandler(repository, quotaResolver, tenantId, mapper, unitOfWork);

        repository.ExistsDefinitionKey(eventId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForEvent(eventId, Arg.Any<CancellationToken>()).Returns(2);
        repository.CreateWithOptions(
                Arg.Any<EventCustomPropertyDefinition>(),
                Arg.Any<IReadOnlyCollection<EventCustomPropertyOption>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(definition);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key, tenantId, Arg.Any<CancellationToken>()).Returns(3);
        mapper.Map<EventCustomPropertyDefinition>(Arg.Any<CreateEventCustomPropertyDefinitionDto>()).Returns(definition);
        unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<EventCustomPropertyDefinition>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<EventCustomPropertyDefinition>>>().Invoke(CancellationToken.None));

        var result = await handler.Handle(
            new CreateEventCustomPropertyDefinitionCommand { DefinitionDto = CreateEventDto(eventId) },
            CancellationToken.None);

        await Assert.That(result.FailureCode).IsNotEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNull();
    }

    [Test]
    public async Task EventHandle_WhenDefinitionQuotaReached_ReturnsQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var repository = Substitute.For<IEventCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var handler = CreateEventHandler(repository, quotaResolver, tenantId);

        repository.ExistsDefinitionKey(eventId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForEvent(eventId, Arg.Any<CancellationToken>()).Returns(3);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key, tenantId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await handler.Handle(
            new CreateEventCustomPropertyDefinitionCommand { DefinitionDto = CreateEventDto(eventId) },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNotNull();
        await Assert.That(result.QuotaExceeded!.QuotaKey).IsEqualTo(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key);
        await Assert.That(result.QuotaExceeded.Limit).IsEqualTo(3);
        await Assert.That(result.QuotaExceeded.Actual).IsEqualTo(3);
        await Assert.That(result.QuotaExceeded.Attempted).IsEqualTo(4);
        await Assert.That(result.QuotaExceeded.Scope).IsEqualTo("event_custom_property_definitions");
        await Assert.That(result.QuotaExceeded.TenantId).IsEqualTo(tenantId);
        await Assert.That(result.Errors!.Single()).Contains(FailureCodes.QuotaExceeded);
        await repository.DidNotReceiveWithAnyArgs().CreateWithOptions(default!, default!, default, default);
    }

    [Test]
    public async Task EventHandle_WhenDefinitionCountIsAboveQuota_ReturnsQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var repository = Substitute.For<IEventCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var handler = CreateEventHandler(repository, quotaResolver, tenantId);

        repository.ExistsDefinitionKey(eventId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForEvent(eventId, Arg.Any<CancellationToken>()).Returns(4);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key, tenantId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await handler.Handle(
            new CreateEventCustomPropertyDefinitionCommand { DefinitionDto = CreateEventDto(eventId) },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNotNull();
        await Assert.That(result.QuotaExceeded!.Actual).IsEqualTo(4);
        await Assert.That(result.QuotaExceeded.Attempted).IsEqualTo(5);
        await repository.DidNotReceiveWithAnyArgs().CreateWithOptions(default!, default!, default, default);
    }

    [Test]
    public async Task EventSessionHandle_WhenDefinitionCountIsOneBelowQuota_DoesNotReturnQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var mapper = Substitute.For<IMapper>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var definition = new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            Namespace = "tenant.community",
            Key = "prayer_notes",
            DisplayName = "Prayer Notes"
        };
        var handler = CreateSessionHandler(repository, quotaResolver, tenantId, mapper, unitOfWork);

        repository.ExistsDefinitionKey(sessionId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForSession(sessionId, Arg.Any<CancellationToken>()).Returns(1);
        repository.CreateWithOptions(
                Arg.Any<EventSessionCustomPropertyDefinition>(),
                Arg.Any<IReadOnlyCollection<EventSessionCustomPropertyOption>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(definition);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key, tenantId, Arg.Any<CancellationToken>()).Returns(2);
        mapper.Map<EventSessionCustomPropertyDefinition>(Arg.Any<CreateEventSessionCustomPropertyDefinitionDto>()).Returns(definition);
        unitOfWork.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task<EventSessionCustomPropertyDefinition>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<EventSessionCustomPropertyDefinition>>>().Invoke(CancellationToken.None));

        var result = await handler.Handle(
            new CreateEventSessionCustomPropertyDefinitionCommand { DefinitionDto = CreateSessionDto(sessionId) },
            CancellationToken.None);

        await Assert.That(result.FailureCode).IsNotEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNull();
    }

    [Test]
    public async Task EventSessionHandle_WhenDefinitionQuotaReached_ReturnsQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var handler = CreateSessionHandler(repository, quotaResolver, tenantId);

        repository.ExistsDefinitionKey(sessionId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForSession(sessionId, Arg.Any<CancellationToken>()).Returns(2);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key, tenantId, Arg.Any<CancellationToken>()).Returns(2);

        var result = await handler.Handle(
            new CreateEventSessionCustomPropertyDefinitionCommand { DefinitionDto = CreateSessionDto(sessionId) },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNotNull();
        await Assert.That(result.QuotaExceeded!.QuotaKey).IsEqualTo(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key);
        await Assert.That(result.QuotaExceeded.Limit).IsEqualTo(2);
        await Assert.That(result.QuotaExceeded.Actual).IsEqualTo(2);
        await Assert.That(result.QuotaExceeded.Attempted).IsEqualTo(3);
        await Assert.That(result.QuotaExceeded.Scope).IsEqualTo("event_session_custom_property_definitions");
        await Assert.That(result.QuotaExceeded.TenantId).IsEqualTo(tenantId);
        await Assert.That(result.Errors!.Single()).Contains(FailureCodes.QuotaExceeded);
        await repository.DidNotReceiveWithAnyArgs().CreateWithOptions(default!, default!, default, default);
    }

    [Test]
    public async Task EventSessionHandle_WhenDefinitionCountIsAboveQuota_ReturnsQuotaFailure()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var repository = Substitute.For<IEventSessionCustomPropertyRepository>();
        var quotaResolver = Substitute.For<ICustomPropertyQuotaResolver>();
        var handler = CreateSessionHandler(repository, quotaResolver, tenantId);

        repository.ExistsDefinitionKey(sessionId, "tenant.community", "prayer_notes").Returns(false);
        repository.CountDefinitionsForSession(sessionId, Arg.Any<CancellationToken>()).Returns(3);
        quotaResolver.GetIntAsync(CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key, tenantId, Arg.Any<CancellationToken>()).Returns(2);

        var result = await handler.Handle(
            new CreateEventSessionCustomPropertyDefinitionCommand { DefinitionDto = CreateSessionDto(sessionId) },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
        await Assert.That(result.QuotaExceeded).IsNotNull();
        await Assert.That(result.QuotaExceeded!.Actual).IsEqualTo(3);
        await Assert.That(result.QuotaExceeded.Attempted).IsEqualTo(4);
        await repository.DidNotReceiveWithAnyArgs().CreateWithOptions(default!, default!, default, default);
    }

    private static CreateEventCustomPropertyDefinitionCommandHandler CreateEventHandler(
        IEventCustomPropertyRepository repository,
        ICustomPropertyQuotaResolver quotaResolver,
        Guid tenantId,
        IMapper? mapper = null,
        IUnitOfWork? unitOfWork = null)
    {
        var governancePolicy = Substitute.For<ICustomPropertyGovernancePolicy>();
        var tenantContext = Substitute.For<ITenantContext>();
        var currentUserService = Substitute.For<ICurrentUserService>();
        var cache = Substitute.For<HybridCache>();
        mapper ??= Substitute.For<IMapper>();
        unitOfWork ??= Substitute.For<IUnitOfWork>();

        tenantContext.TenantId.Returns(tenantId);
        quotaResolver.GetIntAsync(Arg.Any<string>(), tenantId, Arg.Any<CancellationToken>()).Returns(500);
        governancePolicy.EvaluateDefinition(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new CustomPropertyGovernanceEvaluation
            {
                NormalizedNamespace = "tenant.community",
                NormalizedKey = "prayer_notes",
            });

        return new CreateEventCustomPropertyDefinitionCommandHandler(
            repository,
            governancePolicy,
            quotaResolver,
            tenantContext,
            currentUserService,
            mapper,
            cache,
            unitOfWork);
    }

    private static CreateEventSessionCustomPropertyDefinitionCommandHandler CreateSessionHandler(
        IEventSessionCustomPropertyRepository repository,
        ICustomPropertyQuotaResolver quotaResolver,
        Guid tenantId,
        IMapper? mapper = null,
        IUnitOfWork? unitOfWork = null)
    {
        var governancePolicy = Substitute.For<ICustomPropertyGovernancePolicy>();
        var tenantContext = Substitute.For<ITenantContext>();
        var currentUserService = Substitute.For<ICurrentUserService>();
        var cache = Substitute.For<HybridCache>();
        mapper ??= Substitute.For<IMapper>();
        unitOfWork ??= Substitute.For<IUnitOfWork>();

        tenantContext.TenantId.Returns(tenantId);
        quotaResolver.GetIntAsync(Arg.Any<string>(), tenantId, Arg.Any<CancellationToken>()).Returns(500);
        governancePolicy.EvaluateDefinition(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new CustomPropertyGovernanceEvaluation
            {
                NormalizedNamespace = "tenant.community",
                NormalizedKey = "prayer_notes",
            });

        return new CreateEventSessionCustomPropertyDefinitionCommandHandler(
            repository,
            governancePolicy,
            quotaResolver,
            tenantContext,
            currentUserService,
            mapper,
            cache,
            unitOfWork);
    }

    private static CreateEventCustomPropertyDefinitionDto CreateEventDto(Guid eventId)
    {
        return new CreateEventCustomPropertyDefinitionDto
        {
            EventId = eventId,
            Namespace = "Tenant Community",
            Key = "Prayer Notes",
            DisplayName = "Prayer Notes",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            IsActive = true,
        };
    }

    private static CreateEventSessionCustomPropertyDefinitionDto CreateSessionDto(Guid sessionId)
    {
        return new CreateEventSessionCustomPropertyDefinitionDto
        {
            EventSessionId = sessionId,
            Namespace = "Tenant Community",
            Key = "Prayer Notes",
            DisplayName = "Prayer Notes",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            IsActive = true,
        };
    }
}
