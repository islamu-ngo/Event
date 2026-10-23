using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionTemplates.Handlers.Commands;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private static async Task InvokeAdministrativeOwnerAsync(
        IServiceProvider services, ISerializableAttemptOwner physicalOwner, int owner,
        Guid targetId, Guid parentId, Guid stamp, string key, CancellationToken token)
    {
        bool succeeded = owner switch
        {
            0 => (await ActivatorUtilities.CreateInstance<PurgeCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId, Reason = "Unused administrative definition" }, token)).IsSuccess,
            1 => (await ActivatorUtilities.CreateInstance<CreateEventCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionDto = new()
                    {
                        EventId = parentId,
                        Namespace = "tenant",
                        Key = key,
                        DisplayName = key,
                        PropertyType = PropertyType.Text,
                        ExposureLevel = ExposureLevel.Public,
                        IsActive = true
                    }
                }, token)).IsSuccess,
            2 => (await ActivatorUtilities.CreateInstance<UpdateEventCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionId = targetId,
                    ExpectedConcurrencyStamp = stamp,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    DefinitionDto = new() { Metadata = new() { DisplayName = key } }
                }, token)).IsSuccess,
            3 => await ActivatorUtilities.CreateInstance<DeleteEventCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId }, token),
            4 => (await ActivatorUtilities.CreateInstance<PurgeEventCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId, Reason = "Unused administrative definition" }, token)).IsSuccess,
            5 => (await ActivatorUtilities.CreateInstance<SetEventCustomPropertyValueCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    ValueDto = new() { EventCustomPropertyDefinitionId = targetId, EventId = parentId, TextValue = key }
                }, token)).IsSuccess,
            6 => (await ActivatorUtilities.CreateInstance<SetEventCustomPropertyMultiValuesCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionId = targetId,
                    EventId = parentId,
                    Values = [new() { EventCustomPropertyDefinitionId = targetId, EventId = parentId, TextValue = key }]
                }, token)).IsSuccess,
            7 => (await ActivatorUtilities.CreateInstance<CreateEventSessionCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionDto = new()
                    {
                        EventSessionId = parentId,
                        Namespace = "tenant",
                        Key = key,
                        DisplayName = key,
                        PropertyType = PropertyType.Text,
                        ExposureLevel = ExposureLevel.Public,
                        IsActive = true
                    }
                }, token)).IsSuccess,
            8 => (await ActivatorUtilities.CreateInstance<UpdateEventSessionCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionId = targetId,
                    ExpectedConcurrencyStamp = stamp,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    DefinitionDto = new() { Metadata = new() { DisplayName = key } }
                }, token)).IsSuccess,
            9 => await ActivatorUtilities.CreateInstance<DeleteEventSessionCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId }, token),
            10 => (await ActivatorUtilities.CreateInstance<PurgeEventSessionCustomPropertyDefinitionCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId, Reason = "Unused administrative definition" }, token)).IsSuccess,
            11 => (await ActivatorUtilities.CreateInstance<SetEventSessionCustomPropertyValueCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    ValueDto = new() { EventSessionCustomPropertyDefinitionId = targetId, EventSessionId = parentId, TextValue = key }
                }, token)).IsSuccess,
            12 => (await ActivatorUtilities.CreateInstance<SetEventSessionCustomPropertyMultiValuesCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    DefinitionId = targetId,
                    EventSessionId = parentId,
                    Values = [new() { EventSessionCustomPropertyDefinitionId = targetId, EventSessionId = parentId, TextValue = key }]
                }, token)).IsSuccess,
            13 => (await ActivatorUtilities.CreateInstance<CreateEventSessionTemplateCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    SessionTemplateDto = new() { EventTemplateId = parentId, SessionTemplateKey = key, DisplayName = key }
                }, token)).IsSuccess,
            14 => (await ActivatorUtilities.CreateInstance<UpdateEventSessionTemplateCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new()
                {
                    SessionTemplateId = targetId,
                    ExpectedConcurrencyStamp = stamp,
                    TenantId = PlatformDefaults.DefaultTenantId,
                    SessionTemplateDto = new() { Metadata = new() { DisplayName = key } }
                }, token)).IsSuccess,
            15 => await ActivatorUtilities.CreateInstance<DeleteEventSessionTemplateCommandHandler>(services, physicalOwner)
                .ExecuteAsync(new() { Id = targetId }, token),
            _ => throw new ArgumentOutOfRangeException(nameof(owner))
        };
        await Assert.That(succeeded).IsTrue();
    }
}
