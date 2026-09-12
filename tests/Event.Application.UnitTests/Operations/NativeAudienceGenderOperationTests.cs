using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.AudienceGenders.Handlers.Queries;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAudienceGenderOperationTests
{
    [Test]
    public async Task AudienceGenderReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetAudienceGenderDetailsRequest), typeof(GetAudienceGenderDetailsRequestHandler),
            typeof(GetAudienceGenderListRequest), typeof(GetAudienceGenderListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetAudienceGenderDetailsRequest), typeof(GetAudienceGenderListRequest) });
    }

    [Test]
    public async Task MissingDetail_HasANullableNativeResultContract()
    {
        var method = typeof(GetAudienceGenderDetailsRequestHandler)
            .GetMethod(nameof(GetAudienceGenderDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
