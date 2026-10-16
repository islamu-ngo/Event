using System.Collections.Immutable;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;

namespace Explore.Application.Contracts.Persistence;

public interface IHumanSupportAccessSourceFactory
{
    Task<IHumanSupportAccessSourceFence> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Original-owner facts, never an action grant or a substitute account authority.</summary>
public interface IHumanSupportAccessSourceFence
{
    Task<HumanSupportAccessSourceFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanSupportAccessSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, IHumanEventAssignmentSourceLease? assignments = null,
        CancellationToken cancellationToken = default);
}

public interface IHumanSupportAccessSourceLease
{
    HumanSupportAccessSourceFacts Facts { get; }
    /// <summary>Returns fresh persisted facts after blocking reentry; never returns the acquisition cache.</summary>
    Task<HumanSupportAccessSourceFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Snapshots the real request's validated support metadata before transaction selection.
/// No constructor accepts a header, body/audit user identifier, target user, or fabricated context.
/// The service is the original trusted in-process service, not an HTTP-supplied implementation.
/// Its cached positive is only a session selector; persisted source facts revalidate it.
/// </summary>
public sealed class HumanSupportForwardingObservation
{
    private HumanSupportForwardingObservation(HumanCommitSubject subject, bool wasForwarded, bool wasActive,
        Guid? sessionId, Guid? resolvedTenantId)
    {
        Subject = subject;
        WasForwarded = wasForwarded;
        WasActive = wasActive;
        SessionId = sessionId;
        ResolvedTenantId = resolvedTenantId;
    }

    public HumanCommitSubject Subject { get; }
    public bool WasForwarded { get; }
    public bool WasActive { get; }
    public Guid? SessionId { get; }
    public Guid? ResolvedTenantId { get; }

    public static async Task<HumanSupportForwardingObservation> CaptureAsync(HumanCommitSubject subject,
        ISupportAccessSessionService originalService, ITenantContextAccessor originalTenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(originalService);
        ArgumentNullException.ThrowIfNull(originalTenant);
        Guid? tenant = originalTenant.TenantId;
        var current = await originalService.GetCurrentAsync(cancellationToken);
        if (tenant != originalTenant.TenantId || tenant == Guid.Empty
            || current.IsActive && (current.ActorUserId != subject.PrincipalId
                || current.SessionId is null || current.SessionId == Guid.Empty
                || tenant.HasValue && current.TargetTenantId != tenant))
            throw new UnauthorizedAccessException("human_support_forwarding_original_subject_mismatch");
        return new(subject, current.WasForwarded, current.IsActive,
            current.IsActive ? current.SessionId : null, tenant);
    }

    public override string ToString() => nameof(HumanSupportForwardingObservation);
}

public sealed record HumanSupportAccessSourceFacts(
    HumanSupportForwardingObservation Forwarding,
    ImmutableArray<HumanSupportSessionFact> Sessions,
    ImmutableArray<HumanSupportSettingFact> Settings)
{
    public override string ToString() => nameof(HumanSupportAccessSourceFacts);
}

public sealed record HumanSupportSessionFact(Guid Id, Guid? ActorUserId, Guid TargetTenantId,
    Guid? TargetTenantUserId, int StatusId, int ModeId, DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? EndedAtUtc, string ReasonCode, string TicketReference, Guid ConcurrencyStamp)
{
    public override string ToString() => nameof(HumanSupportSessionFact);
}

public sealed record HumanSupportSettingFact(Guid Id, string SettingKey, string Value, int SettingValueTypeId,
    bool IsLocked, string? Description, string? Category, string? AllowedValues)
{
    public override string ToString() => nameof(HumanSupportSettingFact);
}
