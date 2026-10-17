using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Domain;
using Explore.Application.Settings.Groups;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Original human custody for the five protected support-administration owners.
/// Preparation selects no new database; acquisition belongs to the actual non-replaying owner.
/// </summary>
public interface IHumanSupportAdministrationPreparation
{
    Task<IPreparedHumanSupportAdministration> PrepareAsync(StartSupportAccessSessionCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanSupportAdministration> PrepareAsync(StopSupportAccessSessionCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanSupportAdministration> PrepareAsync(ForceStopSupportAccessSessionCommand request, CancellationToken cancellationToken);
    Task<IPreparedHumanSupportAdministration> PrepareAsync(ListSupportAccessSessionsQuery request, CancellationToken cancellationToken);
    Task<IPreparedHumanSupportAdministration> PrepareAsync(GetSupportAccessAuditEventsQuery request, CancellationToken cancellationToken);
}

public interface IPreparedHumanSupportAdministration
{
    Guid PrincipalId { get; }
    DateTimeOffset TransitionAt { get; }
    ISupportAdministrationData? NativeData { get; }
    SupportAccessSettingGroup Settings { get; }
    Task AcquireAsync(object originalRequest, CancellationToken cancellationToken);
    SupportAccessSession StartSession();

    /// <summary>
    /// Applies only the original command's privately compiled transition. Returns false
    /// for an already terminal own-stop. It is not permission to change a different session.
    /// </summary>
    bool EndSession(SupportAccessSession session);

    /// <summary>
    /// Binds the completed save and audit to that exact transition. Final persisted
    /// validation remains mandatory; neither this call nor its arguments grant authority.
    /// </summary>
    void Complete(SupportAccessSession session, SupportAccessAuditEvent audit);
}

/// <summary>
/// Tracker-free support target and lifecycle evidence on the original physical owner.
/// Unsupported providers must reject rather than borrow Global credentials.
/// </summary>
public interface ISupportAdministrationSource
{
    void Select(StartSupportAccessSessionCommand request);
    void Select(StopSupportAccessSessionCommand request);
    void Select(ForceStopSupportAccessSessionCommand request);
    void Select(ListSupportAccessSessionsQuery request);
    void Select(GetSupportAccessAuditEventsQuery request);
    Task<SupportAdministrationSessionFact?> ObserveAsync(Guid? sessionId, CancellationToken cancellationToken);
    Task<ISupportAdministrationLease> AcquireAsync(Guid? sessionId, IPolicySourceSetLease sourceSet,
        CancellationToken cancellationToken);
}

public interface ISupportAdministrationLease
{
    SupportAdministrationSessionFact? Original { get; }
    ISupportAdministrationData? NativeData { get; }
    Task<SupportAdministrationPersistedFacts> ValidateAsync(Guid? sessionId, Guid? auditId,
        CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Closed DATA operations for the retained request on the original physical owner.
/// Domain entities remain the transition authority. Native compilation independently
/// derives every stored column; these methods do not accept row patches or callbacks.
/// </summary>
public interface ISupportAdministrationData
{
    Task<SupportAdministrationStartTargets> ReadStartTargetsAsync(DateTimeOffset transitionAt, CancellationToken cancellationToken);
    Task<SupportAccessSession?> ReadSessionAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportAccessSession>> ListSessionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SupportAccessAuditEvent>> ListAuditAsync(CancellationToken cancellationToken);
    Task SaveStartedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
        CancellationToken cancellationToken);
    Task SaveOwnStoppedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
        CancellationToken cancellationToken);
    Task SaveForceStoppedAsync(SupportAccessSession session, SupportAccessAuditEvent audit, DateTimeOffset transitionAt,
        CancellationToken cancellationToken);
}

public sealed record SupportAdministrationStartTargets(
    bool TenantExists, bool TenantUserMatches, bool ActorHasActiveSession);

public sealed record SupportAdministrationPersistedFacts(
    SupportAdministrationSessionFact? Session, SupportAdministrationAuditFact? Audit);

public sealed record SupportAdministrationSessionFact(
    Guid Id, Guid? ActorUserId, Guid TargetTenantId, Guid? TargetTenantUserId,
    int StatusId, int ModeId, string ReasonCode, string ReasonText, string TicketReference,
    Guid? ApprovedByUserId, DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? EndedAtUtc, int? EndReasonId, string? EndReasonText, Guid ConcurrencyStamp)
{
    public static SupportAdministrationSessionFact From(SupportAccessSession session) => new(
        session.Id, session.ActorUserId, session.TargetTenantId, session.TargetTenantUserId,
        session.StatusId, session.ModeId, session.ReasonCode, session.ReasonText, session.TicketReference,
        session.ApprovedByUserId, session.StartedAtUtc, session.ExpiresAtUtc,
        session.EndedAtUtc, session.EndReasonId, session.EndReasonText, session.ConcurrencyStamp);

    public override string ToString() => nameof(SupportAdministrationSessionFact);
}

public sealed record SupportAdministrationAuditFact(
    Guid Id, Guid SessionId, int EventTypeId, Guid? ActorUserId, Guid TargetTenantId,
    Guid? TargetTenantUserId, DateTimeOffset OccurredAtUtc, string Outcome,
    string? RouteName, string? RequestName, string? ResourceKind, string? ResourceId,
    string? Action, int? HttpStatusCode, string? CorrelationId, string? TraceId, string? Metadata)
{
    public static SupportAdministrationAuditFact From(SupportAccessAuditEvent audit) => new(
        audit.Id, audit.SupportAccessSessionId, audit.EventTypeId, audit.ActorUserId, audit.TargetTenantId,
        audit.TargetTenantUserId, audit.OccurredAtUtc, audit.Outcome, audit.RouteName, audit.RequestName,
        audit.ResourceKind, audit.ResourceId, audit.Action, audit.HttpStatusCode,
        audit.CorrelationId, audit.TraceId, audit.SanitizedMetadataJson);

    public override string ToString() => nameof(SupportAdministrationAuditFact);
}
