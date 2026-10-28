using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventRoleAssignments.Requests.Commands;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanEventTeamActionPreparation
{
    private sealed class Prepared(HumanEventTeamActionPreparation issuer, object operation, EventTeamPolicyFacts policy,
        HumanEventAssignmentSourceFacts assignments, DateTime evaluation, DateTime changedAt, Guid principal)
        : IPreparedHumanEventTeamAction
    {
        private int consumed;
        private bool acquired;
        private bool handoffConsumed;
        private AssignEventRoleCommand? handoff;
        private Guid? completed;
        internal bool Rejected { get; set; }
        internal bool Ended { get; set; }
        internal HumanEventTeamActionPreparation Issuer { get; } = issuer;
        internal Func<CancellationToken, Task> Acquire { private get; set; } = null!;
        internal Action RequireActiveOwner { private get; set; } = null!;
        public DateTime ChangedAt { get; } = changedAt;

        public async Task AcquireAsync(object originalRequest, CancellationToken token)
        {
            if (Interlocked.Exchange(ref consumed, 1) != 0)
            {
                Rejected = true;
                throw new InvalidOperationException("Original Event team custody belongs to one attempt.");
            }
            if (!ReferenceEquals(operation, originalRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("An equal or substituted request is not the original Event team action.");
            }
            try
            {
                await Acquire(token);
                acquired = true;
            }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        private void Active()
        {
            if (!acquired || Rejected || Ended || completed.HasValue)
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Event team custody is not an active original body.");
            }
            try { RequireActiveOwner(); }
            catch
            {
                Rejected = true;
                throw;
            }
        }

        public void RequireTeamRead(GetEventTeamListRequest originalRequest)
        {
            Active();
            if (!ReferenceEquals(operation, originalRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Only the original team query may read its held entity graph.");
            }
        }

        public AssignEventRoleCommand BindResolvedTarget(AssignEventRoleByEmailCommand original, Guid userId)
        {
            Active();
            if (!ReferenceEquals(operation, original) || handoff is not null
                || userId == Guid.Empty || policy.ResolvedUserId != userId)
            {
                Rejected = true;
                throw new UnauthorizedAccessException("The email handoff changed its original target.");
            }
            return handoff = new()
            {
                TenantId = original.TenantId,
                EventId = original.EventId,
                TargetUserId = userId,
                RoleId = original.RoleId,
                Status = original.Status,
                StartsAtUtc = original.StartsAtUtc,
                ExpiresAtUtc = original.ExpiresAtUtc
            };
        }

        internal void ConsumeHandoff(AssignEventRoleCommand original)
        {
            Active();
            if (handoffConsumed || !ReferenceEquals(handoff, original))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Only the exact original email-to-command handoff may execute once.");
            }
            handoffConsumed = true;
        }

        public EventRoleAssignmentAuthorityResult CanAssignRole(int roleId)
        {
            Active();
            int? expectedRole = operation switch
            {
                AssignEventRoleCommand x => x.RoleId,
                AssignEventRoleByEmailCommand x when handoffConsumed => x.RoleId,
                RevokeEventRoleAssignmentCommand x => policy.Assignments.SingleOrDefault(row => row.Id == x.AssignmentId)?.RoleId,
                _ => null
            };
            if (expectedRole != roleId)
            {
                Rejected = true;
                throw new UnauthorizedAccessException("The ceiling question changed its original requested role.");
            }
            if (!FirstReleaseRole(roleId))
                return EventRoleAssignmentAuthorityResult.Denied(EventRoleAuthorityFailureCodes.RoleNotAssignable,
                    "The requested event role is not assignable in this release.");
            var assignable = AssignablePermissions();
            if (assignable.Count == 0)
                return EventRoleAssignmentAuthorityResult.Denied(EventRoleAuthorityFailureCodes.AuthorityMissing,
                    "You do not have event-team management authority for this event.");
            var role = policy.Roles.SingleOrDefault(row => row.Id == roleId);
            if (role is null || role.RoleScopeId != (int)RoleScopeEnum.Event)
                return EventRoleAssignmentAuthorityResult.Denied(EventRoleAuthorityFailureCodes.RoleNotAssignable,
                    "The requested role is not an event role.");
            var requested = RolePermissions(roleId);
            var missing = requested.Where(code => !assignable.Contains(code)).OrderBy(code => code, StringComparer.Ordinal).ToArray();
            return missing.Length == 0 ? EventRoleAssignmentAuthorityResult.Allowed(requested)
                : EventRoleAssignmentAuthorityResult.Denied(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded,
                    "The role contains permissions outside your same-event authority ceiling.", missing);
        }

        public IReadOnlyList<EventRolePreset> GetAssignablePresets()
        {
            Active();
            if (operation is not (GetAssignableEventRolePresetsRequest or GetEventTeamListRequest))
            {
                Rejected = true;
                throw new UnauthorizedAccessException("This original action is not a preset disclosure.");
            }
            var assignable = AssignablePermissions();
            if (assignable.Count == 0) return [];
            return policy.Roles.Where(role => role.RoleScopeId == (int)RoleScopeEnum.Event && FirstReleaseRole(role.Id))
                .OrderBy(role => role.Id).Select(role => (Role: role, Permissions: RolePermissions(role.Id)))
                .Where(row => row.Permissions.IsSubsetOf(assignable))
                .Select(row => new EventRolePreset(row.Role.Id, row.Role.MasterCode, row.Role.FullName, row.Role.Description,
                    row.Permissions.OrderBy(code => code, StringComparer.Ordinal).ToArray())).ToArray();
        }

        public EventAuthorityForUser GetTeamAuthority()
        {
            Active();
            if (operation is not GetEventTeamListRequest)
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Only an acquired team read may disclose its original authority.");
            }
            return HumanEventAssignmentProjection.Authority(assignments, evaluation)
                .Events[assignments.EventIds.Single()];
        }

        // Same ceiling as EventRoleAuthorityCeilingService, evaluated solely from the
        // retained complete definition inventory rather than an unbound registry cache.
        private HashSet<string> AssignablePermissions()
        {
            var authority = HumanEventAssignmentProjection.Authority(assignments, evaluation).Events[assignments.EventIds.Single()];
            if (!authority.PermissionCodes.Contains(PermissionCodes.EventManageTeam)) return new(StringComparer.Ordinal);
            var active = policy.Permissions.Where(row => row.IsActive).ToDictionary(row => row.MasterCode, StringComparer.Ordinal);
            return authority.PermissionCodes.Where(code => active.TryGetValue(code, out var permission) && !permission.IsFiltered)
                .Where(code => code is not (PermissionCodes.EventManageOwner or PermissionCodes.EventTransferOwnership
                    or PermissionCodes.EventDelete or PermissionCodes.EventManageFinance or PermissionCodes.EventApprovePublish))
                .ToHashSet(StringComparer.Ordinal);
        }

        private HashSet<string> RolePermissions(int role) => policy.RolePermissions.Where(row => row.RoleId == role)
            .Join(policy.Permissions, mapping => mapping.PermissionId, permission => permission.Id, (_, permission) => permission)
            .Where(row => row.IsActive).Select(row => row.MasterCode).ToHashSet(StringComparer.Ordinal);

        private static bool FirstReleaseRole(int id) => id is (int)RoleEnum.EventOwner or (int)RoleEnum.EventManager
            or (int)RoleEnum.RegistrationManager or (int)RoleEnum.CheckInStaff or (int)RoleEnum.EventDiscoveryReviewer;

        public void Complete(Guid assignmentId)
        {
            Active();
            if (assignmentId == Guid.Empty || operation switch
            {
                RevokeEventRoleAssignmentCommand x => x.AssignmentId != assignmentId,
                AssignEventRoleCommand => policy.Assignments.Any(row => row.Id == assignmentId),
                AssignEventRoleByEmailCommand => !handoffConsumed || policy.Assignments.Any(row => row.Id == assignmentId),
                _ => true
            })
            {
                Rejected = true;
                throw new UnauthorizedAccessException("Completion changed the original Event team transition.");
            }
            completed = assignmentId;
        }

        internal IEnumerable<EventTeamAssignmentFact> ExpectedAssignments()
        {
            if (completed is not { } id) return policy.Assignments;
            if (operation is RevokeEventRoleAssignmentCommand)
                return policy.Assignments.Select(row => row.Id == id ? row with
                {
                    Status = (int)EventRoleAssignmentStatus.Revoked,
                    RevokedAtUtc = ChangedAt,
                    RevokedByUserId = principal,
                    Version = checked(row.Version + 1)
                } : row);
            var command = operation as AssignEventRoleCommand ?? handoff
                ?? throw new InvalidOperationException("Original assignment handoff is missing.");
            return policy.Assignments.Append(new(id, command.TenantId, command.EventId, command.TargetUserId,
                command.RoleId, (int)command.Status, command.StartsAtUtc, command.ExpiresAtUtc, null, null, 1, principal));
        }
    }
}
