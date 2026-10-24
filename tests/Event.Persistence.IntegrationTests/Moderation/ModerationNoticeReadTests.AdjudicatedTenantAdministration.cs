using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Custody_tenant_administration_issues_a_case_grant_but_cannot_survive_its_role_or_membership_loss(
        bool removeMembership)
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        var original = await SeedCreatingNoticeLedgerAsync(fixture);
        var request = AdjudicatedGrantRequest(fixture, original);
        Guid issuerRoleId = Guid.CreateVersion7();
        Guid issuerMembershipId = Guid.Empty;
        await fixture.MutateAsync(async context =>
        {
            await context.PlatformUserRoles.Where(row => row.UserId == fixture.Database.AdministratorId)
                .ExecuteDeleteAsync();
            var membership = await context.TenantUsers.SingleAsync(row => row.TenantId == fixture.Database.TenantId
                && row.UserId == fixture.Database.AdministratorId);
            issuerMembershipId = membership.Id;
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = issuerRoleId,
                TenantId = fixture.Database.TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                GrantedBy = fixture.Database.AdministratorId
            });
        });

        await using (var writer = fixture.Database.Context())
        {
            var grant = await AdjudicatedGrants(writer, fixture, fixture.Database.AdministratorId).GrantAsync(request);
            await Assert.That(grant).IsNotNull();
            await Assert.That(grant!.IssuerUserId).IsEqualTo(fixture.Database.AdministratorId);
            await Assert.That(grant.TenantId).IsEqualTo(fixture.Database.TenantId);
            await Assert.That(grant.IssuerRoleGrantId).IsEqualTo(issuerRoleId);
            await Assert.That(grant.IssuerPlatformGrant).IsFalse();
        }
        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNotNull();

        await fixture.MutateAsync(async context =>
        {
            if (removeMembership)
            {
                var membership = await context.TenantUsers.SingleAsync(row => row.Id == issuerMembershipId);
                membership.IsDeleted = true;
            }
            else
            {
                var role = await context.TenantUserRoleGrants.SingleAsync(row => row.Id == issuerRoleId);
                role.RevokedAt = fixture.Clock.GetUtcNow().UtcDateTime;
                role.RevokedBy = fixture.Database.AdministratorId;
            }
        });

        await using (var reader = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(reader, fixture, request.GranteeUserId)
                .ReadCurrentAsync(request.OperationId, request.CaseId, request.ExpectedCaseRevision)).IsNull();
        await using (var issuer = fixture.Database.Context())
            await Assert.That(await AdjudicatedGrants(issuer, fixture, fixture.Database.AdministratorId)
                .GrantAsync(request)).IsNull();
        await using (var inspect = fixture.Database.Context())
        {
            inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var retained = await inspect.Set<ModerationAdjudicatedCaseReadGrant>()
                .AsNoTracking().SingleAsync(row => row.Id == request.OperationId);
            await Assert.That(retained.IssuerRoleGrantId).IsEqualTo(issuerRoleId);
            await Assert.That(retained.IssuerPlatformGrant).IsFalse();
            await Assert.That(retained.Revision).IsEqualTo(1L);
            await Assert.That(retained.RevokedAt).IsNull();
        }
        await AssertOriginalAdjudicatedLedgerUnchangedAsync(fixture, original);
    }
}
