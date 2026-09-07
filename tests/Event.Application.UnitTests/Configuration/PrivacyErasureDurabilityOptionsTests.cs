using Explore.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Application.UnitTests.Configuration;

public sealed class PrivacyErasureDurabilityOptionsTests
{
    [Test]
    public async Task AbsentConfiguration_DefaultsToEmbeddedSqlite()
    {
        PrivacyErasureDurabilityOptions options = Resolve(
            new Dictionary<string, string?>());

        await Assert.That(options.Topology)
            .IsEqualTo(PrivacyErasureAuthorityTopology.EmbeddedSqlite);
    }

    [Test]
    [Arguments("EmbeddedSqlite", PrivacyErasureAuthorityTopology.EmbeddedSqlite)]
    [Arguments("embeddedsqlite", PrivacyErasureAuthorityTopology.EmbeddedSqlite)]
    [Arguments("ExternalDatabase", PrivacyErasureAuthorityTopology.ExternalDatabase)]
    [Arguments("externaldatabase", PrivacyErasureAuthorityTopology.ExternalDatabase)]
    [Arguments("CoLocated", PrivacyErasureAuthorityTopology.CoLocated)]
    [Arguments("colocated", PrivacyErasureAuthorityTopology.CoLocated)]
    public async Task SupportedTopologyName_IsAccepted(
        string configured,
        PrivacyErasureAuthorityTopology expected)
    {
        var values = new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = configured
        };

        await Assert.That(Resolve(values).Topology).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("Automatic")]
    [Arguments("0")]
    [Arguments("1")]
    [Arguments("ApplicationDatabase")]
    [Arguments("RetainedAuthority")]
    [Arguments("None")]
    [Arguments("none")]
    public async Task UnsupportedTopologyName_FailsValidation(string configured)
    {
        await Assert.ThrowsAsync<OptionsValidationException>(() =>
            Task.FromResult(Resolve(new Dictionary<string, string?>
            {
                ["PrivacyErasure:Authority:Topology"] = configured
            })));
    }

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("ApplicationDatabase")]
    [Arguments("RetainedAuthority")]
    public async Task PresentLegacyMode_FailsWithResetOnlyReplacementGuidance(string legacyMode)
    {
        OptionsValidationException? exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            Task.FromResult(Resolve(new Dictionary<string, string?>
            {
                ["PrivacyErasure:Durability:Mode"] = legacyMode,
                ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite"
            })));

        await Assert.That(exception!.Failures.Single())
            .Contains("PrivacyErasure:Durability:Mode", StringComparison.Ordinal);
        await Assert.That(exception.Failures.Single())
            .Contains("PrivacyErasure:Authority:Topology", StringComparison.Ordinal);
        await Assert.That(exception.Failures.Single())
            .Contains("reset", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Failures.Single())
            .Contains("eligible", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Failures.Single())
            .Contains("backup", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Failures.Single())
            .Contains("export", StringComparison.OrdinalIgnoreCase);
        await Assert.That(exception.Failures.Single())
            .Contains("operator", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(legacyMode))
        {
            await Assert.That(exception.Failures.Single()).DoesNotContain(legacyMode);
        }
    }

    [Test]
    public async Task Topology_DerivesRestoreReplayProtectionCapability()
    {
        PrivacyErasureDurabilityOptions embedded = Resolve(new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite"
        });
        PrivacyErasureDurabilityOptions external = Resolve(new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = "ExternalDatabase"
        });
        PrivacyErasureDurabilityOptions colocated = Resolve(new Dictionary<string, string?>
        {
            ["PrivacyErasure:Authority:Topology"] = "CoLocated"
        });
        await Assert.That(embedded.RestoreReplayProtection).IsTrue();
        await Assert.That(external.RestoreReplayProtection).IsTrue();
        await Assert.That(colocated.RestoreReplayProtection).IsFalse();
    }

    private static PrivacyErasureDurabilityOptions Resolve(
        IDictionary<string, string?> values) =>
        PrivacyErasureDurabilityOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
}
