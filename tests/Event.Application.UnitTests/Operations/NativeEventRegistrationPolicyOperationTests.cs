using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventRegistrationPolicy;
using Explore.Application.Features.EventRegistrationPolicies.Handlers.Queries;
using Explore.Application.Features.EventRegistrationPolicies.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEventRegistrationPolicyOperationTests
{
    [Test]
    public async Task EventRegistrationPolicyCatalogue_RegistersItsScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetEventRegistrationPolicyListRequest), typeof(GetEventRegistrationPolicyListRequestHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments())
            .IsEquivalentTo(new[] { typeof(GetEventRegistrationPolicyListRequest), typeof(List<EventRegistrationPolicyListDto>) });
    }
}
