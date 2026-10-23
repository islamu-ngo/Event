using System.Collections.Immutable;
using Explore.Application.Settings;
using Explore.Domain.Constants;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Fresh fixed-key route facts on one selected owner, not a decision. Observe before any
/// transaction; acquire only after that owner's policy-source-set lease. No provider I/O occurs here.
/// </summary>
public interface IHumanAuthorizationRouteSource
{
    Task<HumanAuthorizationRouteFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanAuthorizationRouteLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default);
}

public interface IHumanAuthorizationRouteLease
{
    HumanAuthorizationRouteFacts Facts { get; }

    /// <summary>
    /// Validates the original live owner and exclusion contract, then returns current facts,
    /// including its own persisted writes. Compare projected routes, not raw row equality.
    /// </summary>
    Task<HumanAuthorizationRouteFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Exactly six entries, in fixed order. Null System/Tenant denotes a real missing row.
/// Values and locks remain unparsed so projection uses the actual hierarchical merge and serializer.
/// PrincipalId binds the prepared account; this object grants no principal authority.
/// </summary>
public sealed record HumanAuthorizationRouteFacts(
    Guid PrincipalId,
    Guid? SelectedTenantId,
    ImmutableArray<HumanAuthorizationRouteSettingFact> Settings)
{
    public static ImmutableArray<string> Keys { get; } =
    [
        GovernanceSettingKeys.Security.AuthorizationProvider,
        GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
        GovernanceSettingKeys.Cerbos.CustomEndpoint,
        GovernanceSettingKeys.Cerbos.GrpcEndpoint,
        GovernanceSettingKeys.Cerbos.Mode,
        GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled
    ];

    public override string ToString() => nameof(HumanAuthorizationRouteFacts);
}

public sealed record HumanAuthorizationRouteSettingFact(
    string Key, SystemSettingFacts? System, TenantSettingFacts? Tenant)
{
    public override string ToString() => nameof(HumanAuthorizationRouteSettingFact);
}
