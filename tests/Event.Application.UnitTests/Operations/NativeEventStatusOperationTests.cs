using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventStatus;
using Explore.Application.Features.EventStatuses.Handlers.Queries;
using Explore.Application.Features.EventStatuses.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventStatusOperationTests
{
    [Test]
    public async Task EventStatusCapability_RegistersTwoScopedQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetEventStatusDetailsRequest), typeof(GetEventStatusDetailsRequestHandler),
            typeof(GetEventStatusListRequest), typeof(GetEventStatusListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetEventStatusDetailsRequest), typeof(GetEventStatusListRequest) });
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetEventStatusDetailsRequestHandler)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method => method.ReturnType == typeof(Task<EventStatusDto>));
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
