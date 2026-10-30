using Event.Testing.DataProtection;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.Extensions.Configuration;
using Explore.ServiceDefaults.Secrets;

namespace Explore.Infrastructure.Tests.Secrets;

[NotInParallel]
public sealed class ExternalDataProtectionSourceTests
{
    [Test]
    public async Task EnvironmentUsesOnlyItsOwnPartitionAndNeverApplicationConfigurationFallback()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new EnvironmentScope(new Dictionary<string, string?>
        {
            ["EVENT_DP_API_RING"] = authority.Input.Ring,
            ["EVENT_DP_API_ADMISSION"] = authority.Input.Admission,
            ["EVENT_DP_API_ADMISSION_SHA256"] = authority.Input.AdmissionSha256,
            ["EVENT_DP_API_MINIMUM_GENERATION"] = "1"
        });
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["EVENT_DP_API_RING"] = authority.Input.Ring,
            ["EVENT_DP_API_ADMISSION"] = authority.Input.Admission
        }).Build();
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            configuration, "Production", SecretConsumerRole.Api,
            [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt());
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await source.PrepareAsync();
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        await Assert.That(provider.KeyCount).IsEqualTo(1);
        Environment.SetEnvironmentVariable("EVENT_DP_API_RING", null);
        await Assert.That(async () => await source.ReadFreshAsync()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SelectedUserSecretsIsRejectedInProductionDespiteValidLowerEnvironment()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new EnvironmentScope(new Dictionary<string, string?>
        {
            ["EVENT_DP_API_RING"] = authority.Input.Ring,
            ["EVENT_DP_API_ADMISSION"] = authority.Input.Admission,
            ["EVENT_DP_API_ADMISSION_SHA256"] = authority.Input.AdmissionSha256,
            ["EVENT_DP_API_MINIMUM_GENERATION"] = "1"
        });
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "UserSecrets"
        }).Build();
        await Assert.That(async () => await SecretAuthorityBootstrap.OpenAsync(
            configuration, "Production", SecretConsumerRole.Api,
            [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt()))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CandidateConfigurationCannotSupplyIndependentDeploymentPin()
    {
        var authority = new ExternalDataProtectionTestAuthority("api");
        using var environment = new EnvironmentScope(new Dictionary<string, string?>
        {
            ["EVENT_DP_API_RING"] = authority.Input.Ring,
            ["EVENT_DP_API_ADMISSION"] = authority.Input.Admission,
            ["EVENT_DP_API_ADMISSION_SHA256"] = null,
            ["EVENT_DP_API_MINIMUM_GENERATION"] = "1"
        });
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["EVENT_DP_API_ADMISSION_SHA256"] = authority.Input.AdmissionSha256
        }).Build();
        await using var owner = await SecretAuthorityBootstrap.OpenAsync(
            configuration, "Production", SecretConsumerRole.Api,
            [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt());
        var source = new ExternalDataProtectionSource(owner, new("api"));
        await Assert.That(async () => await source.PrepareAsync()).Throws<InvalidOperationException>();
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = [];
        public EnvironmentScope(Dictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                previous.Add(pair.Key, Environment.GetEnvironmentVariable(pair.Key));
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
        public void Dispose()
        {
            foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
