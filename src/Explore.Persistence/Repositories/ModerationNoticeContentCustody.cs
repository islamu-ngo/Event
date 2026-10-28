using System.Collections.Immutable;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Qualifies explicitly approved original representatives through the native Event authority
/// owner. Human discovery and its privacy/membership fences must precede this native acquisition.
/// No administrator, submitter or replacement manager is inferred as a disclosure recipient.
/// </summary>
internal static class ModerationNoticeContentCustody
{
    internal static async Task<DateTimeOffset?> AcquireAsync(
        ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy,
        SubjectReference subject, AuthorityScope tenant,
        ImmutableArray<ModerationNoticeContentRepresentative> representatives,
        IReadOnlyCollection<Guid> fencedHumans, DateTimeOffset observedAt,
        DateTimeOffset validUntil, CancellationToken token,
        EventAuthorityProviderOperations.AuthorityPlan? acquiredPlan = null)
    {
        if (representatives.IsDefault || representatives.Length > 32
            || context.Database.CurrentTransaction is null || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != tenant.Id || tenant.Kind != ModerationScopeKind.Tenant
            || context.ModerationTrustDomainId != tenant.TrustDomainId || validUntil <= observedAt)
            return null;
        if (subject.Kind != ModerationSubjectKind.Content)
            return representatives.IsEmpty ? validUntil : null;
        if (subject.Origin != tenant || representatives.IsEmpty
            || representatives.Any(row => row is null || !fencedHumans.Contains(row.HumanId)
                || row.AssignmentId == Guid.Empty || row.AssignmentRevision is < 1 or long.MaxValue)
            || representatives.Select(row => row.AssignmentId).Distinct().Count() != representatives.Length
            || representatives.Select(row => row.HumanId).Distinct().Count() != representatives.Length)
            return null;

        var snapshots = await new EventAuthoritySnapshotService(context).GetCommitBoundForUsersAndEventsAsync(
            tenant.Id, representatives.Select(row => row.HumanId).ToArray(), [subject.Id], observedAt.UtcDateTime, token, acquiredPlan);
        foreach (var representative in representatives.OrderBy(row => row.HumanId))
        {
            var authority = snapshots[representative.HumanId];
            if (!authority.Events.TryGetValue(subject.Id, out var rights) || !rights.IsOwner && !rights.IsManager)
                return null;
        }
        if (!await context.Events.AsNoTracking().AnyAsync(
            row => row.Id == subject.Id && row.TenantId == tenant.Id && !row.IsDeleted, token))
            return null;
        foreach (var representative in representatives.OrderBy(row => row.AssignmentId))
        {
            var assignment = await context.EventRoleAssignments.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == representative.AssignmentId
                    && row.TenantId == tenant.Id && row.EventId == subject.Id
                    && row.UserId == representative.HumanId, token);
            if (assignment is null || assignment.Version != representative.AssignmentRevision
                || assignment.RoleId is not ((int)RoleEnum.EventOwner) and not ((int)RoleEnum.EventManager)
                || assignment.RevokedAtUtc is not null || !assignment.IsEffectiveAt(observedAt.UtcDateTime)
                || !await context.Roles.AsNoTracking().AnyAsync(row => row.Id == assignment.RoleId
                    && row.RoleScopeId == (int)RoleScopeEnum.Event
                    && row.MasterCode == (assignment.RoleId == (int)RoleEnum.EventOwner ? "event.owner" : "event.manager"), token)
                || !await context.TenantUsers.AsNoTracking().AnyAsync(row => row.TenantId == tenant.Id
                    && row.UserId == representative.HumanId && !row.IsDeleted
                    && row.StatusId == (int)TenantUserStatusEnum.Active && !row.User.IsDeleted, token)
                || await context.PrivacyErasureSagas.AsNoTracking()
                    .AnyAsync(row => row.SubjectId == representative.HumanId, token)
                || await privacy.IsSubjectFencedAsync(representative.HumanId, token))
                return null;
            if (assignment.ExpiresAtUtc is { } expiresAt && expiresAt < validUntil.UtcDateTime)
                validUntil = new(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc));
        }
        return validUntil > observedAt ? validUntil : null;
    }
}
