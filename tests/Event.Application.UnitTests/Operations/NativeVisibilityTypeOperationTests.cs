using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Handlers.Queries;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeVisibilityTypeOperationTests
{
    [Test]
    public async Task VisibilityTypeCapability_RegistersTwoScopedQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetVisibilityTypeDetailsRequest), typeof(GetVisibilityTypeDetailsRequestHandler),
            typeof(GetVisibilityTypeListRequest), typeof(GetVisibilityTypeListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetVisibilityTypeDetailsRequest), typeof(GetVisibilityTypeListRequest) });
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetVisibilityTypeDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<VisibilityTypeDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
