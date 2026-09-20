using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeTenantSettingsDocumentOperationTests
{
    [Test]
    public async Task Discovery_RegistersThreeCommandsAndOneReadOnlyQueryExclusively()
    {
        Type[] cohort = typeof(PatchTenantBrandingSettingsDocumentCommand).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Explore.Application.Features.TenantSettingsDocuments.", StringComparison.Ordinal) == true)
            .ToArray();
        Type[] requests = cohort.Where(type => type.Namespace?.Contains(".Requests.", StringComparison.Ordinal) == true).ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        Type[] ports = services.Where(descriptor => !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ||
                 type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .ToArray();

        await Assert.That(requests.Length).IsEqualTo(4);
        await Assert.That(ports.Select(port => port.GetGenericArguments()[0])).IsEquivalentTo(requests);
        await Assert.That(ports.Count(port => port.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(3);
        await Assert.That(ports.Count(port => port.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))).IsEqualTo(1);
        foreach (Type port in ports)
        {
            await Assert.That(port.GetGenericArguments()[0].GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == port).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
