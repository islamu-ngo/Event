using Explore.Application;
using Explore.Application.Configuration;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Configuration;

public sealed class PrivacyErasureModelCompositionTests
{
    [Test]
    public async Task EmbeddedSqliteComposition_PoisonExternalConnectionProviderIsNeverRead()
    {
        var provider = new PoisonExternalConnectionConfigurationProvider();
        using var configuration = new ConfigurationRoot([provider]);
        var services = new ServiceCollection();

        services.ConfigureApplicationServices(configuration);
        PrivacyErasureDurabilityOptions options =
            PrivacyErasureDurabilityOptions.FromConfiguration(configuration);

        await Assert.That(options.Topology)
            .IsEqualTo(PrivacyErasureAuthorityTopology.EmbeddedSqlite);
        await Assert.That(provider.ExternalConnectionReadCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("ExternalDatabase")]
    [Arguments("CoLocated")]
    [Arguments("EmbeddedSqlite")]
    public async Task BothTopologies_RegisterExactlyOneAuthorityFirstWorkflow(string topology)
    {
        var values = new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = topology
        };
        if (topology == "ExternalDatabase")
        {
            values["ConnectionStrings:PrivacyErasureAuthority"] =
                "Host=unused;Database=unused;Username=unused";
        }

        var services = new ServiceCollection();
        services.ConfigureApplicationServices(Build(values));

        ServiceDescriptor[] workflows = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPrivacyErasureService))
            .ToArray();
        await Assert.That(workflows.Length).IsEqualTo(1);
        await Assert.That(workflows[0].ImplementationType)
            .IsEqualTo(typeof(RetainedAuthorityPrivacyErasureWorkflow));
    }

    [Test]
    public async Task EmbeddedSqliteComposition_DoesNotRegisterFallbackAuthorityAdapter()
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(Build(new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite"
        }));

        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType.FullName ==
            "Explore.Application.Contracts.PrivacyErasure.IPrivacyErasureAuthority")).IsFalse();
    }

    [Test]
    public async Task CoLocatedTopology_Composition_DoesNotRegisterEmbeddedAuthority()
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(Build(new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = "CoLocated"
        }));

        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType.FullName ==
            "Explore.Application.Contracts.PrivacyErasure.IPrivacyErasureAuthority")).IsFalse();
    }

    [Test]
    public async Task AuthorityMaintenanceContract_SeparatesDryRunFromApply()
    {
        string[] methodNames = typeof(IPrivacyErasureAuthorityMaintenance)
            .GetMethods()
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(methodNames).IsEquivalentTo([
            "CompactExpiredIntentsAsync",
            "EvaluateRetentionAsync"]);
    }

    [Test]
    public async Task AuthorityMaintenanceRequest_RequiresAnExplicitPiiFreeHoldSet()
    {
        await Assert.That(() => new PrivacyErasureRetentionRequest(
                DateTime.UtcNow,
                100,
                null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ConfigurationDataSurface_ReportsOnlyTopologyAndWorkflowType()
    {
        var provider = new PoisonExternalConnectionConfigurationProvider();
        using var configuration = new ConfigurationRoot([provider]);
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(configuration);

        PrivacyErasureDurabilityOptions options =
            PrivacyErasureDurabilityOptions.FromConfiguration(configuration);
        ServiceDescriptor workflow = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IPrivacyErasureService));
        await Assert.That(provider.ExternalConnectionReadCount).IsEqualTo(0);
        Microsoft.Extensions.Options.OptionsValidationException? legacyFailure =
            await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            Task.FromResult(PrivacyErasureDurabilityOptions.FromConfiguration(
                Build(new Dictionary<string, string?>
                {
                    ["PrivacyErasure:Durability:Mode"] = "secret-canary"
                }))));
        await Assert.That(legacyFailure!.Failures.Single()).DoesNotContain("secret-canary");

        Console.WriteLine(
            $"topology={options.Topology};workflow={workflow.ImplementationType!.Name}");
    }

    private static IConfiguration Build(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class PoisonExternalConnectionConfigurationProvider : ConfigurationProvider
    {
        private const string ExternalConnectionKey =
            "ConnectionStrings:PrivacyErasureAuthority";

        private int _externalConnectionReadCount;

        public int ExternalConnectionReadCount => Volatile.Read(ref _externalConnectionReadCount);

        public override bool TryGet(string key, out string? value)
        {
            ThrowIfExternalConnectionKey(key);
            value = key.Equals(
                "PrivacyErasure:Authority:Topology",
                StringComparison.OrdinalIgnoreCase)
                ? "EmbeddedSqlite"
                : null;
            return value is not null;
        }

        public override IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath)
        {
            ThrowIfExternalConnectionKey(parentPath);
            return earlierKeys;
        }

        private void ThrowIfExternalConnectionKey(string? key)
        {
            if (key is null
                || (!key.Equals(ExternalConnectionKey, StringComparison.OrdinalIgnoreCase)
                    && !key.StartsWith($"{ExternalConnectionKey}:", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            Interlocked.Increment(ref _externalConnectionReadCount);
            throw new InvalidOperationException("EmbeddedSqlite composition read the external authority connection.");
        }
    }
}
