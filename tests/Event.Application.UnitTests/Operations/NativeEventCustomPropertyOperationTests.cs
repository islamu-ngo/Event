using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventCustomProperties.Handlers.Queries;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventCustomPropertyOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventCustomPropertyDefinitionCommand), typeof(CreateEventCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(UpdateEventCustomPropertyDefinitionCommand), typeof(UpdateEventCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(DeleteEventCustomPropertyDefinitionCommand), typeof(DeleteEventCustomPropertyDefinitionCommandHandler), typeof(bool), false)]
    [Arguments(typeof(PurgeEventCustomPropertyDefinitionCommand), typeof(PurgeEventCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<CustomPropertyPurgeResultDto>), false)]
    [Arguments(typeof(SetEventCustomPropertyValueCommand), typeof(SetEventCustomPropertyValueCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(SetEventCustomPropertyMultiValuesCommand), typeof(SetEventCustomPropertyMultiValuesCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(GetEventCustomPropertyDefinitionDetailsRequest), typeof(GetEventCustomPropertyDefinitionDetailsRequestHandler), typeof(EventCustomPropertyDefinitionDto), true)]
    [Arguments(typeof(GetEventCustomPropertyDefinitionListRequest), typeof(GetEventCustomPropertyDefinitionListRequestHandler), typeof(PaginatedResult<EventCustomPropertyDefinitionListDto>), true)]
    [Arguments(typeof(GetEventCustomPropertyValuesRequest), typeof(GetEventCustomPropertyValuesRequestHandler), typeof(List<EventCustomPropertyValueDto>), true)]
    public async Task Operation_HasOneNativeContractAndPreservesProtection(Type request, Type handler, Type result, bool query)
    {
        await Assert.That((query ? typeof(IQuery<>) : typeof(ICommand<>)).MakeGenericType(result).IsAssignableFrom(request)).IsTrue();
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
        var port = (query ? typeof(IQueryHandler<,>) : typeof(ICommandHandler<,>)).MakeGenericType(request, result);
        await Assert.That(handler.GetInterfaces()).IsEquivalentTo(new[] { port });
        await Assert.That(handler.GetMethod("Handle")).IsNull();
        var method = handler.GetMethod(query ? "QueryAsync" : "ExecuteAsync")!;
        await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<>).MakeGenericType(result));
        await Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { request, typeof(CancellationToken) });
        if (!query)
        {
            var protection = request.GetCustomAttribute<AuthorizeResourceAttribute>()!;
            await Assert.That(protection.Resource).IsEqualTo(ResourceKinds.Tenant);
            await Assert.That(protection.Action).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(typeof(ISecureRequest).IsAssignableFrom(request)).IsTrue();
        }
    }

    [Test]
    public async Task Discovery_RegistersSixScopedCommandsAndThreeScopedQueries()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(CreateEventCustomPropertyDefinitionCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.EventCustomProperties.", StringComparison.Ordinal) == true));
        var ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(9);
        await Assert.That(ports.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(6);
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(3);
    }
}
