using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionStatus;
using Explore.Application.Features.EventSessionStatuses.Handlers.Queries;
using Explore.Application.Features.EventSessionStatuses.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventSessionStatusOperationTests
{
    [Test]
    public async Task Catalogue_RegistersExactlyTwoScopedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetEventSessionStatusDetailsQuery), typeof(GetEventSessionStatusDetailsQueryHandler),
            typeof(GetEventSessionStatusListQuery), typeof(GetEventSessionStatusListQueryHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Select(port => port.ServiceType.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetEventSessionStatusDetailsQuery), typeof(GetEventSessionStatusListQuery) });
    }

    [Test]
    public async Task MissingDetail_DeclaresNullableHandlerResult()
    {
        var method = typeof(GetEventSessionStatusDetailsQueryHandler)
            .GetMethod(nameof(GetEventSessionStatusDetailsQueryHandler.QueryAsync))
            ?? throw new InvalidOperationException("Expected the detail query handler entry point.");
        var result = new NullabilityInfoContext().Create(method.ReturnParameter).GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }

    [Test]
    public async Task Queries_UseOnlyTheirExactNativeResultContract()
    {
        await Assert.That(typeof(GetEventSessionStatusDetailsQuery).GetInterfaces())
            .Contains(typeof(IQuery<EventSessionStatusDto>));
        await Assert.That(typeof(GetEventSessionStatusListQuery).GetInterfaces())
            .Contains(typeof(IQuery<List<EventSessionStatusListDto>>));
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(typeof(GetEventSessionStatusDetailsQuery))).IsFalse();
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(typeof(GetEventSessionStatusListQuery))).IsFalse();
    }
}
