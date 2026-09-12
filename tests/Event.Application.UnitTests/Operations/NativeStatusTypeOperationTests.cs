using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.StatusType;
using Explore.Application.Features.StatusTypes.Handlers.Queries;
using Explore.Application.Features.StatusTypes.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeStatusTypeOperationTests
{
    [Test]
    public async Task StatusTypeCatalogue_RegistersItsScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetStatusTypeListRequest), typeof(GetStatusTypeListRequestHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments())
            .IsEquivalentTo(new[] { typeof(GetStatusTypeListRequest), typeof(List<StatusTypeListDto>) });
    }
}
