namespace Explore.Application.Contracts.Persistence;

using System.Collections.Immutable;
using Explore.Application.Settings;

public interface ICoordinatedSettingMutationStore
{
    Task<PublicationPolicyMutationSnapshot> ReadTenantSnapshotAsync(
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<PublicationPolicyMutationSnapshot> ReadInstanceSnapshotAsync(
        CancellationToken cancellationToken);

    Task<CoordinatedSettingMutationWriteResult> WriteTenantAsync(
        Guid tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid? actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);

    Task<CoordinatedSettingMutationWriteResult> WriteInstanceAsync(
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        Guid actorUserId,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);
}
