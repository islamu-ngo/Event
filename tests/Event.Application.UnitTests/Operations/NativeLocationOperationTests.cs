using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Handlers.Commands;
using Explore.Application.Features.Locations.Handlers.Queries;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Features.Locations.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLocationOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersFiveCommandsAndTwoQueries()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateLocationCommand), typeof(CreateLocationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateLocationCommand), typeof(UpdateLocationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteLocationCommand), typeof(DeleteLocationCommandHandler),
                typeof(ICommandHandler<,>), typeof(bool)),
            (typeof(ClassifyLocationAsPrivateHomeCommand), typeof(ClassifyLocationAsPrivateHomeCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(AcceptPrivateHomeOwnershipCommand), typeof(AcceptPrivateHomeOwnershipCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetLocationDetailsRequest), typeof(GetLocationDetailsRequestHandler),
                typeof(IQueryHandler<,>), typeof(LocationDto)),
            (typeof(GetLocationListRequest), typeof(GetLocationListRequestHandler),
                typeof(IQueryHandler<,>), typeof(PaginatedResult<LocationListDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(
            operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }

    [Test]
    public async Task MissingLocation_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetLocationDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<LocationDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
