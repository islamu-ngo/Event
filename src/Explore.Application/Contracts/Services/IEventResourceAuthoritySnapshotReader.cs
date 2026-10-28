using Explore.Application.Authorization;

namespace Explore.Application.Contracts.Services;

/// <summary>
/// Reads fresh, no-tracking, server-derived facts in the caller's serializable transaction.
/// All time-aware authority resolvers receive evaluationUtc; implementations never sample their own clock.
/// Missing, deleted, cross-tenant, or incomplete authority returns null.
/// </summary>
public interface IEventResourceAuthoritySnapshotReader
{
    Task<EventResourceAuthorizationFacts?> ReadAsync(
        EventResourceAuthorityRequest request, DateTimeOffset evaluationUtc, CancellationToken cancellationToken);

    /// <summary>Reads one subject's bounded targets by fact category, preserving input positions, including missing facts.</summary>
    Task<IReadOnlyList<EventResourceAuthorizationFacts?>> ReadBatchAsync(
        IReadOnlyList<EventResourceAuthorityRequest> requests, DateTimeOffset evaluationUtc, CancellationToken cancellationToken);
}

public sealed record EventResourceAuthorityRequest(
    Guid TenantId, Guid ResourceId, Guid? SubjectUserId, bool IsMachineCaller,
    string Action, DateTimeOffset? DeadlineUtc = null)
{
    public const int MaximumBatchResources = 500;
    public const int MaximumBatchChecks = MaximumBatchResources * 13;

    public EventResourceMachinePrincipal? MachinePrincipal { get; init; }

    /// <summary>Server-selected upload boundary; quarantined material cannot be replaced or disclosed through upload responses.</summary>
    public bool RequiresUnquarantinedMaterial { get; init; }
    /// <summary>Server-selected upload mutation, never ordinary metadata editing or cleanup.</summary>
    public bool IsUploadMutation { get; init; }
    public bool HasConsistentUploadPurpose => !IsUploadMutation
        || (Action == "update" && !TargetsParentEvent && ResourceId != Guid.Empty && RequiresUnquarantinedMaterial);
    public bool HasConsistentPrincipal => IsMachineCaller
        ? SubjectUserId is null && MachinePrincipal is not null
        : MachinePrincipal is null;

    /// <summary>Server-selected parent-event authority for management collection and export reads.</summary>
    public bool IsEventCollection { get; init; }
    public bool TargetsParentEvent => Action == "create" || IsEventCollection;

    /// <summary>Binds an already prepared metadata projection to the exact persisted resource version.</summary>
    public Guid? ExpectedResourceVersion { get; init; }

    /// <summary>Binds descriptive file metadata to the exact privately prepared attachment.</summary>
    public string? ExpectedAttachmentGeneration { get; init; }

    /// <summary>Binds prepared audit entries to the retention policy that qualified their disclosure.</summary>
    public int? ExpectedAuditRetentionDays { get; init; }

    /// <summary>Binds prepared audience metadata to its exact disclosure, not merely an unchanged row stamp.</summary>
    public Explore.Domain.Services.EventResourceAccessDecision? ExpectedDisclosure { get; init; }
}
