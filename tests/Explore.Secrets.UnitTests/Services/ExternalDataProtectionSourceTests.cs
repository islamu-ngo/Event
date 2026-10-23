using Event.Testing.DataProtection;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Explore.Secrets.UnitTests.Services;

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
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["EVENT_DP_API_RING"] = authority.Input.Ring,
            ["EVENT_DP_API_ADMISSION"] = authority.Input.Admission
        }).Build();
        var source = new ExternalDataProtectionSource(configuration, new HostEnvironment(), new("api"));
        using var provider = ExternalDataProtectionProvider.Create(source.Read(), "api", TimeProvider.System);
        await Assert.That(provider.KeyCount).IsEqualTo(1);
        Environment.SetEnvironmentVariable("EVENT_DP_API_RING", null);
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
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
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "UserSecrets"
        }).Build();
        var source = new ExternalDataProtectionSource(configuration, new HostEnvironment(), new("api"));
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
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
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SECRET_PROVIDER"] = "Environment",
            ["EVENT_DP_API_ADMISSION_SHA256"] = authority.Input.AdmissionSha256
        }).Build();
        var source = new ExternalDataProtectionSource(configuration, new HostEnvironment(), new("api"));
        await Assert.That(() => source.Read()).Throws<InvalidOperationException>();
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

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "DataProtectionSourceTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
