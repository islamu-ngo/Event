using Explore.Application;
using System.Reflection;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Handlers.Queries;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAudienceAgeOperationTests
{
    [Test]
    public async Task MissingDetail_DeclaresNullableNativeQueryResult()
    {
        var method = typeof(GetAudienceAgeDetailsRequestHandler)
            .GetMethod(nameof(GetAudienceAgeDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();

        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }

    [Test]
    public async Task AudienceAgeReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetAudienceAgeDetailsRequest), typeof(GetAudienceAgeDetailsRequestHandler),
            typeof(GetAudienceAgeListRequest), typeof(GetAudienceAgeListRequestHandler)
        ]);

        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        var detail = ports.Single(type => type.GetGenericArguments()[0] == typeof(GetAudienceAgeDetailsRequest));
        var list = ports.Single(type => type.GetGenericArguments()[0] == typeof(GetAudienceAgeListRequest));
        await Assert.That(detail.GetGenericArguments()[1]).IsEqualTo(typeof(AudienceAgeDto));
        await Assert.That(list.GetGenericArguments()[1]).IsEqualTo(typeof(List<AudienceAgeListDto>));
    }
}
