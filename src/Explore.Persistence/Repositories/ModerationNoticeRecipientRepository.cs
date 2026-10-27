using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Current recipient authority, independently read from the decision's original explanation.
/// No service/API-key owner is converted to a human notice recipient.
/// </summary>
public sealed class ModerationNoticeRecipientRepository(ExploreDbContext context)
{
    public async Task<IReadOnlyList<ModerationReviewerAssignment>> ReadReviewersAsync(
        ModerationDecision decision, ModerationNoticeReviewerQualification qualification,
        DateTimeOffset now, CancellationToken token, Guid? reviewerHumanId = null)
    {
        RequireTransaction(decision.ScopeTrustDomainId);
        if (qualification.CaseId != decision.CaseId || qualification.Scope != decision.Scope
            || qualification.CurrentSource != qualification.ApprovedSource
            || qualification.MaximumLifetime <= TimeSpan.Zero
            || qualification.ObservedAt != now || qualification.ValidUntil <= now)
            return [];
        var candidates = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == decision.CaseId && row.WithdrawnAt == null
                && (reviewerHumanId == null || row.ReviewerHumanId == reviewerHumanId))
            .OrderBy(row => row.Id).ToListAsync(token);
        var reviewers = new List<ModerationReviewerAssignment>();
        foreach (var discovered in candidates)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationReviewerAssignment>(context, discovered.Id, token);
            var row = await context.ModerationReviewerAssignments.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == discovered.Id, token);
            if (row is null) continue;
            row.ValidatePersistedState();
            if (!row.IsQualifiedAt(now) || row.CaseId != decision.CaseId
                || row.TrustDomainId != decision.ScopeTrustDomainId || row.CaseTenantId != decision.ScopeTenantId
                || row.SubjectHumanId != (decision.Subject.Kind == ModerationSubjectKind.Human
                    ? decision.Subject.Id : (Guid?)null)
                || row.ReviewerHumanId == decision.SubjectId || row.ReviewerHumanId == decision.IssuerId
                || row.ControllerHumanId == decision.SubjectId || row.ControllerHumanId == decision.IssuerId
                || row.ControllerHumanId is not { } controller || row.QualifiedByHumanId is not { } qualifier
                || row.AssessedByHumanId is not { } assessor
                || row.IdentityEvidenceId is not { } identityId || row.ConflictEvidenceId is not { } conflictId
                || qualifier == row.ReviewerHumanId || qualifier == controller || qualifier == decision.SubjectId
                || assessor == row.ReviewerHumanId || assessor == controller || assessor == decision.SubjectId)
                continue;

            var humans = new[] { row.ReviewerHumanId, controller, qualifier, assessor }.Distinct().Order().ToArray();
            foreach (var id in humans)
                await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
            if (await context.Users.AsNoTracking().CountAsync(item => humans.Contains(item.Id) && !item.IsDeleted, token)
                != humans.Length
                || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(item => humans.Contains(item.SubjectId), token)
                || !await AdministratorAsync(row.ReviewerHumanId, decision.ScopeTenantId, token)
                || !await AdministratorAsync(qualifier, decision.ScopeTenantId, token)
                || !await AdministratorAsync(assessor, decision.ScopeTenantId, token))
                continue;

            foreach (var id in new[] { identityId, conflictId }.Distinct().Order())
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, id, token);
            var identity = await context.ModerationQualificationEvidence.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == identityId, token);
            var conflict = await context.ModerationQualificationEvidence.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == conflictId, token);
            if (identity is null || conflict is null) continue;
            identity.ValidatePersistedState();
            conflict.ValidatePersistedState();
            if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
                || conflict.Purpose != ModerationQualificationEvidencePurpose.Conflict
                || identity.CaseId != row.CaseId || conflict.CaseId != row.CaseId
                || identity.TrustDomainId != row.TrustDomainId || conflict.TrustDomainId != row.TrustDomainId
                || identity.TenantId != context.TenantFilterTenantId || conflict.TenantId != context.TenantFilterTenantId
                || identity.ReviewerHumanId != row.ReviewerHumanId || conflict.ReviewerHumanId != row.ReviewerHumanId
                || identity.ControllerHumanId != controller || conflict.ControllerHumanId != controller
                || identity.AssertedByHumanId != qualifier || conflict.AssertedByHumanId != assessor
                || identity.ValidUntil != row.QualifiedUntil || conflict.ValidUntil != row.QualifiedUntil
                || identity.ValidUntil - identity.RecordedAt > qualification.MaximumLifetime
                || conflict.ValidUntil - conflict.RecordedAt > qualification.MaximumLifetime
                || identity.RecordedAt > conflict.RecordedAt || conflict.RecordedAt > now
                || conflict.ConflictState != (int)ModerationReviewerConflictState.Independent)
                continue;
            reviewers.Add(row);
        }
        return reviewers;
    }

    public async Task<User?> ReadTenantRecipientAsync(
        Guid trustDomainId, Guid tenantId, Guid userId, CancellationToken token)
    {
        RequireTransaction(trustDomainId);
        if (tenantId != context.TenantFilterTenantId)
            return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, userId, token);
        if (await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == userId, token))
            return null;
        var membership = await context.TenantUsers.AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == tenantId && row.UserId == userId && !row.IsDeleted, token);
        if (membership is null) return null;
        await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenantId, row => row.Id, membership.Id, token);
        // Suspension does not remove required remedy notices. Deleted membership does remove native
        // tenant projection; the independent dedicated portal occurrence remains available.
        if (!await context.TenantUsers.AsNoTracking().AnyAsync(
                row => row.Id == membership.Id && row.TenantId == tenantId && row.UserId == userId && !row.IsDeleted, token))
            return null;
        return await context.Users.AsNoTracking().Include(row => row.IdentityEmailClaims)
            .ThenInclude(row => row.Evidence).SingleOrDefaultAsync(row => row.Id == userId && !row.IsDeleted, token);
    }

    private async Task<bool> AdministratorAsync(Guid userId, Guid? tenantId, CancellationToken token)
    {
        var platform = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == userId)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in platform)
            await RelationalEntityRowFence.AcquireGlobalAsync<PlatformUserRole>(context, grant.Id, token);
        foreach (var role in platform.Select(row => row.RoleId).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], token);
        var platformIds = platform.Select(row => row.Id).ToArray();
        if (await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => platformIds.Contains(row.Id)
                && row.UserId == userId && !row.User.IsDeleted
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin", token))
            return true;
        if (tenantId is not { } tenant || tenant != context.TenantFilterTenantId)
            return false;
        var membership = await context.TenantUsers.AsNoTracking().SingleOrDefaultAsync(
            row => row.TenantId == tenant && row.UserId == userId, token);
        if (membership is null) return false;
        await RelationalEntityRowFence.AcquireAsync<TenantUser>(context, tenant, row => row.Id, membership.Id, token);
        var grants = await context.TenantUserRoleGrants.AsNoTracking().Where(row =>
            row.TenantId == tenant && row.TenantUserId == membership.Id).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.RoleId }).ToListAsync(token);
        foreach (var grant in grants)
            await RelationalEntityRowFence.AcquireAsync<TenantUserRoleGrant>(context, tenant, row => row.Id, grant.Id, token);
        foreach (var role in grants.Select(row => row.RoleId).Distinct().Order())
            await RelationalEntityRowFence.AcquireLookupAsync<Role>(context, [role], token);
        var grantIds = grants.Select(row => row.Id).ToArray();
        return await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row => grantIds.Contains(row.Id)
            && row.TenantId == tenant && row.TenantUserId == membership.Id && row.RevokedAt == null
            && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
            && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant && !row.TenantUser.IsDeleted
            && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active && !row.TenantUser.User.IsDeleted, token);
    }

    private void RequireTransaction(Guid trustDomainId)
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId != trustDomainId)
            throw new InvalidOperationException("Notice recipients require the native moderation source transaction.");
    }
}
