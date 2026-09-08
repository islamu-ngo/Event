// ABOUTME: Immutable visitor authority facts and complete proposed-state inputs for shared policy evaluation.
// ABOUTME: Snapshots provider collections and separates public onboarding from existing account login.

using System.Collections.Immutable;
using Explore.Domain.Enums;

namespace Explore.Application.Models;

public sealed record VisitorAccessProviderState(
    AuthenticationProviderKind Provider,
    bool Enabled,
    bool TenantUsable,
    PublicOnboardingPolicy PublicOnboardingPolicy,
    string? SignupUrl = null);

public sealed record VisitorAccessPolicyState
{
    public VisitorAccessPolicyState(VisitorAccessMode mode, IEnumerable<VisitorAccessProviderState> providers)
    {
        Mode = mode;
        Providers = providers.ToImmutableArray();
    }

    public VisitorAccessMode Mode { get; }
    public ImmutableArray<VisitorAccessProviderState> Providers { get; }
}

public sealed record VisitorSignupDestination(AuthenticationProviderKind Provider, string Url);

/// <summary>
/// Applies only to new native visitor participation. Existing order status/cancellation,
/// operator login, listing, walk-in and external participation retain their own authority.
/// </summary>
public sealed record VisitorAccessCapability
{
    internal VisitorAccessCapability(
        VisitorAccessMode mode,
        bool allowsExistingAccountLogin,
        IEnumerable<VisitorSignupDestination> signupDestinations)
    {
        Mode = mode;
        AllowsExistingAccountLogin = allowsExistingAccountLogin;
        SignupDestinations = signupDestinations.ToImmutableArray();
    }

    public VisitorAccessMode Mode { get; }
    public bool AllowsNewNativeAllocation => Mode != VisitorAccessMode.DirectoryListingOnly;
    public bool AllowsAnonymousParticipation => AllowsNewNativeAllocation;
    public bool AllowsAccountRequiredParticipation =>
        Mode == VisitorAccessMode.FullRegistrationAndAuth && !SignupDestinations.IsEmpty;
    public bool AllowsExistingAccountLogin { get; }
    public ImmutableArray<VisitorSignupDestination> SignupDestinations { get; }
}
