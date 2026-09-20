using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.EventPublicActions.Handlers.Commands;
using Explore.Application.Features.EventPublicActions.Handlers.Queries;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Features.EventPublicActions.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventPublicActionsOperationTests
{
    [Test]
    public async Task SixOperations_HaveOneNativeShapeAndDiscoverScopedPortsWithoutUnit()
    {
        (Type Request, Type Handler, Type Shape, Type Port)[] operations =
        [
            (typeof(GetEventPublicActionsRequest), typeof(GetEventPublicActionsRequestHandler),
                typeof(IQuery<IReadOnlyList<EventPublicActionDto>>), typeof(IQueryHandler<,>)),
            (typeof(GetEventPublicActionRequest), typeof(GetEventPublicActionRequestHandler),
                typeof(IQuery<EventPublicActionDto>), typeof(IQueryHandler<,>)),
            (typeof(CreateEventPublicActionCommand), typeof(CreateEventPublicActionCommandHandler),
                typeof(ICommand<BaseCommandResponse<Guid>>), typeof(ICommandHandler<,>)),
            (typeof(UpdateEventPublicActionCommand), typeof(UpdateEventPublicActionCommandHandler),
                typeof(ICommand<BaseCommandResponse<Guid>>), typeof(ICommandHandler<,>)),
            (typeof(DeleteEventPublicActionCommand), typeof(DeleteEventPublicActionCommandHandler),
                typeof(ICommand<BaseCommandResponse<Guid>>), typeof(ICommandHandler<,>)),
            (typeof(RecordEventPublicActionEngagementCommand), typeof(RecordEventPublicActionEngagementCommandHandler),
                typeof(ICommand), typeof(ICommandHandler<>))
        ];
        foreach (var operation in operations)
        {
            await Assert.That(operation.Shape.IsAssignableFrom(operation.Request)).IsTrue();
            await Assert.That(operation.Request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
            var method = operation.Handler.GetMethod(operation.Port == typeof(IQueryHandler<,>) ? "QueryAsync" : "ExecuteAsync")!;
            var result = operation.Shape == typeof(ICommand) ? typeof(Task)
                : typeof(Task<>).MakeGenericType(operation.Shape.GetGenericArguments());
            await Assert.That(method.ReturnType).IsEqualTo(result);
        }
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        foreach (var operation in operations)
        {
            var arguments = operation.Shape == typeof(ICommand) ? new[] { operation.Request }
                : new[] { operation.Request, operation.Shape.GetGenericArguments()[0] };
            var descriptor = services.Single(value => value.ServiceType == operation.Port.MakeGenericType(arguments));
            await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
