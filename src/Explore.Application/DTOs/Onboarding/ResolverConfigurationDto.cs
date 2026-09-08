namespace Explore.Application.DTOs.Onboarding;

public sealed record ResolverConfigurationDto
{
    public bool HeaderEnabled { get; set; } = true;

    public bool SubdomainEnabled { get; set; }

    public bool CustomDomainEnabled { get; set; }

    public bool PathEnabled { get; set; } = true;

    public string PathPrefix { get; set; } = "/t";

    public string InstanceBaseDomain { get; set; } = string.Empty;

    public bool AllowTenantCustomDomains { get; set; } = true;
}
