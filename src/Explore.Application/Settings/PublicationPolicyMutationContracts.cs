namespace Explore.Application.Settings;

using System.Collections.Immutable;
using Explore.Application.Notifications;

public enum PublicationPolicyLockedSystemBehavior
{
    Reject,
    RemoveOverride
}

public sealed record PublicationPolicyTenantMutationRequest(
    Guid TenantId,
    Guid? ActorUserId,
    DateTime OccurredAtUtc,
    ImmutableArray<PublicationPolicySettingMutation> Mutations,
    PublicationPolicyLockedSystemBehavior LockedSystemBehavior);

public sealed record PublicationPolicyInstanceMutationRequest(
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    ImmutableArray<PublicationPolicySettingMutation> Mutations);

public sealed record PublicationPolicyMutationResult(
    bool Success,
    string? FailureCode,
    string Message,
    ImmutableArray<SettingChangedNotification> DeferredNotifications);

public sealed record PublicationPolicyMutationSnapshot(
    ImmutableArray<PublicationPolicySystemValueSnapshot> SystemValues,
    ImmutableArray<PublicationPolicyTenantValueSnapshot> TenantValues);

public sealed record CoordinatedSettingValueChange(
    string Key,
    string? OldValue,
    string? NewValue);

public sealed record CoordinatedSettingMutationWriteResult(
    ImmutableArray<CoordinatedSettingValueChange> Changes);
