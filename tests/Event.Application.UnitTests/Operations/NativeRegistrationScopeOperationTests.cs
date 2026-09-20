using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationScope;
using Explore.Application.Features.RegistrationScopes.Handlers.Queries;
using Explore.Application.Features.RegistrationScopes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeRegistrationScopeOperationTests
{
    [Test]
    public async Task RegistrationScopeCatalogue_RegistersItsScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetRegistrationScopeListRequest), typeof(GetRegistrationScopeListRequestHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments())
            .IsEquivalentTo(new[] { typeof(GetRegistrationScopeListRequest), typeof(List<RegistrationScopeListDto>) });
    }
}
