using Explore.Application.Configuration;

namespace Event.Application.UnitTests.Configuration;

public sealed class AgentBrowserProvisioningOptionsTests
{
    private static readonly AgentBrowserProvisioningOptions Admitted = new(
        Enabled: true,
        EnvironmentName: "Development",
        Mode: "AgentBrowser",
        HostingTopology: "Split",
        IdentityDatabaseTopology: "colocated",
        AuthenticationProvider: "local",
        AuthorizationProvider: "local",
        DatabaseProvider: "PostgreSql",
        DatabaseName: "islamu_event_agent",
        ConfigurationManifestMode: "Off");

    [Test]
    public async Task DisabledNormalHostIsInertAndNoDefaultEnablesProvisioning()
    {
        await Assert.That(new AgentBrowserProvisioningOptions().Enabled).IsFalse();
        await Assert.That((Admitted with { Enabled = false, Mode = "LocalFull" }).EnsureAdmitted())
            .IsFalse();
    }

    [Test]
    public async Task ExactlyAdmittedProfileCanProvision()
    {
        await Assert.That(Admitted.EnsureAdmitted()).IsTrue();
    }

    [Test]
    public async Task MismatchedEnabledAndModePairsRejectRatherThanFallingThrough()
    {
        await Assert.That(() => (Admitted with { Enabled = false }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { Mode = "LocalFull" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Production")]
    [Arguments("Staging")]
    [Arguments("Testing")]
    public async Task NonDevelopmentEnvironmentsRejectProvisioning(string environmentName)
    {
        await Assert.That(() => (Admitted with { EnvironmentName = environmentName }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StandaloneIsRejectedEvenWhenConfigurationClaimsSplit()
    {
        await Assert.That(() => Admitted.EnsureAdmitted(isStandaloneHost: true))
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { HostingTopology = "Standalone" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExternalIdentityAndNonLocalProvidersAreRejected()
    {
        await Assert.That(() => (Admitted with { IdentityDatabaseTopology = "external" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { AuthenticationProvider = "keycloak" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { AuthorizationProvider = "cerbos" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task WrongDatabaseOrManifestImportIsRejected()
    {
        await Assert.That(() => (Admitted with { DatabaseProvider = "Sqlite" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { DatabaseName = "islamu_event_db" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { ConfigurationManifestMode = "Import" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BlankRequiredAdmissionInputsRejectWithoutEchoingValues()
    {
        AgentBrowserProvisioningOptions[] invalid =
        [
            Admitted with { EnvironmentName = "" },
            Admitted with { HostingTopology = "" },
            Admitted with { IdentityDatabaseTopology = "" },
            Admitted with { AuthenticationProvider = "" },
            Admitted with { AuthorizationProvider = "" },
            Admitted with { DatabaseProvider = "" },
            Admitted with { DatabaseName = "" },
            Admitted with { ConfigurationManifestMode = "" },
        ];
        foreach (AgentBrowserProvisioningOptions options in invalid)
        {
            await Assert.That(() => options.EnsureAdmitted()).Throws<InvalidOperationException>();
        }
    }
}
