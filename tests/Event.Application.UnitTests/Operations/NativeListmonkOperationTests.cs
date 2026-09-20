using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Integrations;
using Explore.Application.Features.Integrations.Listmonk.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeListmonkOperationTests
{
    [Test]
    public async Task Listmonk_DiscoveryExposesOnlyTwoQueriesAndTwoCommands()
    {
        Type[] cohort = typeof(GetListmonkIntegrationSettingsQuery).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(
                "Explore.Application.Features.Integrations.Listmonk.", StringComparison.Ordinal) == true)
            .ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        Type[] ports = services.Where(descriptor => !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) ||
                 type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .ToArray();

        await Assert.That(ports.Select(port => port.GetGenericArguments()[0].Name)).IsEquivalentTo(new[]
        {
            "GetListmonkIntegrationSettingsQuery", "TestListmonkConnectionQuery",
            "ResolveIntegrationSyncAmbiguityCommand", "UpdateListmonkIntegrationSettingsCommand"
        });
        foreach (Type port in ports)
        {
            Type request = port.GetGenericArguments()[0];
            bool query = request.Name.EndsWith("Query", StringComparison.Ordinal);
            await Assert.That(port.GetGenericTypeDefinition())
                .IsEqualTo(query ? typeof(IQueryHandler<,>) : typeof(ICommandHandler<,>));
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(
                request == typeof(GetListmonkIntegrationSettingsQuery)
                    ? typeof(ListmonkIntegrationSettingsDto) : typeof(BaseCommandResponse<Guid>));
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == port).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }
}
