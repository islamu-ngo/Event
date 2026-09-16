using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.EventLocations.Handlers.Commands;
using Explore.Application.Features.EventLocations.Handlers.Queries;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Application.Features.EventLocations.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventLocationOperationTests
{
    [Test]
    [Arguments(typeof(UpdateEventLocationPolicyCommand), typeof(UpdateEventLocationPolicyCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(ConfirmEventLocationRemediationCommand), typeof(ConfirmEventLocationRemediationCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(GetPublicEventLocationsRequest), typeof(GetPublicEventLocationsRequestHandler), typeof(IReadOnlyList<EventLocationPublicDto>), true)]
    [Arguments(typeof(GetAttendeeEventLocationsRequest), typeof(GetAttendeeEventLocationsRequestHandler), typeof(IReadOnlyList<EventLocationAttendeeDto>), true)]
    [Arguments(typeof(GetManagementEventLocationRequest), typeof(GetManagementEventLocationRequestHandler), typeof(EventLocationManagementDto), true)]
    [Arguments(typeof(GetManagementEventLocationsRequest), typeof(GetManagementEventLocationsRequestHandler), typeof(IReadOnlyList<EventLocationManagementDto>), true)]
    [Arguments(typeof(GetEventLocationReviewQueueRequest), typeof(GetEventLocationReviewQueueRequestHandler), typeof(IReadOnlyList<EventLocationManagementDto>), true)]
    public async Task Operation_HasOneNativeContractAndPreservesProtection(Type request, Type handler, Type result, bool query)
    {
        Type expectedContractType = query
            ? typeof(IQuery<>).MakeGenericType(result)
            : typeof(ICommand<>).MakeGenericType(result);

        await Assert.That(expectedContractType.IsAssignableFrom(request)).IsTrue();
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();

        Type expectedPortType = query
            ? typeof(IQueryHandler<,>).MakeGenericType(request, result)
            : typeof(ICommandHandler<,>).MakeGenericType(request, result);

        await Assert.That(handler.GetInterfaces()).IsEquivalentTo(new[] { expectedPortType });
        await Assert.That(handler.GetMethod("Handle")).IsNull();

        MethodInfo method = handler.GetMethod(query ? "QueryAsync" : "ExecuteAsync")!;
        await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<>).MakeGenericType(result));
        await Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { request, typeof(CancellationToken) });

        if (!query)
        {
            AuthorizeResourceAttribute protection = request.GetCustomAttribute<AuthorizeResourceAttribute>()!;
            await Assert.That(protection.Resource).IsEqualTo(ResourceKinds.Event);
            await Assert.That(protection.Action).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(typeof(ISecureRequest).IsAssignableFrom(request)).IsTrue();
        }
    }

    [Test]
    public async Task Discovery_RegistersTwoScopedCommandsAndFiveScopedQueries()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(UpdateEventLocationPolicyCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.EventLocations.", StringComparison.Ordinal) == true));

        ServiceDescriptor[] ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(2);
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(5);
    }
}
