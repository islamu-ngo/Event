using System.Data.Common;
using System.IO;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Commits qualified event-team mutations with audit and native qualification invalidation.</summary>
public sealed partial class EventRoleAssignmentMutationBoundary(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IUserContext user, IAdminContext identity,
    IMachinePrincipalAccessor machine, IAuthorizationProvider authorization, TimeProvider clock,
    IModerationRecognitionContinuityReader continuity,
    IHumanActionCommitFinalizer finalizer)
    : IEventOwnershipTransferMutationBoundary, IEventRoleAssignmentWindowMutationBoundary,
      IEventRoleAssignmentRevocationMutationBoundary, IEventRoleAssignmentCreationMutationBoundary,
      IEventInitialCreationMutationBoundary
{
    public Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid currentOwnerAssignmentId,
        Guid newOwnerUserId, Guid auditId, Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken) => ExecuteMutationAsync(MutationKind.Transfer, tenantId, eventId, currentOwnerAssignmentId,
            newOwnerUserId, null, null, auditId, null, operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId,
        DateTime startsAtUtc, DateTime? expiresAtUtc, Guid auditId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(MutationKind.Window, tenantId, eventId, assignmentId, null, startsAtUtc, expiresAtUtc, auditId, null, operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId, Guid auditId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(MutationKind.Revoke, tenantId, eventId, assignmentId, null, null, null, auditId, null, operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteAsync(Guid tenantId, Guid eventId, Guid assignmentId, Guid targetUserId,
        int roleId, EventRoleAssignmentStatus status, DateTime startsAtUtc, DateTime? expiresAtUtc, Guid auditId,
        string? expectedEmail, Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken) =>
        ExecuteMutationAsync(MutationKind.Create, tenantId, eventId, assignmentId, targetUserId, startsAtUtc, expiresAtUtc,
            auditId, new Creation(roleId, status, expectedEmail), operation, cancellationToken);

    public Task<BaseCommandResponse<Guid>> ExecuteInCurrentTransactionAsync(Guid tenantId, Guid eventId, Guid assignmentId,
        Guid targetUserId, int roleId, EventRoleAssignmentStatus status, DateTime startsAtUtc, DateTime? expiresAtUtc,
        Guid auditId, string? expectedEmail, Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation,
        CancellationToken cancellationToken) =>
        ExecuteMutationAsync(MutationKind.Create, tenantId, eventId, assignmentId, targetUserId, startsAtUtc,
            expiresAtUtc, auditId, new Creation(roleId, status, expectedEmail), operation, cancellationToken, joined: true);

    private async Task<BaseCommandResponse<Guid>> ExecuteMutationAsync(MutationKind kind, Guid tenantId, Guid eventId, Guid currentOwnerAssignmentId,
        Guid? newOwnerUserId, DateTime? windowStartsAtUtc, DateTime? windowExpiresAtUtc, Guid auditId, Creation? creation,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken,
        bool joined = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool transfer = kind == MutationKind.Transfer;
        bool revocation = kind == MutationKind.Revoke;
        bool creating = kind == MutationKind.Create;
        string action = transfer ? AuthorizationActions.Events.TransferOwnership : AuthorizationActions.Events.ManageTeam;
        Guid targetUserId = newOwnerUserId ?? Guid.Empty;
        RequireContext();
        if (eventId == Guid.Empty || currentOwnerAssignmentId == Guid.Empty || (transfer || creating) && targetUserId == Guid.Empty || auditId == Guid.Empty) Deny();
        if (joined) context.RequireHumanCommitAdmission();
        else if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges()) Deny();
        Attempt? attempted = null;
        var admission = new NativeHumanMutationAdmission(context, finalizer);
        try
        {
            return joined ? await ExecuteAttemptAsync(cancellationToken)
                : await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(() =>
                    context.Database.CreateExecutionStrategy().ExecuteAsync<object?, BaseCommandResponse<Guid>>(null,
                        (_, _, token) => ExecuteAttemptAsync(token),
                        async (_, _, token) => new ExecutionResult<BaseCommandResponse<Guid>>(
                            await VerifyAsync(token), attempted is null ? default! : attempted.Result), cancellationToken), cancellationToken);

            async Task<BaseCommandResponse<Guid>> ExecuteAttemptAsync(CancellationToken token)
                {
                    RequireContext();
                    if (await VerifyAsync(token)) return attempted!.Result;
                    var actor = await identity.ResolveUserIdAsync(token);
                    if (actor is null || actor == Guid.Empty) Deny();
                    var decision = await authorization.AuthorizeAsync(new AuthorizationRequest(
                        AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, action), eventId.ToString("D"),
                        Facts: new EventScopedAuthorizationFacts(tenantId, eventId), Subject: new AuthorizationSubject(actor.Value),
                        Tenant: new AuthorizationTenant(tenantId)), token);
                    if (!decision.IsAllowed)
                    {
                        if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                            throw new AuthorizationProviderUnavailableException(ResourceKinds.Event, action);
                        Deny();
                    }
                    if (!joined) context.ChangeTracker.Clear();
                    int targetRoleId = creation?.RoleId ?? (int)RoleEnum.EventOwner;
                    if (!transfer && !creating)
                    {
                        var target = await context.EventRoleAssignments.AsNoTracking().Where(row => row.Id == currentOwnerAssignmentId
                            && row.TenantId == tenantId && row.EventId == eventId).Select(row => new { row.UserId, row.RoleId }).SingleOrDefaultAsync(token);
                        if (target is null)
                            return BaseCommandResponse.Failure("event_role_assignment_not_found",
                                "Event role assignment not found for the requested event.",
                                ["Event role assignment not found for the requested event."], currentOwnerAssignmentId);
                        targetUserId = target.UserId;
                        targetRoleId = target.RoleId;
                    }
                    Guid? epochId = null;
                    long revision = 0;
                    DateTime? acquiredAt = null;
                    DateTime authorityEnd = DateTime.MaxValue;
                    DateTime ceilingEnd = DateTime.MaxValue;
                    HostState? heldHost = null;
                    bool sourcesHeld = false;
                    async Task RecheckAsync(CancellationToken inner)
                    {
                        RequireContext();
                        if (!await new ModerationQualificationAuthority(context, identity, machine).ProviderTenantMatchesAsync(tenantId, inner)
                            || await identity.ResolveUserIdAsync(inner) != actor || await privacy.IsSubjectFencedAsync(actor.Value, inner)
                            || !revocation && await privacy.IsSubjectFencedAsync(targetUserId, inner)
                            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == actor && !row.IsDeleted, inner)
                            || !revocation && !await context.Users.AsNoTracking().AnyAsync(row => row.Id == targetUserId && !row.IsDeleted, inner)
                            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId && row.TenantStatus.IsActiveState, inner)
                            || !await context.Events.AsNoTracking().AnyAsync(row => row.Id == eventId && row.TenantId == tenantId, inner)
                            || !await EmailMatchesAsync(inner)) Deny();
                        if (epochId is { } id && !await context.ModerationQualificationEpochs.AsNoTracking()
                            .AnyAsync(row => row.Id == id && row.Revision == revision, inner)) Deny();
                        if (sourcesHeld)
                        {
                            var currentHost = await context.ModerationHostBindings.AsNoTracking().Select(row => new HostState(
                                row.Id, row.TrustDomainId, row.SolutionId, row.InstanceId, row.MaintenanceServicePrincipalId)).SingleOrDefaultAsync(inner);
                            if (currentHost != heldHost || heldHost is not null && !await context.ServicePrincipals.AsNoTracking()
                                .AnyAsync(row => row.Id == heldHost.MaintenanceServicePrincipalId && !row.IsDeleted, inner)) Deny();
                        }
                        AssignmentState? replacement = null;
                        if (attempted is { } owned)
                        {
                            var current = await ReadAsync(actor.Value, targetUserId, inner);
                            if (!current.SequenceEqual(owned.After) || await ReadAuditAsync(inner) != owned.Audit) throw Changed();
                            replacement = current.SingleOrDefault(row => row.Id == owned.Result.Id);
                        }
                        var now = clock.GetUtcNow().UtcDateTime;
                        // Owned revocation cannot renew or erase the original permission's clock boundary.
                        if (acquiredAt is { } start && (now < start || now >= authorityEnd)) Deny();
                        if (!transfer && attempted is { Result.IsSuccess: true } && now >= ceilingEnd) Deny();
                        if (transfer && attempted is { Result.IsSuccess: true } && (replacement is null || !Effective(replacement, now))) Deny();
                    }
                    void RequireCurrent()
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        if (acquiredAt is { } start && (now < start || now >= authorityEnd)) Deny();
                        if (!transfer && attempted is { Result.IsSuccess: true } && now >= ceilingEnd) Deny();
                        if (transfer && attempted is { Result.IsSuccess: true } owned
                            && (owned.After.SingleOrDefault(row => row.Id == owned.Result.Id) is not { } replacement
                                || !Effective(replacement, now))) Deny();
                    }
                    async Task<BaseCommandResponse<Guid>> ExecuteBodyAsync(CancellationToken inner)
                        {
                            if (joined) admission.RegisterJoined(RecheckAsync, RequireCurrent);
                            else admission.RegisterOwning(RecheckAsync, RequireCurrent);
                            await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                context, "moderation.authority-source.installation", inner);
                            var selectedHumans = new[] { actor.Value, targetUserId }.Distinct().Order().ToArray();
                            var plan = await EventAuthorityProviderOperations.ReadPlanAsync(context, tenantId, selectedHumans, [eventId], inner, includeOwners: transfer);
                            var humans = selectedHumans.Concat(plan.Assignments.Select(row => row.UserId)).Distinct().Order().ToArray();
                            await EventAuthoritySnapshotService.AcquireHumanSourcesAsync(context, tenantId, humans, plan.Assignments.Select(row => row.RoleId).Append(targetRoleId).ToArray(),
                                creation?.ExpectedEmail is null ? null : targetUserId, inner);
                            var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(inner);
                            heldHost = host is null ? null : new(host.Id, host.TrustDomainId, host.SolutionId, host.InstanceId, host.MaintenanceServicePrincipalId);
                            if (host is not null)
                            {
                                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, host.Id, inner);
                                await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, host.MaintenanceServicePrincipalId, inner);
                                if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, inner)) Deny();
                            }
                            // Native Actor source saves take Actor before Tenant; retain that order here too.
                            foreach (var id in plan.Parents.Select(row => row.ActorId)
                                .Concat(plan.Parents.Where(row => row.OrganizerActorId.HasValue).Select(row => row.OrganizerActorId!.Value)).Distinct().Order())
                                await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, id, inner);
                            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, inner);
                            var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, inner);
                            if (tenant is null) Deny();
                            await RelationalEntityRowFence.AcquireLookupAsync<TenantStatus>(context, [tenant.TenantStatusId], inner);
                            ModerationQualificationEpoch? epoch = null;
                            if (host is not null)
                            {
                                var found = await context.ModerationQualificationEpochs.AsNoTracking()
                                    .SingleOrDefaultAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, inner);
                                if (found is null) Deny();
                                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEpoch>(context, found.Id, inner);
                                epoch = await context.ModerationQualificationEpochs.SingleAsync(row => row.Id == found.Id, inner);
                                epochId = epoch.Id;
                                revision = epoch.Revision;
                                if (revision < 1) Deny();
                            }
                            sourcesHeld = true;
                            await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(context, tenantId, row => row.Id, eventId, inner);
                            var parents = await EventAuthorityProviderOperations.ReadOwnershipAsync(context, tenantId, [eventId], inner);
                            if (!plan.Parents.OrderBy(row => row.Id).SequenceEqual(parents.OrderBy(row => row.Id))) throw Changed();
                            await CheckPlanAsync();
                            foreach (var row in plan.Assignments.OrderBy(row => row.Id))
                                await RelationalEntityRowFence.AcquireAsync<EventRoleAssignment>(context, tenantId, row => row.Id, row.Id, inner);
                            await CheckPlanAsync();
                            await RecheckAsync(inner);
                            var before = await ReadAsync(actor.Value, targetUserId, inner);
                            var original = before.SingleOrDefault(row => row.Id == currentOwnerAssignmentId);
                            acquiredAt = clock.GetUtcNow().UtcDateTime;
                            if (creating)
                            {
                                if (original is not null) throw Changed();
                            }
                            else
                            {
                                if (original is null) Deny();
                                if (transfer && (original.RoleId != (int)RoleEnum.EventOwner || !Effective(original, acquiredAt.Value))) Deny();
                                if (!transfer && (original.UserId != targetUserId || original.RoleId != targetRoleId)) throw Changed();
                            }
                            string permission = transfer ? PermissionCodes.EventTransferOwnership : PermissionCodes.EventManageTeam;
                            var actorRoles = before.Where(row => row.UserId == actor).Select(row => row.RoleId).Distinct().ToArray();
                            var permissionRoles = await context.RolePermissions.AsNoTracking().Where(row => row.Permission.IsActive
                                && (actorRoles.Contains(row.RoleId) || !transfer && row.RoleId == targetRoleId))
                                .Select(row => new { row.RoleId, row.Permission.MasterCode }).ToArrayAsync(inner);
                            var transferRoles = permissionRoles.Where(row => row.MasterCode == permission).Select(row => row.RoleId).ToArray();
                            var contributors = before.Where(row => row.UserId == actor && transferRoles.Contains(row.RoleId) && Effective(row, acquiredAt.Value)).ToArray();
                            if (contributors.Length == 0) Deny();
                            authorityEnd = contributors.Any(row => row.ExpiresAtUtc is null) ? DateTime.MaxValue : contributors.Max(row => row.ExpiresAtUtc!.Value);
                            if (!transfer)
                            {
                                var required = permissionRoles.Where(row => row.RoleId == targetRoleId).Select(row => row.MasterCode)
                                    .Append(PermissionCodes.EventManageTeam).Distinct().ToArray();
                                ceilingEnd = required.Select(code =>
                                {
                                    var supportingRoles = permissionRoles.Where(row => row.MasterCode == code).Select(row => row.RoleId).ToArray();
                                    var originals = before.Where(row => row.UserId == actor && supportingRoles.Contains(row.RoleId)
                                        && Effective(row, acquiredAt.Value)).ToArray();
                                    return originals.Length == 0 ? DateTime.MinValue : originals.Any(row => row.ExpiresAtUtc is null)
                                        ? DateTime.MaxValue : originals.Max(row => row.ExpiresAtUtc!.Value);
                                }).Min();
                            }
                            await RecheckAsync(inner);
                            var result = await operation(actor.Value, inner);
                            await RecheckAsync(inner);
                            if (result.IsSuccess && !transfer && clock.GetUtcNow().UtcDateTime >= ceilingEnd) Deny();
                            var after = await ReadAsync(actor.Value, targetUserId, inner);
                            if (!result.IsSuccess && !before.SequenceEqual(after)) throw Changed();
                            if (result.IsSuccess)
                            {
                                var replacement = after.SingleOrDefault(row => row.Id == result.Id);
                                var revoked = after.SingleOrDefault(row => row.Id == currentOwnerAssignmentId);
                                var now = clock.GetUtcNow().UtcDateTime;
                                if (transfer)
                                {
                                    if (replacement is null || replacement.UserId != targetUserId || replacement.RoleId != (int)RoleEnum.EventOwner
                                        || !Effective(replacement, now) || revoked?.Status != EventRoleAssignmentStatus.Revoked
                                        || revoked.RevokedByUserId != actor) throw Changed();
                                }
                                else if (creating)
                                {
                                    if (targetRoleId == (int)RoleEnum.EventOwner || replacement is null || replacement.Id != currentOwnerAssignmentId
                                        || replacement.UserId != targetUserId || replacement.RoleId != targetRoleId || replacement.Status != creation!.Status
                                        || replacement.Status is not (EventRoleAssignmentStatus.Pending or EventRoleAssignmentStatus.Active)
                                        || replacement.StartsAtUtc != windowStartsAtUtc || replacement.ExpiresAtUtc != windowExpiresAtUtc
                                        || replacement.CreatedBy != actor || replacement.Version != 1 || replacement.RevokedAtUtc is not null
                                        || replacement.RevokedByUserId is not null || !before.SequenceEqual(after.Where(row => row.Id != replacement.Id))) throw Changed();
                                }
                                else if (revocation)
                                {
                                    if (original!.RoleId == (int)RoleEnum.EventOwner || replacement is null || replacement.Id != original.Id
                                        || replacement.UserId != original.UserId || replacement.RoleId != original.RoleId
                                        || replacement.Status != EventRoleAssignmentStatus.Revoked || replacement.RevokedByUserId != actor
                                        || replacement.RevokedAtUtc is null || replacement.StartsAtUtc != original.StartsAtUtc
                                        || replacement.ExpiresAtUtc != original.ExpiresAtUtc || replacement.CreatedAt != original.CreatedAt
                                        || replacement.CreatedBy != original.CreatedBy || replacement.Version != checked(original.Version + 1)
                                        || !before.Where(row => row.Id != original.Id).SequenceEqual(after.Where(row => row.Id != original.Id))) throw Changed();
                                }
                                else if (replacement is null || replacement.Id != original!.Id || replacement.UserId != original.UserId
                                    || replacement.RoleId != original.RoleId || replacement.Status != original.Status
                                    || replacement.StartsAtUtc != windowStartsAtUtc || replacement.ExpiresAtUtc != windowExpiresAtUtc
                                    || replacement.Version != checked(original.Version + 1)
                                    || !before.Where(row => row.Id != original.Id).SequenceEqual(after.Where(row => row.Id != original.Id))) throw Changed();
                                var audit = await ReadAuditAsync(inner);
                                string auditAction = transfer ? "EventOwnershipTransferred" : creating ? "EventRoleAssignmentCreated"
                                    : revocation ? "EventRoleAssignmentRevoked" : "EventRoleAssignmentWindowUpdated";
                                if (audit is null || audit.EntityType != nameof(EventRoleAssignment) || audit.ActorId != actor || audit.Action != auditAction
                                    || audit.EntityId != result.Id.ToString()) throw Changed();
                                long beforeRevision = revision;
                                if (epoch is not null) { epoch.Advance(revision); revision = epoch.Revision; }
                                await context.SaveChangesAsync(inner);
                                attempted = new(actor.Value, targetUserId, result, before, after, audit, epochId, beforeRevision, revision);
                            }
                            else
                            {
                                var denial = await ReadAuditAsync(inner);
                                if (denial is not null)
                                {
                                    string denialAction = transfer ? "EventOwnershipTransferDenied" : creating ? "EventRoleAssignmentDenied"
                                        : revocation ? "EventRoleAssignmentRevokeDenied" : "EventRoleAssignmentWindowUpdateDenied";
                                    if (denial.EntityType != nameof(EventRoleAssignment) || denial.ActorId != actor
                                        || denial.Action != denialAction || denial.EntityId != (creating ? eventId : currentOwnerAssignmentId).ToString()) throw Changed();
                                    attempted = new(actor.Value, targetUserId, result, before, after, denial, epochId, revision, revision);
                                }
                            }
                            return result;

                            async Task CheckPlanAsync()
                            {
                                var current = await EventAuthorityProviderOperations.ReadAssignmentsAsync(context, tenantId, selectedHumans, [eventId], inner, includeOwners: transfer);
                                if (!plan.Assignments.OrderBy(row => row.Id).SequenceEqual(current.OrderBy(row => row.Id))) throw Changed();
                            }
                        }
                    return joined ? await ExecuteBodyAsync(token)
                        : await privacy.ExecuteEnrollmentSerializedAsync(privacyToken =>
                            new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(
                                ExecuteBodyAsync, RecheckAsync, privacyToken), RecheckAsync, token);
                }
        }
        catch (Exception exception) when (attempted is not null && TransportFailure(exception))
        {
            if (!joined && await VerifyAsync(cancellationToken)) return attempted!.Result;
            throw;
        }

        async Task<bool> VerifyAsync(CancellationToken token)
        {
            if (attempted is not { } receipt) return false;
            RequireContext();
            if (await identity.ResolveUserIdAsync(token) != receipt.Actor) Deny();
            context.ChangeTracker.Clear();
            return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async inner =>
                {
                    var rows = await ReadAsync(receipt.Actor, receipt.TargetUser, inner);
                    var audit = await ReadAuditAsync(inner);
                    long? revision = receipt.EpochId is { } id ? await context.ModerationQualificationEpochs.AsNoTracking()
                        .Where(row => row.Id == id).Select(row => (long?)row.Revision).SingleOrDefaultAsync(inner) : 0;
                    if (rows.SequenceEqual(receipt.After) && audit == receipt.Audit && revision == receipt.AfterRevision) return true;
                    if (rows.SequenceEqual(receipt.Before) && audit is null && revision == receipt.BeforeRevision) { attempted = null; return false; }
                    throw Changed();
                }, privacyToken), _ => Task.CompletedTask, token);
        }
        Task<List<AssignmentState>> ReadAsync(Guid actor, Guid target, CancellationToken token) => context.EventRoleAssignments.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.EventId == eventId
                && (row.UserId == actor || row.UserId == target || transfer && row.RoleId == (int)RoleEnum.EventOwner))
            .OrderBy(row => row.Id).Select(row => new AssignmentState(row.Id, row.UserId, row.RoleId, row.Status, row.StartsAtUtc,
                row.ExpiresAtUtc, row.RevokedAtUtc, row.RevokedByUserId, row.Version, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy)).ToListAsync(token);
        Task<AuditState?> ReadAuditAsync(CancellationToken token) => context.AuditLogs.AsNoTracking()
            .Where(row => row.Id == auditId && row.TenantId == tenantId)
            .Select(row => new AuditState(row.EntityType, row.EntityId, row.Action, row.OldValues, row.NewValues, row.AffectedColumns, row.ActorId, row.Timestamp)).SingleOrDefaultAsync(token);
        async Task<bool> EmailMatchesAsync(CancellationToken token)
        {
            if (creation?.ExpectedEmail is not { } email) return true;
            var matches = await context.Users.AsNoTracking().Where(row => row.Pii != null && row.Pii.Email == email)
                .Select(row => row.Id).Take(2).ToArrayAsync(token);
            return matches.Length == 1 && matches[0] == targetUserId;
        }
        void RequireContext()
        {
            if (tenantId == Guid.Empty || !user.IsAuthenticated || machine.Current is not null
                || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId) Deny();
        }
        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        void Deny() => throw new AuthorizationException(ResourceKinds.Event, action);
    }

    private enum MutationKind { Transfer, Window, Revoke, Create }
    private sealed record Creation(int RoleId, EventRoleAssignmentStatus Status, string? ExpectedEmail)
    { public override string ToString() => nameof(Creation); }

    private static bool Effective(AssignmentState row, DateTime now) => row.Status == EventRoleAssignmentStatus.Active
        && row.StartsAtUtc <= now && (row.ExpiresAtUtc is null || row.ExpiresAtUtc > now);
    private static bool TransportFailure(Exception failure) => failure is DbException or TimeoutException or IOException
        || failure.InnerException is { } cause && TransportFailure(cause);
    private static ConcurrencyConflictException Changed() => new(ConcurrencyConflictException.ConcurrentUpdate,
        "Event assignments changed outside this mutation's exact fenced state.");
    private sealed record AssignmentState(Guid Id, Guid UserId, int RoleId, EventRoleAssignmentStatus Status,
        DateTime StartsAtUtc, DateTime? ExpiresAtUtc, DateTime? RevokedAtUtc, Guid? RevokedByUserId, long Version,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
    { public override string ToString() => nameof(AssignmentState); }
    private sealed record AuditState(string EntityType, string EntityId, string Action, string? OldValues, string? NewValues,
        string? AffectedColumns, Guid? ActorId, DateTime Timestamp)
    { public override string ToString() => nameof(AuditState); }
    private sealed record HostState(Guid Id, Guid TrustDomainId, Guid SolutionId, Guid InstanceId, Guid MaintenanceServicePrincipalId)
    { public override string ToString() => nameof(HostState); }
    private sealed record Attempt(Guid Actor, Guid TargetUser, BaseCommandResponse<Guid> Result, List<AssignmentState> Before,
        List<AssignmentState> After, AuditState Audit, Guid? EpochId, long BeforeRevision, long AfterRevision)
    { public override string ToString() => nameof(Attempt); }
}

/// <summary>Connects native owner checks to the retained human's transaction-bound finalizer.</summary>
internal sealed class NativeHumanMutationAdmission(ExploreDbContext context, IHumanActionCommitFinalizer finalizer)
{
    private int entered;

    internal void RegisterOwning(Func<CancellationToken, Task> validate, Action? requireCurrent = null)
    {
        if (!context.HasHumanCommitContextLease) return;
        if (Interlocked.Exchange(ref entered, 1) != 0)
            throw new InvalidOperationException("A retained human mutation cannot replay its original preparation.");
        // This entry is called only by the boundary's own native UoW callback. Registration
        // independently verifies the selected lease, physical transaction and credential custody.
        context.HumanCommitAttemptOwnsTransaction = true;
        finalizer.Register(validate, _ => requireCurrent?.Invoke());
    }

    internal void RegisterJoined(Func<CancellationToken, Task> validate, Action? requireCurrent = null)
    {
        context.RequireHumanCommitAdmission();
        if (Interlocked.Exchange(ref entered, 1) != 0)
            throw new InvalidOperationException("A joined native mutation may enter its original owner only once.");
        finalizer.Register(validate, _ => requireCurrent?.Invoke());
    }
}
