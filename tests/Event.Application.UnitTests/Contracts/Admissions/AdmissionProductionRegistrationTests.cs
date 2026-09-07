using Explore.Application;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApplicationUnitTests.Contracts.Admissions;

public sealed class AdmissionProductionRegistrationTests
{
    [Test]
    public async Task RealApplicationRegistrationBindsAdmissionOptionsAndService()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AdmissionCredentialOptions.SectionName}:ActiveKeyVersion"] = "9"
            })
            .Build();
        var services = new ServiceCollection();

        services.ConfigureApplicationServices(configuration);

        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType == typeof(AdmissionIssuanceService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        using ServiceProvider provider = services.BuildServiceProvider();
        await Assert.That(provider.GetRequiredService<IOptions<AdmissionCredentialOptions>>().Value.ActiveKeyVersion)
            .IsEqualTo(9);
    }

    [Test]
    public async Task RegistrationBindsScannerOptionsAndRegistersOneAuthenticationImplementation()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{AdmissionScannerCapabilityDigestOptions.SectionName}:ActiveKeyVersion"] = "7",
                [$"{AdmissionScannerCapabilityDigestOptions.SectionName}:RetainedKeyVersions:0"] = "6"
            })
            .Build();
        var services = new ServiceCollection();

        services.ConfigureApplicationServices(configuration);

        await Assert.That(services.Count(descriptor =>
            descriptor.ServiceType == typeof(IAdmissionScannerAuthenticationService))).IsEqualTo(1);
        using ServiceProvider provider = services.BuildServiceProvider();
        AdmissionScannerCapabilityDigestOptions options = provider
            .GetRequiredService<IOptions<AdmissionScannerCapabilityDigestOptions>>().Value;
        await Assert.That(options.ActiveKeyVersion).IsEqualTo(7);
        await Assert.That(options.RetainedKeyVersions).IsEquivalentTo([6]);
    }

    [Test]
    public async Task CredentialOptionsValidationUsesBoundedDigestKeyVersions()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{AdmissionCredentialOptions.SectionName}:ActiveKeyVersion"] = "9"
        };
        foreach (int version in Enumerable.Range(1, AdmissionCredentialOptions.MaximumKeyVersions))
        {
            settings[$"{AdmissionCredentialOptions.SectionName}:RetainedKeyVersions:{version - 1}"] =
                version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        await Assert.That(() =>
            provider.GetRequiredService<IOptions<AdmissionCredentialOptions>>().Value)
            .Throws<OptionsValidationException>();
    }
}
