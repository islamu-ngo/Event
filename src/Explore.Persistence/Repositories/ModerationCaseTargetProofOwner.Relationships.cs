using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationCaseTargetProofOwner
{
    private async Task<Relationship?> RelationshipAsync(
        ModerationCaseRelationshipKind kind, Guid id, ModerationCaseTargetSource source, NativeTarget native,
        NativeEnvironment environment, Guid caseId, bool acquire, DateTimeOffset now, CancellationToken token)
    {
        if (source.EventId is null && kind is not (ModerationCaseRelationshipKind.InstanceAdministrator
            or ModerationCaseRelationshipKind.CaseReadGrant))
            return null;
        switch (kind)
        {
            case ModerationCaseRelationshipKind.InstanceAdministrator:
                {
                    if (source.Kind is not (ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service))
                        return null;
                    // Global Actor moderation and native ServicePrincipal installation already
                    // require instance administration. A tenant admin or credential owner is not enough.
                    var admin = await AdministratorAsync(environment, environment.CallerId, id, true, acquire, token);
                    return admin is not null
                        ? new(ModerationCaseRelationshipGeneration.FromIdentity(admin.GrantId),
                            DateTimeOffset.MaxValue, $"instance-reporter:{admin.Fingerprint}") : null;
                }
            case ModerationCaseRelationshipKind.PublicEvent:
                return await PublicEventRelationshipAsync(id, source, native, acquire, token);
            case ModerationCaseRelationshipKind.EventSubmitter:
                return id == source.EventId && native.SubmitterId == environment.CallerId
                    ? new(ModerationCaseRelationshipGeneration.FromStamp(native.EventVersion!.Value),
                        DateTimeOffset.MaxValue, $"submitter:{id:D}:{native.EventVersion:D}") : null;
            case ModerationCaseRelationshipKind.EventRole:
                {
                    if (!await FenceAsync<EventRoleAssignment>(id, acquire, token)) return null;
                    var role = await context.EventRoleAssignments.AsNoTracking().SingleOrDefaultAsync(
                        row => row.Id == id && row.EventId == source.EventId && row.UserId == environment.CallerId, token);
                    if (role is null || !role.IsEffectiveAt(now.UtcDateTime) || role.RevokedAtUtc is not null
                        || role.Version < 1 || role.RoleId is < (int)RoleEnum.EventOwner or > (int)RoleEnum.EventDiscoveryReviewer)
                        return null;
                    if (acquire) await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role.RoleId], token);
                    if (!await context.Roles.AsNoTracking().AnyAsync(
                        row => row.Id == role.RoleId && row.RoleScopeId == (int)RoleScopeEnum.Event, token)) return null;
                    var until = role.ExpiresAtUtc is { } expiry ? new DateTimeOffset(expiry, TimeSpan.Zero) : DateTimeOffset.MaxValue;
                    return new(ModerationCaseRelationshipGeneration.FromRevision(role.Version), until,
                        $"role:{id:D}:{role.Version}:{role.RoleId}:{role.StartsAtUtc.Ticks}:{until.Ticks}");
                }
            case ModerationCaseRelationshipKind.RegistrationOrder:
                {
                    if (!await FenceAsync<RegistrationOrder>(id, acquire, token)) return null;
                    var order = await context.RegistrationOrders.AsNoTracking()
                        .Where(row => row.Id == id && row.EventId == source.EventId
                            && row.AccountUserId == environment.CallerId && !row.IsDeleted)
                        .Select(row => new { row.ConcurrencyStamp }).SingleOrDefaultAsync(token);
                    return order is not null && order.ConcurrencyStamp != Guid.Empty
                        ? new(ModerationCaseRelationshipGeneration.FromStamp(order.ConcurrencyStamp),
                            DateTimeOffset.MaxValue, $"order:{id:D}:{order.ConcurrencyStamp:D}") : null;
                }
            case ModerationCaseRelationshipKind.RegistrationParticipant:
                {
                    var participant = await NativeParticipantAsync(id, source.EventId!.Value, environment.CallerId, acquire, token);
                    return participant is not null
                        ? new(ModerationCaseRelationshipGeneration.FromStamp(participant.Version),
                            DateTimeOffset.MaxValue,
                            $"participant:{id:D}:{participant.Version:D}:{participant.OrderId:D}:{participant.OrderVersion:D}")
                        : null;
                }
            case ModerationCaseRelationshipKind.CaseReadGrant:
                {
                    if (!await FenceAsync<ModerationCaseReadGrant>(id, acquire, token)) return null;
                    var grant = await context.ModerationCaseReadGrants.AsNoTracking().SingleOrDefaultAsync(row =>
                        row.Id == id && row.CaseId == caseId && row.SourceId == source.Id
                        && row.CallerUserId == environment.CallerId, token);
                    if (grant is null || !grant.IsCurrentAt(now)) return null;
                    if (_protectedReadPlan is { } protectedRead && ReadGrantCoordinates.From(grant) != protectedRead.Sources.Grant)
                        return null;
                    if (!await FenceAsync<ModerationCase>(caseId, acquire, token)
                        || !await context.ModerationCases.AsNoTracking().AnyAsync(row =>
                            row.Id == caseId && row.Revision == grant.CaseRevision
                            && row.ScopeTenantId == environment.TenantId, token)) return null;
                    var issuer = await AdministratorAsync(environment, grant.IssuerUserId,
                        grant.IssuerRoleGrantId, grant.IssuerPlatformGrant, acquire, token);
                    return issuer is not null
                        ? new(ModerationCaseRelationshipGeneration.FromRevision(grant.Revision), grant.ExpiresAt,
                            $"read:{grant.Id:D}:{grant.Revision}:{grant.CaseId:D}:{grant.SourceId:D}:{issuer.Fingerprint}") : null;
                }
            default:
                return null;
        }
    }

    /// <summary>Uses existing native administration semantics and exact grant rows, not claim strings.</summary>
    private async Task<Administrator?> AdministratorAsync(NativeEnvironment environment,
        Guid userId, Guid? selectedGrant, bool? platformGrant, bool acquire, CancellationToken token)
    {
        if (!await AccountAsync(userId, acquire, token)) return null;
        var platform = platformGrant == false ? null : await context.PlatformUserRoles.AsNoTracking()
            .Where(row => row.UserId == userId && (selectedGrant == null || row.Id == selectedGrant)
                && row.RoleId == (int)RoleEnum.Admin)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).FirstOrDefaultAsync(token);
        if (platform is not null)
        {
            if (!await FenceAsync<PlatformUserRole>(platform.Id, acquire, token)) return null;
            if (acquire) await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [platform.RoleId], token);
            var current = await context.PlatformUserRoles.AsNoTracking().Where(row => row.Id == platform.Id
                && row.UserId == userId && row.RoleId == (int)RoleEnum.Admin
                && row.Role.MasterCode == "platform.admin" && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform)
                .Select(row => new { row.GrantedAt, row.GrantedBy }).SingleOrDefaultAsync(token);
            if (current is not null)
                return new(platform.Id, true, $"platform:{platform.Id:D}:{userId:D}:{current.GrantedAt.Ticks}:{current.GrantedBy:D}");
        }
        if (platformGrant == true) return null;
        var found = await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            row.TenantId == environment.TenantId && row.TenantUser.UserId == userId
            && row.RoleId == (int)RoleEnum.TenantAdmin && (selectedGrant == null || row.Id == selectedGrant))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.TenantUserId, row.RoleId }).FirstOrDefaultAsync(token);
        if (found is null || !await FenceAsync<TenantUser>(found.TenantUserId, acquire, token)
            || !await FenceAsync<TenantUserRoleGrant>(found.Id, acquire, token)) return null;
        if (acquire) await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [found.RoleId], token);
        var qualified = await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            row.Id == found.Id && row.TenantId == environment.TenantId && row.TenantUserId == found.TenantUserId
            && row.TenantUser.UserId == userId && !row.TenantUser.IsDeleted
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && row.RevokedAt == null
            && row.RoleId == (int)RoleEnum.TenantAdmin && row.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant)
            .Select(row => new { row.GrantedAt, row.GrantedBy }).SingleOrDefaultAsync(token);
        return qualified is null ? null : new(found.Id, false,
            $"tenant:{found.Id:D}:{found.TenantUserId:D}:{userId:D}:{qualified.GrantedAt.Ticks}:{qualified.GrantedBy:D}");
    }

    private sealed record Administrator(Guid GrantId, bool Platform, string Fingerprint);
}
