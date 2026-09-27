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
        ConfigurationManifestMode: "Off",
        PrivacyErasureTopology: "EmbeddedSqlite",
        WebhookProvider: "Local");

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
        await Assert.That(() => (Admitted with { DatabaseName = "islamu_event_db" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
        await Assert.That(() => (Admitted with { ConfigurationManifestMode = "Import" }).EnsureAdmitted())
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("CoLocated", "Local")]
    [Arguments("ExternalDatabase", "Local")]
    [Arguments("EmbeddedSqlite", "Svix")]
    [Arguments("EmbeddedSqlite", "Composite")]
    public async Task AgentBrowserRejectsExternalErasureOrWebhookProvider(
        string erasureTopology, string webhookProvider)
    {
        await Assert.That(() => (Admitted with
        {
            PrivacyErasureTopology = erasureTopology,
            WebhookProvider = webhookProvider
        }).EnsureAdmitted()).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StructuredLocalTopologyDoesNotMaskConflictingFlatSelectors()
    {
        await Assert.That(() => Admitted.EnsureAdmitted(erasureDatabaseTopology: "CoLocated"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => Admitted.EnsureAdmitted(flatWebhookProvider: "Svix"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Sqlite")]
    [Arguments("SqlServer")]
    [Arguments("MariaDb")]
    [Arguments("MySql")]
    public async Task AgentBrowserRequiresPostgreSqlProvider(string databaseProvider)
    {
        await Assert.That(() => (Admitted with { DatabaseProvider = databaseProvider }).EnsureAdmitted())
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
            Admitted with { PrivacyErasureTopology = "" },
            Admitted with { WebhookProvider = "" },
        ];
        foreach (AgentBrowserProvisioningOptions options in invalid)
        {
            await Assert.That(() => options.EnsureAdmitted()).Throws<InvalidOperationException>();
        }
    }
}
