using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Queries;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeSessionCustomPropertyOperationTests
{
    [Test]
    [Arguments(typeof(CreateEventSessionCustomPropertyDefinitionCommand), typeof(CreateEventSessionCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(UpdateEventSessionCustomPropertyDefinitionCommand), typeof(UpdateEventSessionCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(DeleteEventSessionCustomPropertyDefinitionCommand), typeof(DeleteEventSessionCustomPropertyDefinitionCommandHandler), typeof(bool), false)]
    [Arguments(typeof(PurgeEventSessionCustomPropertyDefinitionCommand), typeof(PurgeEventSessionCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<CustomPropertyPurgeResultDto>), false)]
    [Arguments(typeof(SetEventSessionCustomPropertyValueCommand), typeof(SetEventSessionCustomPropertyValueCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(SetEventSessionCustomPropertyMultiValuesCommand), typeof(SetEventSessionCustomPropertyMultiValuesCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(GetEventSessionCustomPropertyDefinitionDetailsRequest), typeof(GetEventSessionCustomPropertyDefinitionDetailsRequestHandler), typeof(EventSessionCustomPropertyDefinitionDto), true)]
    [Arguments(typeof(GetEventSessionCustomPropertyDefinitionListRequest), typeof(GetEventSessionCustomPropertyDefinitionListRequestHandler), typeof(PaginatedResult<EventSessionCustomPropertyDefinitionListDto>), true)]
    [Arguments(typeof(GetEventSessionCustomPropertyValuesRequest), typeof(GetEventSessionCustomPropertyValuesRequestHandler), typeof(List<EventSessionCustomPropertyValueDto>), true)]
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
        services.AddNativeOperations(typeof(CreateEventSessionCustomPropertyDefinitionCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.EventSessionCustomProperties.", StringComparison.Ordinal) == true));
        var ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(9);
        await Assert.That(ports.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(6);
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(3);
    }
}
