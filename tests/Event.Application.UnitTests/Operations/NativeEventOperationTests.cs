using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventOperationTests
{
    private static readonly Type[] Requests =
    [
        // Commands (12)
        typeof(CreateEventCommand), typeof(UpdateEventCommand),
        typeof(UpdateEventDraftCommand), typeof(DeleteEventCommand),
        typeof(PublishEventCommand), typeof(ApprovePublishEventCommand),
        typeof(ArchiveEventCommand), typeof(CancelEventCommand),
        typeof(ImportEventCommand), typeof(ModerateEventCommand),
        typeof(HeavyRedactEventCommand), typeof(UnmoderateEventCommand),

        // Queries (12)
        typeof(GetAttendeeEventCalendarExportRequest), typeof(GetEventCalendarExportRequest),
        typeof(GetEventCreationContextRequest), typeof(GetEventDetailsRequest),
        typeof(GetEventListRequest), typeof(GetEventManagementDetailsRequest),
        typeof(GetEventModerationHistoryRequest), typeof(GetEventPublishReadinessRequest),
        typeof(GetManagedEventsByActorRequest), typeof(GetMyEventsRequest),
        typeof(GetPublicEventDetailsRequest), typeof(GetPublicEventOpenGraphImageRequest)
    ];

    [Test]
    [Arguments(typeof(CreateEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UpdateEventDraftCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(DeleteEventCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(PublishEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ApprovePublishEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ArchiveEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(CancelEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ImportEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(ModerateEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(HeavyRedactEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(UnmoderateEventCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(GetAttendeeEventCalendarExportRequest), typeof(IQuery<AttendeeEventCalendarExportDto?>))]
    [Arguments(typeof(GetEventCalendarExportRequest), typeof(IQuery<EventCalendarExportDto?>))]
    [Arguments(typeof(GetEventCreationContextRequest), typeof(IQuery<EventCreationContextDto>))]
    [Arguments(typeof(GetEventDetailsRequest), typeof(IQuery<EventDto?>))]
    [Arguments(typeof(GetEventListRequest), typeof(IQuery<PaginatedResult<EventListDto>>))]
    [Arguments(typeof(GetEventManagementDetailsRequest), typeof(IQuery<EventDto?>))]
    [Arguments(typeof(GetEventModerationHistoryRequest), typeof(IQuery<IReadOnlyList<EventModerationHistoryDto>?>))]
    [Arguments(typeof(GetEventPublishReadinessRequest), typeof(IQuery<EventPublishReadinessDto?>))]
    [Arguments(typeof(GetManagedEventsByActorRequest), typeof(IQuery<PaginatedResult<EventListDto>>))]
    [Arguments(typeof(GetMyEventsRequest), typeof(IQuery<PaginatedResult<EventListDto>>))]
    [Arguments(typeof(GetPublicEventDetailsRequest), typeof(IQuery<EventDto?>))]
    [Arguments(typeof(GetPublicEventOpenGraphImageRequest), typeof(IQuery<EventOpenGraphImageRenderResult?>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(24);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryEventOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateEventCommand).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(24);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
