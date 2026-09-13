using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class AdminAuthorityScopeFreshnessTests
{
    [Test]
    public async Task PersistedPlatformOrganizationAndGroupChangesRefreshBooleansAndListsWithoutInvalidation()
    {
        var factory = await AdminAuthorityFreshnessFactory.CreateAsync();
        try
        {
            await using (factory)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var subject = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
                var group = new Group { Id = Guid.CreateVersion7(), FullName = "Authority test group" };
                var participation = new GroupTenant
                {
                    Id = Guid.CreateVersion7(), GroupId = group.Id, Group = group,
                    TenantId = subject.TenantId, Tenant = null!,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!
                };
                db.GroupMembers.Add(new GroupMember
                {
                    Id = Guid.CreateVersion7(), GroupTenantId = participation.Id, GroupTenant = participation,
                    UserId = subject.UserId, User = null!, TenantId = subject.TenantId, Tenant = null!,
                    RoleId = (int)RoleEnum.GroupAdmin, Role = null!
                });
                await db.SaveChangesAsync();
                scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject.UserId.ToString())], "Test"))
                };
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(subject.TenantId);
                var admin = scope.ServiceProvider.GetRequiredService<IAdminContext>();
                await Assert.That(await admin.IsInstanceAdminAsync(subject.UserId)).IsFalse();
                await Assert.That(await admin.IsOrganizationAdminAsync(subject.OrganizationId)).IsTrue();
                await Assert.That(await admin.IsGroupAdminAsync(group.Id)).IsTrue();
                await Assert.That(await admin.GetAdminOrganizationIdsAsync(subject.UserId)).Contains(subject.OrganizationId);
                await Assert.That(await admin.GetAdminGroupIdsAsync(subject.UserId)).Contains(group.Id);
                await Assert.That(await admin.GetAdminOrganizationIdsAsync(subject.UserId, Guid.CreateVersion7())).IsEmpty();
                await Assert.That(await admin.GetAdminGroupIdsAsync(subject.UserId, Guid.CreateVersion7())).IsEmpty();

                await using (var external = factory.ExternalDatabase())
                {
                    external.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(), UserId = subject.UserId, User = null!,
                        RoleId = (int)RoleEnum.Admin, Role = null!
                    });
                    await external.SaveChangesAsync();
                    await external.OrganizationMembers
                        .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                        .Where(row => row.TenantId == subject.TenantId && row.UserId == subject.UserId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                    await external.GroupMembers
                        .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                        .Where(row => row.TenantId == subject.TenantId && row.UserId == subject.UserId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                }
                await Assert.That(await admin.IsInstanceAdminAsync(subject.UserId)).IsTrue();
                await Assert.That(await admin.IsInstanceAdminAsync(Guid.CreateVersion7())).IsFalse();
                await Assert.That(await admin.IsOrganizationAdminAsync(subject.OrganizationId)).IsFalse();
                await Assert.That(await admin.IsGroupAdminAsync(group.Id)).IsFalse();
                await Assert.That(await admin.GetAdminOrganizationIdsAsync(subject.UserId)).IsEmpty();
                await Assert.That(await admin.GetAdminGroupIdsAsync(subject.UserId)).IsEmpty();
                await Assert.That(await admin.GetAdminOrganizationIdsAsync(subject.UserId, subject.TenantId)).IsEmpty();
                await Assert.That(await admin.GetAdminGroupIdsAsync(subject.UserId, subject.TenantId)).IsEmpty();
                await using (var external = factory.ExternalDatabase())
                {
                    await external.PlatformUserRoles.Where(row => row.UserId == subject.UserId).ExecuteDeleteAsync();
                }
                await Assert.That(await admin.IsInstanceAdminAsync()).IsFalse();
            }
        }
        finally { factory.DeleteDatabase(); }
    }
}
