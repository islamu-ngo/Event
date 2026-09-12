using System.Reflection;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Languages.Handlers.Queries;
using Explore.Application.Features.Languages.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeLanguageOperationTests
{
    [Test]
    public async Task LanguageReads_RegisterBothClosedNativeQueryPorts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(GetLanguageDetailsRequest), typeof(GetLanguageDetailsRequestHandler),
            typeof(GetLanguageListRequest), typeof(GetLanguageListRequestHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(GetLanguageDetailsRequest), typeof(GetLanguageListRequest) });
    }

    [Test]
    public async Task MissingDetail_HasANullableNativeResultContract()
    {
        var method = typeof(GetLanguageDetailsRequestHandler)
            .GetMethod(nameof(GetLanguageDetailsRequestHandler.QueryAsync))!;
        var result = new NullabilityInfoContext().Create(method.ReturnParameter)
            .GenericTypeArguments.Single();
        await Assert.That(result.ReadState).IsEqualTo(NullabilityState.Nullable);
    }
}
