using Explore.Application.Configuration;

namespace Event.Application.UnitTests.Configuration;

public sealed class AgentBrowserProfileSettingsTests
{
    private readonly AgentBrowserProfileSettings _settings = AgentBrowserProfileSettings.Create();

    [Test]
    public async Task ProfileUsesExplicitSplitLocalPostgresTopology()
    {
        await Assert.That(_settings.Mode).IsEqualTo("AgentBrowser");
        await Assert.That(_settings.SeedEnabled).IsTrue();
        await Assert.That(_settings.HostingTopology).IsEqualTo("Split");
        await Assert.That(_settings.IdentityDatabaseTopology).IsEqualTo("colocated");
        await Assert.That(_settings.AuthenticationProvider).IsEqualTo("local");
        await Assert.That(_settings.AuthorizationProvider).IsEqualTo("local");
        await Assert.That(_settings.DatabaseProvider).IsEqualTo("PostgreSql");
        await Assert.That(_settings.DatabaseName).IsEqualTo("islamu_event_agent");
        await Assert.That(_settings.ConfigurationManifestMode).IsEqualTo("Off");
        await Assert.That(_settings.DeploymentMode).IsEqualTo("multi_tenant");
        await Assert.That(_settings.PrivacyErasureTopology).IsEqualTo("EmbeddedSqlite");
        await Assert.That(_settings.WebhookProvider).IsEqualTo("Local");
    }

    [Test]
    public async Task ProfileUsesStableLoopbackBrowserAndMailPorts()
    {
        await Assert.That(_settings.ApiPort).IsEqualTo(5100);
        await Assert.That(_settings.BffPort).IsEqualTo(5200);
        await Assert.That(_settings.MailpitUiPort).IsEqualTo(58025);
        await Assert.That(_settings.MailpitSmtpPort).IsEqualTo(51025);
        await Assert.That(_settings.LoopbackHost).IsEqualTo("127.0.0.1");
        await Assert.That(_settings.ApiLoopbackUrl).IsEqualTo("http://127.0.0.1:5100");
        await Assert.That(_settings.BffLoopbackUrl).IsEqualTo("http://127.0.0.1:5200");
        await Assert.That(_settings.AdminOrigin).IsEqualTo("http://admin.localhost:5200");
        await Assert.That(_settings.DefaultTenantOrigin).IsEqualTo("http://default.localhost:5200");
        await Assert.That(_settings.NegativeTenantOrigin).IsEqualTo("http://agent-negative.localhost:5200");
    }

    [Test]
    public async Task ProfileKeepsEveryDurableResourceSeparateFromOrdinaryDevelopment()
    {
        await Assert.That(_settings.PostgresVolumeName).IsEqualTo("islamu-event-agent-postgres-data");
        await Assert.That(_settings.RedisVolumeName).IsEqualTo("islamu-event-agent-redis-data");
        await Assert.That(_settings.MailpitVolumeName).IsEqualTo("islamu-event-agent-mailpit-data");
        await Assert.That(_settings.PrivacyErasureVolumeName).IsEqualTo("islamu-event-agent-privacy-erasure-authority-data");
        await Assert.That(_settings.LocalStorageRelativePath).IsEqualTo("storage-data/aspire-agent");
        await Assert.That(_settings.PrivacyErasureRelativePath)
            .IsEqualTo("privacy-erasure-authority-data/aspire-agent/privacy_erasure_authority.db");
        await Assert.That(_settings.PostgresUsernameConfigurationKey).IsEqualTo("POSTGRESQL_USERNAME");
        await Assert.That(_settings.PostgresPasswordConfigurationKey).IsEqualTo("POSTGRESQL_PASSWORD");
        await Assert.That(_settings.RedisPasswordConfigurationKey).IsEqualTo("AGENT_BROWSER_REDIS_PASSWORD");
    }

    [Test]
    public async Task ProfileDoesNotSelectSecretsOrRequirePlatformServices()
    {
        await Assert.That(_settings.SelectsSecretAuthority).IsFalse();
        await Assert.That(_settings.IncludesPlatformResources).IsFalse();
        await Assert.That(_settings.IncludesMessaging).IsFalse();
        await Assert.That(_settings.InstanceBootstrapMode).IsEqualTo("ConfiguredAdministrator");
        await Assert.That(_settings.InstanceBootstrapAdminProvider).IsEqualTo("local");
        await Assert.That(_settings.InstanceBootstrapAdminSubject).IsEqualTo("01998880-0000-7000-8000-000000000101");
        await Assert.That(_settings.InstanceBootstrapAdminEmail).IsEqualTo("admin@agent.example.test");
        await Assert.That(_settings.InstanceBootstrapBindingGeneration).IsEqualTo("1");
    }
}
