using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Settings.Groups;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSupportAdministrationPreparation
{
    private sealed class Prepared(object operation, Guid principalId) : IPreparedHumanSupportAdministration
    {
        private int consumed;
        private bool acquired;
        private bool staged;
        private SupportAccessSession? entity;
        internal Func<CancellationToken, Task> Acquire { private get; set; } = null!;
        internal SupportAdministrationSessionFact? Original { get; set; }
        internal SupportAdministrationSessionFact? Expected { get; private set; }
        internal SupportAdministrationAuditFact? Audit { get; private set; }
        internal bool Completed { get; private set; }
        internal bool Rejected { get; private set; }
        public Guid PrincipalId { get; } = principalId;
        public DateTimeOffset TransitionAt { get; internal set; }
        public ISupportAdministrationData? NativeData { get; internal set; }
        public SupportAccessSettingGroup Settings { get; internal set; } = null!;

        public async Task AcquireAsync(object originalRequest, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
            {
                Rejected = true;
                throw new InvalidOperationException("Prepared support custody belongs to one attempt.");
            }
            if (!ReferenceEquals(operation, originalRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Support custody belongs to another original request.");
            }
            try
            {
                await Acquire(cancellationToken);
                acquired = true;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        public SupportAccessSession StartSession()
        {
            RequireStage();
            try
            {
                if (operation is not StartSupportAccessSessionCommand request)
                    throw new InvalidOperationException("Only the original support start may create a session.");
                entity = SupportAccessSession.Start(PrincipalId, request.TargetTenantId, request.Mode,
                    request.ReasonCode, request.ReasonText, request.TicketReference ?? string.Empty,
                    TransitionAt, TransitionAt.AddMinutes(request.DurationMinutes), request.TargetTenantUserId);
                Expected = SupportAdministrationSessionFact.From(entity);
                return entity;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        public bool EndSession(SupportAccessSession session)
        {
            RequireStage();
            try
            {
                if (Original is null || SupportAdministrationSessionFact.From(session) != Original)
                    throw new UnauthorizedAccessException("Support termination requires the unchanged original target.");
                entity = session;
                switch (operation)
                {
                    case StopSupportAccessSessionCommand stop when stop.SessionId == session.Id
                        && session.ActorUserId == PrincipalId:
                        if (!session.IsActiveAt(TransitionAt))
                        {
                            if (session.StatusId != (int)SupportAccessSessionStatusEnum.Active)
                                return false;
                            Expected = Original with
                            {
                                StatusId = (int)SupportAccessSessionStatusEnum.Expired,
                                EndReasonId = (int)SupportAccessEndReasonEnum.Expired,
                                EndedAtUtc = TransitionAt,
                                EndReasonText = "Expired before stop request."
                            };
                            session.Expire(TransitionAt, Expected.EndReasonText);
                        }
                        else
                        {
                            Expected = Original with
                            {
                                StatusId = (int)SupportAccessSessionStatusEnum.Stopped,
                                EndReasonId = (int)SupportAccessEndReasonEnum.UserStopped,
                                EndedAtUtc = TransitionAt,
                                EndReasonText = Normalize(stop.EndReasonText)
                            };
                            session.Stop(TransitionAt, stop.EndReasonText);
                        }
                        break;
                    case ForceStopSupportAccessSessionCommand force when force.SessionId == session.Id
                        && session.IsActiveAt(TransitionAt):
                        Expected = Original with
                        {
                            StatusId = (int)SupportAccessSessionStatusEnum.Revoked,
                            EndReasonId = (int)SupportAccessEndReasonEnum.ForceStopped,
                            EndedAtUtc = TransitionAt,
                            EndReasonText = Normalize(force.EndReasonText)
                        };
                        session.Revoke(TransitionAt, SupportAccessEndReasonEnum.ForceStopped, force.EndReasonText);
                        break;
                    default:
                        throw new UnauthorizedAccessException("The original support command cannot end this session.");
                }
                if (SupportAdministrationSessionFact.From(session) != Expected)
                    throw new UnauthorizedAccessException("Support termination differed from its compiled transition.");
                return true;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        public void Complete(SupportAccessSession session, SupportAccessAuditEvent audit)
        {
            try
            {
                if (Rejected || !acquired || !staged || Completed || !ReferenceEquals(entity, session)
                    || Expected is not { } expected || session.ConcurrencyStamp == Guid.Empty
                    || session.ConcurrencyStamp == expected.ConcurrencyStamp
                    || SupportAdministrationSessionFact.From(session) != (expected with { ConcurrencyStamp = session.ConcurrencyStamp }))
                    throw new UnauthorizedAccessException("Only the exact saved support transition may complete.");
                var (type, outcome) = operation switch
                {
                    StartSupportAccessSessionCommand => (SupportAccessAuditEventTypeEnum.Started, "started"),
                    StopSupportAccessSessionCommand when expected.StatusId == (int)SupportAccessSessionStatusEnum.Expired =>
                        (SupportAccessAuditEventTypeEnum.Expired, "expired"),
                    StopSupportAccessSessionCommand => (SupportAccessAuditEventTypeEnum.Stopped, "stopped"),
                    ForceStopSupportAccessSessionCommand => (SupportAccessAuditEventTypeEnum.Revoked, "force_stopped"),
                    _ => throw new InvalidOperationException("A support disclosure cannot complete a mutation.")
                };
                var expectedAudit = new SupportAdministrationAuditFact(audit.Id, expected.Id, (int)type,
                    PrincipalId, expected.TargetTenantId, expected.TargetTenantUserId, TransitionAt, outcome,
                    null, null, null, null, null, null, null, null, null);
                if (audit.Id == Guid.Empty || SupportAdministrationAuditFact.From(audit) != expectedAudit)
                    throw new UnauthorizedAccessException("Support lifecycle evidence differs from the original transition.");
                Expected = expected with { ConcurrencyStamp = session.ConcurrencyStamp };
                Audit = expectedAudit;
                Completed = true;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        internal void RequirePersisted(SupportAdministrationPersistedFacts persisted)
        {
            if (Rejected || !acquired || Expected is not null && !Completed
                || persisted.Session != (Completed ? Expected : Original) || persisted.Audit != Audit)
                throw new UnauthorizedAccessException("Persisted support effects differ from the exact original transition.");
        }

        private void RequireStage()
        {
            if (Rejected || !acquired || staged)
            {
                Rejected = true;
                throw new InvalidOperationException("A support transition requires one acquired original body.");
            }
            staged = true;
        }

        private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
