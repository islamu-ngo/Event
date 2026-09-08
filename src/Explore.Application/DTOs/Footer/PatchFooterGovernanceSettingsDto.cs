using Explore.Application.Models.Common;

namespace Explore.Application.DTOs.Footer;

public sealed record PatchFooterGovernanceSettingsDto
{
    public OptionalUpdate<bool> LockTenantTemplate { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantLinkGroups { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantSocialLinks { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantDescription { get; init; } = OptionalUpdate<bool>.Unspecified();
    public OptionalUpdate<bool> LockTenantCopyright { get; init; } = OptionalUpdate<bool>.Unspecified();

    public bool HasChanges() => LockTenantTemplate.HasValue || LockTenantLinkGroups.HasValue
        || LockTenantSocialLinks.HasValue || LockTenantDescription.HasValue || LockTenantCopyright.HasValue;
}
