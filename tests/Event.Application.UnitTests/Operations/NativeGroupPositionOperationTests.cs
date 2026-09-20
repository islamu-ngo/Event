using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.GroupPosition;
using Explore.Application.Features.GroupPositions.Handlers.Queries;
using Explore.Application.Features.GroupPositions.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeGroupPositionOperationTests
{
    [Test]
    public async Task GroupPositionCapability_RegistersTwoScopedQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetGroupPositionDetailsRequest), typeof(GetGroupPositionDetailsRequestHandler),
            typeof(GetGroupPositionListRequest), typeof(GetGroupPositionListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetGroupPositionDetailsRequest), typeof(GetGroupPositionListRequest) });
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetGroupPositionDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<GroupPositionDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
