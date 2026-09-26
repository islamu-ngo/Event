namespace Explore.Application.Configuration;

/// <summary>Non-secret admission inputs for the isolated development browser profile.</summary>
public sealed record AgentBrowserProvisioningOptions(
    bool Enabled = false,
    string EnvironmentName = "",
    string Mode = "",
    string HostingTopology = "",
    string IdentityDatabaseTopology = "",
    string AuthenticationProvider = "",
    string AuthorizationProvider = "",
    string DatabaseProvider = "",
    string DatabaseName = "",
    string ConfigurationManifestMode = "")
{
    public bool EnsureAdmitted(bool isStandaloneHost = false)
    {
        bool agentMode = string.Equals(Mode, "AgentBrowser", StringComparison.OrdinalIgnoreCase);
        if (Enabled != agentMode)
            throw new InvalidOperationException("agent-browser-opt-in-mismatch");
        if (!Enabled)
            return false;

        if (!string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-environment-unsupported");
        if (isStandaloneHost)
            throw new InvalidOperationException("agent-browser-host-unsupported");
        if (!string.Equals(HostingTopology, "Split", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-topology-unsupported");
        if (!string.Equals(IdentityDatabaseTopology, "colocated", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-identity-topology-unsupported");
        if (!string.Equals(AuthenticationProvider, "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-authentication-provider-unsupported");
        if (!string.Equals(AuthorizationProvider, "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-authorization-provider-unsupported");
        if (!string.Equals(DatabaseProvider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-database-provider-unsupported");
        if (!string.Equals(DatabaseName, "islamu_event_agent", StringComparison.Ordinal))
            throw new InvalidOperationException("agent-browser-database-name-unsupported");
        if (!string.Equals(ConfigurationManifestMode, "Off", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("agent-browser-manifest-unsupported");

        return true;
    }
}
