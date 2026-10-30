using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Architecture.Tests;

public sealed class NativeTenantStorageOperationTests
{
    [Test]
    public async Task StorageOperations_ExposeOnlyOneReadAndTwoMutationPorts()
    {
        Type[] cohort = typeof(GetTenantStorageSettingsQuery).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features.TenantStorageSettings.",
                StringComparison.Ordinal) == true).ToArray();
        var services = new ServiceCollection();
        services.AddNativeOperations(cohort);
        Type[] ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(3);
        await Assert.That(ports.Single(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .GetGenericArguments()[0]).IsEqualTo(typeof(GetTenantStorageSettingsQuery));
        await Assert.That(ports.Count(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && type.GetGenericArguments()[0] == typeof(PatchTenantStorageSettingsCommand))).IsEqualTo(1);
        await Assert.That(ports.Count(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && type.GetGenericArguments()[0] == typeof(TestTenantStorageProviderCommand))).IsEqualTo(1);
        await Assert.That(cohort.Any(type => type.GetInterfaces().Any(contract => contract.Namespace == "MediatR"))).IsFalse();
    }
}
