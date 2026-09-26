namespace Explore.Application.Configuration;

/// <summary>
/// Compiled, non-secret topology for the isolated local agent browser profile.
/// </summary>
public sealed record AgentBrowserProfileSettings
{
    private AgentBrowserProfileSettings()
    {
    }

    public static AgentBrowserProfileSettings Create() => new();

    public string Mode { get; } = "AgentBrowser";
    public bool SeedEnabled { get; } = true;
    public string HostingTopology { get; } = "Split";
    public string IdentityDatabaseTopology { get; } = "colocated";
    public string AuthenticationProvider { get; } = "local";
    public string AuthorizationProvider { get; } = "local";
    public string DatabaseProvider { get; } = "PostgreSql";
    public string DatabaseName { get; } = "islamu_event_agent";
    public string ConfigurationManifestMode { get; } = "Off";
    public string DeploymentMode { get; } = "multi_tenant";
    public string PrivacyErasureTopology { get; } = "EmbeddedSqlite";
    public string WebhookProvider { get; } = "Local";
    public string InstanceBootstrapMode { get; } = "ConfiguredAdministrator";
    public string InstanceBootstrapAdminProvider { get; } = "local";
    public string InstanceBootstrapAdminSubject { get; } = "01998880-0000-7000-8000-000000000101";
    public string InstanceBootstrapAdminEmail { get; } = "admin@agent.example.test";
    public string InstanceBootstrapBindingGeneration { get; } = "1";
    public int ApiPort { get; } = 5100;
    public int BffPort { get; } = 5200;
    public int MailpitUiPort { get; } = 58025;
    public int MailpitSmtpPort { get; } = 51025;
    public string LoopbackHost { get; } = "127.0.0.1";
    public string ApiLoopbackUrl { get; } = "http://127.0.0.1:5100";
    public string BffLoopbackUrl { get; } = "http://127.0.0.1:5200";
    public string AdminOrigin { get; } = "http://admin.localhost:5200";
    public string DefaultTenantOrigin { get; } = "http://default.localhost:5200";
    public string NegativeTenantOrigin { get; } = "http://agent-negative.localhost:5200";
    public string PostgresVolumeName { get; } = "islamu-event-agent-postgres-data";
    public string RedisVolumeName { get; } = "islamu-event-agent-redis-data";
    public string MailpitVolumeName { get; } = "islamu-event-agent-mailpit-data";
    public string PrivacyErasureVolumeName { get; } = "islamu-event-agent-privacy-erasure-authority-data";
    public string LocalStorageRelativePath { get; } = "storage-data/aspire-agent";
    public string PrivacyErasureRelativePath { get; } = "privacy-erasure-authority-data/aspire-agent/privacy_erasure_authority.db";
    public string PostgresUsernameConfigurationKey { get; } = "POSTGRESQL_USERNAME";
    public string PostgresPasswordConfigurationKey { get; } = "POSTGRESQL_PASSWORD";
    public string RedisPasswordConfigurationKey { get; } = "AGENT_BROWSER_REDIS_PASSWORD";
    public bool IncludesPlatformResources { get; } = false;
    public bool IncludesMessaging { get; } = false;
    public bool SelectsSecretAuthority { get; } = false;
}
