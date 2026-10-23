using System.Collections.Immutable;
using Explore.Application.Settings;
using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Operations;

public sealed record AtprotoModerationTarget(
    Guid ActorId, Guid? IdentityId, string? Did, Guid SubjectStamp);

public sealed record AtprotoPublicationLineage(
    Guid TenantId, string SourceEntityType, Guid SourceEntityId,
    Guid UserId, string Did, string Collection, string RecordKey);

public sealed record AtprotoModerationDeleteFence(
    Guid SourceVersion, Guid ActorStamp, Guid? IdentityStamp);

public sealed record AtprotoModerationSettledRecord(Guid RecordId, string? Cid);

public sealed record AtprotoModerationMutation(
    Guid Id, PdsSyncOperation Operation, PdsSyncStatus Status,
    DateTime CreatedAt, DateTime? NextRetryAt, DateTime? LeaseExpiresAt,
    long LeaseFence, string PdsHost, string? ExpectedCid);

public sealed record AtprotoModerationPolicyFacts(
    SettingMergeFacts EventsEnabled,
    SettingMergeFacts ValidationProfile,
    SettingMergeFacts PublishConsent);

public sealed record AtprotoAccountBindingFacts(
    int SessionCount, string? SubjectDid, string? PdsHost, bool ExactExternalLoginExists);

public sealed record AtprotoModerationDeleteCandidate(
    AtprotoPublicationLineage Lineage,
    AtprotoModerationDeleteFence Fence,
    AtprotoModerationSettledRecord? Settled,
    ImmutableArray<AtprotoModerationMutation> Unsettled,
    AtprotoModerationPolicyFacts Policy,
    AtprotoAccountBindingFacts Account);

public sealed record AtprotoModerationDeleteWrite(
    AtprotoPublicationLineage Lineage,
    AtprotoModerationDeleteFence Fence,
    Guid SuccessorId,
    DateTime CreatedAtUtc,
    string PdsHost,
    Guid? AtprotoRecordId,
    string? ExpectedCid,
    DateTime? NotBefore);

/// <summary>
/// Reads grounded, secret-free compensation facts and writes fixed deletes inside
/// the caller's existing Serializable transaction. Weaker isolation is rejected.
/// Neither operation owns a transaction or a tenant lease.
/// </summary>
public interface IAtprotoModerationCompensationStore
{
    IAsyncEnumerable<AtprotoModerationDeleteCandidate> ReadDeleteCandidatesAsync(
        AtprotoModerationTarget target, CancellationToken cancellationToken);

    Task WriteDeleteAsync(
        AtprotoModerationTarget target,
        AtprotoModerationDeleteWrite write,
        CancellationToken cancellationToken);
}
