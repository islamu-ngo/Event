using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizationPosition;
using Explore.Application.Features.OrganizationPositions.Handlers.Queries;
using Explore.Application.Features.OrganizationPositions.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationPositionOperationTests
{
    [Test]
    public async Task OrganizationPositionCapability_RegistersTwoScopedQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetOrganizationPositionDetailsRequest), typeof(GetOrganizationPositionDetailsRequestHandler),
            typeof(GetOrganizationPositionListRequest), typeof(GetOrganizationPositionListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetOrganizationPositionDetailsRequest), typeof(GetOrganizationPositionListRequest) });
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetOrganizationPositionDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<OrganizationPositionDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
