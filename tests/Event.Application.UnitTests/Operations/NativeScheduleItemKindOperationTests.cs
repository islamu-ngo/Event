using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.ScheduleItemKind;
using Explore.Application.Features.ScheduleItemKinds.Handlers.Queries;
using Explore.Application.Features.ScheduleItemKinds.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeScheduleItemKindOperationTests
{
    [Test]
    public async Task ScheduleItemKindCatalogue_RegistersItsScopedNativeQuery()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetScheduleItemKindListRequest), typeof(GetScheduleItemKindListRequestHandler)]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments())
            .IsEquivalentTo(new[] { typeof(GetScheduleItemKindListRequest), typeof(List<ScheduleItemKindListDto>) });
    }
}
