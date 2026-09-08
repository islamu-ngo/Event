using Explore.Application.Contracts.PrivacyErasure;
using Explore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Privacy;

public sealed class PrivacyErasureAuthorityOwnershipTests
{
    [Test]
    public async Task InfrastructureAssembly_HasNoAuthorityStorageAdapterOrSchemaResource()
    {
        var assembly = typeof(InfrastructureServicesRegistration).Assembly;

        await Assert.That(assembly.GetTypes().Select(type => type.FullName ?? string.Empty))
            .DoesNotContain(typeName => typeName.Contains(
                ".Privacy.ErasureAuthority.",
                StringComparison.Ordinal));
        await Assert.That(assembly.GetManifestResourceNames())
            .DoesNotContain(resourceName => resourceName.Contains(
                "PrivacyErasureAuthoritySchema",
                StringComparison.Ordinal));
    }

    [Test]
    public async Task InfrastructureComposition_DoesNotRegisterAuthorityStorage()
    {
        var services = new ServiceCollection();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());

        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureAuthority))).IsFalse();
    }
}
