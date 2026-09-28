using Explore.Persistence.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class AgentBrowserAdmissionConfigurationTests
{
    [Test]
    [Arguments("EmbeddedSqlite", "Local", true)]
    [Arguments("CoLocated", "Local", false)]
    [Arguments("ExternalDatabase", "Local", false)]
    [Arguments("EmbeddedSqlite", "Svix", false)]
    [Arguments("EmbeddedSqlite", "Composite", false)]
    public async Task DirectMigratorAdmissionRequiresIsolatedErasureAndLocalWebhooks(
        string erasureTopology, string webhookProvider, bool expected)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AGENT_BROWSER_SEED_ENABLED"] = "true",
                ["ISLAMU_ASPIRE_MODE"] = "AgentBrowser",
                ["Hosting:Topology"] = "Split",
                ["IDENTITY_DATABASE_TOPOLOGY"] = "colocated",
                ["AUTHENTICATION_PROVIDER"] = "local",
                ["AUTHORIZATION_PROVIDER"] = "local",
                ["DATABASE_PROVIDER"] = "PostgreSql",
                ["DATABASE_NAME"] = "islamu_event_agent",
                ["CONFIGURATION_MANIFEST_MODE"] = "Off",
                ["PrivacyErasure:Authority:Topology"] = erasureTopology,
                ["WEBHOOKS_PROVIDER"] = webhookProvider
            }).Build();
        var environment = new HostingEnvironment { EnvironmentName = Environments.Development };

        if (expected)
            await Assert.That(ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(configuration, environment)).IsTrue();
        else
            await Assert.That(() => ExploreDatabaseMigrator.EnsureAgentBrowserAdmission(configuration, environment))
                .Throws<InvalidOperationException>();
    }
}
