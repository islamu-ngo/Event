
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
    public VisitorAccessPolicyState(VisitorAccessMode mode, IEnumerable<VisitorAccessProviderState> providers,
        bool userRegistrationEnabled = true)
    {
        Mode = mode;
        Providers = providers.ToImmutableArray();
        UserRegistrationEnabled = userRegistrationEnabled;
    }

    public VisitorAccessMode Mode { get; }
    public ImmutableArray<VisitorAccessProviderState> Providers { get; }
    public bool UserRegistrationEnabled { get; }
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
        IEnumerable<VisitorSignupDestination> signupDestinations,
        bool userRegistrationEnabled = true)
    {
        Mode = mode;
        AllowsExistingAccountLogin = allowsExistingAccountLogin;
        SignupDestinations = signupDestinations.ToImmutableArray();
        UserRegistrationEnabled = userRegistrationEnabled;
    }

    public VisitorAccessMode Mode { get; }
    public bool AllowsNewNativeAllocation => Mode != VisitorAccessMode.DirectoryListingOnly;
    public bool AllowsAnonymousParticipation =>
        Mode is VisitorAccessMode.FullRegistrationAndAuth or VisitorAccessMode.AnonymousOnly;
    public bool AllowsAccountRequiredParticipation =>
        (Mode is VisitorAccessMode.FullRegistrationAndAuth or VisitorAccessMode.AuthenticatedOnly)
        && AllowsExistingAccountLogin;
    public bool AllowsExistingAccountLogin { get; }
    public bool UserRegistrationEnabled { get; }
    public ImmutableArray<VisitorSignupDestination> SignupDestinations { get; }
}
