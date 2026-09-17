using System.Reflection;
using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Queries;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeCustomPropertyDefinitionOperationTests
{
    [Test]
    [Arguments(typeof(CreateCustomPropertyDefinitionCommand), typeof(CreateCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(UpdateCustomPropertyDefinitionCommand), typeof(UpdateCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<Guid>), false)]
    [Arguments(typeof(DeleteCustomPropertyDefinitionCommand), typeof(DeleteCustomPropertyDefinitionCommandHandler), typeof(bool), false)]
    [Arguments(typeof(PurgeCustomPropertyDefinitionCommand), typeof(PurgeCustomPropertyDefinitionCommandHandler), typeof(BaseCommandResponse<CustomPropertyPurgeResultDto>), false)]
    [Arguments(typeof(GetCustomPropertyDefinitionDetailsQuery), typeof(GetCustomPropertyDefinitionDetailsQueryHandler), typeof(CustomPropertyDefinitionDto), true)]
    [Arguments(typeof(GetCustomPropertyDefinitionListQuery), typeof(GetCustomPropertyDefinitionListQueryHandler), typeof(PaginatedResult<CustomPropertyDefinitionListDto>), true)]
    public async Task Operation_ExposesOneNativeShapeWithoutLegacyDispatch(Type request, Type handler, Type result, bool query)
    {
        var marker = (query ? typeof(IQuery<>) : typeof(ICommand<>)).MakeGenericType(result);
        await Assert.That(marker.IsAssignableFrom(request)).IsTrue();
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        var port = (query ? typeof(IQueryHandler<,>) : typeof(ICommandHandler<,>)).MakeGenericType(request, result);
        await Assert.That(handler.GetInterfaces()).IsEquivalentTo(new[] { port });
        await Assert.That(handler.GetMethod("Handle")).IsNull();
        var method = handler.GetMethod(query ? "QueryAsync" : "ExecuteAsync")!;
        await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<>).MakeGenericType(result));
        await Assert.That(method.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { request, typeof(CancellationToken) });
        if (query)
        {
            await Assert.That(request.Name.EndsWith("Query", StringComparison.Ordinal)).IsTrue();
            await Assert.That(new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single().ReadState)
                .IsEqualTo(NullabilityState.NotNull);
        }
        else
        {
            var protection = request.GetCustomAttribute<AuthorizeResourceAttribute>()!;
            await Assert.That(protection.Resource).IsEqualTo(ResourceKinds.Tenant);
            await Assert.That(protection.Action).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(typeof(ISecureRequest).IsAssignableFrom(request)).IsTrue();
        }
    }

    [Test]
    public async Task Discovery_RegistersExactlyFourScopedCommandsAndTwoScopedQueries()
    {
        var services = new ServiceCollection();
        var types = typeof(CreateCustomPropertyDefinitionCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.CustomPropertyDefinitions.", StringComparison.Ordinal) == true);
        services.AddNativeOperations(types);
        var ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(6);
        await Assert.That(ports.All(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(4);
        await Assert.That(ports.Count(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(2);
    }
}
