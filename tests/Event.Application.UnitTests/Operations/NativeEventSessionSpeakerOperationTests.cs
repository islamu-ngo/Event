using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Handlers.Commands;
using Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;
using Explore.Application.Features.EventSessionSpeakers.Requests.Commands;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionSpeakerOperationTests
{
    private static readonly (Type Request, Type Handler, Type Port)[] Cohort =
    [
        (typeof(CreateEventSessionSpeakerCommand), typeof(CreateEventSessionSpeakerCommandHandler),
            typeof(ICommandHandler<CreateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>)),
        (typeof(UpdateEventSessionSpeakerCommand), typeof(UpdateEventSessionSpeakerCommandHandler),
            typeof(ICommandHandler<UpdateEventSessionSpeakerCommand, BaseCommandResponse<Guid>>)),
        (typeof(DeleteEventSessionSpeakerCommand), typeof(DeleteEventSessionSpeakerCommandHandler),
            typeof(ICommandHandler<DeleteEventSessionSpeakerCommand, bool>)),
        (typeof(GetEventSessionSpeakerDetailsQuery), typeof(GetEventSessionSpeakerDetailsQueryHandler),
            typeof(IQueryHandler<GetEventSessionSpeakerDetailsQuery, EventSessionSpeakerDto?>)),
        (typeof(GetEventSessionSpeakerListQuery), typeof(GetEventSessionSpeakerListQueryHandler),
            typeof(IQueryHandler<GetEventSessionSpeakerListQuery, PaginatedResult<EventSessionSpeakerListDto>>)),
        (typeof(GetSessionsByActorQuery), typeof(GetSessionsByActorQueryHandler),
            typeof(IQueryHandler<GetSessionsByActorQuery, List<EventSessionSpeakerListDto>>)),
        (typeof(GetSpeakersBySessionQuery), typeof(GetSpeakersBySessionQueryHandler),
            typeof(IQueryHandler<GetSpeakersBySessionQuery, List<EventSessionSpeakerListDto>>))
    ];

    [Test]
    public async Task Cohort_RegistersExactlySevenScopedClosedPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(Cohort.SelectMany(operation => new[] { operation.Request, operation.Handler }));

        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType)).IsEquivalentTo(Cohort.Select(operation => operation.Port));
        await Assert.That(ports.All(port => port.ImplementationType is null)).IsTrue();
    }

    [Test]
    public async Task Requests_UseOneNativeShapeWithoutMediatRCompatibilityMarkers()
    {
        foreach (var operation in Cohort)
        {
            var nativeShapes = operation.Request.GetInterfaces().Where(type =>
                type == typeof(ICommand) ||
                type.IsGenericType && type.GetGenericTypeDefinition() is var definition &&
                    (definition == typeof(ICommand<>) || definition == typeof(IQuery<>))).ToArray();

            await Assert.That(nativeShapes.Length).IsEqualTo(1).Because(operation.Request.Name);
            await Assert.That(operation.Request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse()
                .Because(operation.Request.Name);
            await Assert.That(operation.Handler.GetInterfaces()).Contains(operation.Port).Because(operation.Handler.Name);
        }
    }

    [Test]
    public async Task ManagementOperations_RetainEventSessionUpdateAuthorizationMetadata()
    {
        Type[] protectedRequests =
        [
            typeof(CreateEventSessionSpeakerCommand),
            typeof(UpdateEventSessionSpeakerCommand),
            typeof(DeleteEventSessionSpeakerCommand),
            typeof(GetSpeakersBySessionQuery)
        ];

        foreach (var request in protectedRequests)
        {
            var attribute = request.GetCustomAttribute<AuthorizeResourceAttribute>();
            await Assert.That(attribute).IsNotNull().Because(request.Name);
            await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.EventSession).Because(request.Name);
            await Assert.That(attribute.Action).IsEqualTo(AuthorizationActions.Update).Because(request.Name);
        }
    }
}
