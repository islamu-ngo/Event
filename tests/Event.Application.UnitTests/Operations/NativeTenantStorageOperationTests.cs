using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Handlers.Queries;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeTenantStorageOperationTests
{
    [Test]
    public async Task StorageOperations_ExposeOnlyOneReadAndTwoMutationPorts()
    {
        var assembly = typeof(GetTenantStorageSettingsQuery).Assembly;
        var cohort = assembly.GetTypes().Where(type => type.Namespace?.StartsWith(
            "Explore.Application.Features.TenantStorageSettings.", StringComparison.Ordinal) == true).ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(3);
        await Assert.That(ports.Count(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsEqualTo(2);
        await Assert.That(ports.Single(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .GetGenericArguments()[0]).IsEqualTo(typeof(GetTenantStorageSettingsQuery));
        await Assert.That(cohort.Any(type => typeof(MediatR.IBaseRequest).IsAssignableFrom(type))).IsFalse();
        await Assert.That(assembly.GetType("Explore.Application.Features.TenantStorageSettings.Requests.Queries.TestTenantStorageProviderQuery"))
            .IsNull();
    }
}
