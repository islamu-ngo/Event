using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.FileTypes.Handlers.Queries;
using Explore.Application.Features.FileTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeFileTypeOperationTests
{
    [Test]
    public async Task FileTypeReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetFileTypeDetailsRequest), typeof(GetFileTypeDetailsRequestHandler),
            typeof(GetFileTypeListRequest), typeof(GetFileTypeListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetFileTypeDetailsRequest), typeof(GetFileTypeListRequest) });
    }

    [Test]
    public async Task MissingDetail_HasANullableNativeResultContract()
    {
        var method = typeof(GetFileTypeDetailsRequestHandler)
            .GetMethod(nameof(GetFileTypeDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
