using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.DidCustodyTypes.Handlers.Queries;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeDidCustodyTypeOperationTests
{
    [Test]
    public async Task CustodyTypeReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetDidCustodyTypeDetailsRequest), typeof(GetDidCustodyTypeDetailsRequestHandler),
            typeof(GetDidCustodyTypeListRequest), typeof(GetDidCustodyTypeListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetDidCustodyTypeDetailsRequest), typeof(GetDidCustodyTypeListRequest) });
    }

    [Test]
    public async Task MissingDetail_HasANullableNativeResultContract()
    {
        var method = typeof(GetDidCustodyTypeDetailsRequestHandler)
            .GetMethod(nameof(GetDidCustodyTypeDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
