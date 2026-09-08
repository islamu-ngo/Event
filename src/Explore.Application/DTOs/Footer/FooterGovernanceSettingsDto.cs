namespace Explore.Application.DTOs.Footer;

public sealed record FooterGovernanceSettingsDto
{
    public bool LockTenantTemplate { get; init; }
    public bool LockTenantLinkGroups { get; init; }
    public bool LockTenantSocialLinks { get; init; }
    public bool LockTenantDescription { get; init; }
    public bool LockTenantCopyright { get; init; }
}
