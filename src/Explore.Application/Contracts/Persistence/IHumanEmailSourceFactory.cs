using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Dedicated source purposes, not ordinary Runtime/Global or email action permissions.</summary>
public enum HumanEmailSourcePurpose
{
    Instance = 1,
    Tenant = 2
}

/// <summary>Resolve only after selecting the original EmailDelivery execution owner.</summary>
public interface IHumanEmailSourceFactory
{
    Task<IHumanEmailSourceProtocol> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete facts on the dedicated email owner. The parent retains its exact action, effect,
/// credential validation, support horizon, selected decision and final trusted-time checks.
/// Neither this protocol nor its leases grant an email action or administrator authority.
/// </summary>
public interface IHumanEmailSourceProtocol
{
    HumanCommitSubject Subject { get; }
    HumanAuthenticationLifetime Lifetime { get; }
    HumanSupportForwardingObservation Forwarding { get; }
    HumanEmailSourcePurpose Purpose { get; }
    Guid? SelectedTenantId { get; }

    Task<IHumanEmailSourcePreparation> PrepareAsync(
        bool includeSelectedRoute, CancellationToken cancellationToken = default);
    Task<IHumanEmailSourceLease> AcquireAsync(
        IHumanEmailSourcePreparation preparation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects caller implementations, another producer, another original identity/lifetime,
    /// wrong dedicated purpose/target and expired owner custody. Returns fresh persisted facts.
    /// This is source custody only; the parent must separately require its private action proof.
    /// </summary>
    Task<HumanEmailSourceFacts> RequireOwnedAsync(IHumanEmailSourceLease lease,
        HumanCommitSubject originalSubject, HumanAuthenticationLifetime originalLifetime,
        HumanEmailSourcePurpose purpose, Guid? selectedTenantId,
        CancellationToken cancellationToken = default);
}

public interface IHumanEmailSourcePreparation
{
    HumanEmailSourceFacts Facts { get; }
}

public interface IHumanEmailSourceLease
{
    HumanEmailSourceFacts Facts { get; }
}

public sealed record HumanEmailSourceFacts(
    HumanPrincipalSourceFacts Principal,
    HumanSupportAccessSourceFacts Support,
    HumanAuthorizationRouteFacts? Route)
{
    public override string ToString() => nameof(HumanEmailSourceFacts);
}
