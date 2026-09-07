using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;

namespace Explore.Application.DTOs.Onboarding;

public sealed record CompleteTenantOnboardingRequest
{
    public required UpdateTenantPolicyRequest Settings { get; init; } = new();
    public required TenantDirectoryOperatorIdentityInputDto DirectoryOperatorIdentity { get; init; }
    public Guid? ExpectedDirectoryOperatorIdentityConcurrencyStamp { get; init; }
}
