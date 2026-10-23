using Explore.Application.Configuration;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalPasswordStartupCompositionTests
{
    [Test]
    [Arguments(null, true)]
    [Arguments("local", true)]
    [Arguments("keycloak", false)]
    [Arguments("atproto", false)]
    public async Task RealStartupComposesFailClosedPasswordPolicyAndProviderSpecificReadiness(string? primary, bool required)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Authentication:Provider"] = primary }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigurePersistenceServices(configuration, skipDbContextRegistration: true,
            skipLookupCacheInitializer: true, environmentName: "Testing");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var validators = scope.ServiceProvider.GetServices<IPasswordValidator<LocalIdentityUser>>().ToArray();
        await Assert.That(validators.Length).IsEqualTo(1);
        await Assert.That(validators[0] is LocalIdentityPasswordValidator).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IPasswordHasher<LocalIdentityUser>>()
            is NormalizedLocalIdentityPasswordHasher).IsTrue();
        var identity = provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        await Assert.That(identity.Password.RequiredLength).IsEqualTo(1);
        var hashing = provider.GetRequiredService<IOptions<PasswordHasherOptions>>().Value;
        await Assert.That(hashing.CompatibilityMode).IsEqualTo(PasswordHasherCompatibilityMode.IdentityV3);
        await Assert.That(hashing.IterationCount).IsEqualTo(new LocalIdentityOptions().PasswordHashIterations);
        await Assert.That(provider.GetRequiredService<LocalPasswordBlocklistSnapshot>().IsUsable).IsFalse();
        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
            .Registrations.Single(row => row.Name == "local-password-policy");
        var health = await registration.Factory(scope.ServiceProvider).CheckHealthAsync(new());
        await Assert.That(health.Status).IsEqualTo(required ? HealthStatus.Unhealthy : HealthStatus.Healthy);
        var rejected = await validators[0].ValidateAsync(null!, new LocalIdentityUser(), new string('x', 20));
        await Assert.That(rejected.Succeeded).IsFalse();
    }
}
