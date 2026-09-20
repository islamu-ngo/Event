using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ActorTypes.Handlers.Queries;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeActorTypeOperationTests
{
    [Test]
    public async Task ActorTypeReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetActorTypeDetailsRequest), typeof(GetActorTypeDetailsRequestHandler),
            typeof(GetActorTypeListRequest), typeof(GetActorTypeListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetActorTypeDetailsRequest), typeof(GetActorTypeListRequest) });
    }

    [Test]
    public async Task MissingDetail_HasANullableNativeResultContract()
    {
        var method = typeof(GetActorTypeDetailsRequestHandler)
            .GetMethod(nameof(GetActorTypeDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
