using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Persistence;

public sealed record AtprotoJetstreamClaim(
    Guid ConsumerStateId,
    string Service,
    long Cursor,
    Guid LeaseToken,
    long LeaseFence);

public sealed record AtprotoEventProjectionInvalidation(
    string Did,
    string Collection,
    string RecordKey,
    long SourceVersion);

/// <summary>
/// An upstream account was deactivated or deleted, so every inbound record federated from that repository
/// must stop being presented. AT Protocol treats this as the network's purge signal; ignoring it leaves
/// content visible for an account that no longer exists.
/// </summary>
public sealed record AtprotoAccountPurge(
    string Did,
    long SourceVersion,
    string? Status);

public sealed record AtprotoJetstreamApplyRequest(
    AtprotoJetstreamClaim Claim,
    long ExpectedCursor,
    long NextCursor,
    AtprotoRecord? Record,
    IReadOnlyList<AtprotoRecordTenantPresentation> Presentations,
    AtprotoJetstreamQuarantine? Quarantine,
    DateTime ObservedAt,
    bool AdvanceCursor = true,
    AtprotoEventProjection? EventProjection = null,
    AtprotoEventProjectionInvalidation? EventProjectionInvalidation = null)
{
    public IReadOnlyList<AtprotoFederatedEventImportPlan> EventImports { get; init; } = [];

    /// <summary>
    /// Set instead of <see cref="Record"/> or <see cref="Quarantine"/>. Exactly one of the three carries
    /// the envelope's effect; persistence rejects a request that supplies none or more than one.
    /// </summary>
    public AtprotoAccountPurge? AccountPurge { get; init; }
}

public sealed record AtprotoPersistenceApplyResult(
    bool Applied,
    IReadOnlyList<FileStorageWriteResult> ConsumedStagedThumbnails)
{
    public static AtprotoPersistenceApplyResult Rejected { get; } = new(false, []);
}

public interface IAtprotoJetstreamRepository
{
    Task<AtprotoJetstreamClaim?> TryClaimAsync(
        string service,
        string leaseOwner,
        DateTime claimedAt,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task<bool> TryRenewAsync(
        AtprotoJetstreamClaim claim,
        DateTime observedAt,
        DateTime leaseExpiresAt,
        CancellationToken cancellationToken = default);

    Task<bool> TryApplyAndAdvanceAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<AtprotoPersistenceApplyResult> TryApplyAndAdvanceWithResultAsync(
        AtprotoJetstreamApplyRequest request,
        CancellationToken cancellationToken = default);
}
