using System.Data;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanAdministratorCommitFenceTests
{
    [Test]
    public async Task PortableSubjectFenceRetainsOrdinarySelfWithoutAdministratorAndRejectsDeletedAccount()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var subject = await HumanCommitSubject.ResolveAsync(http.HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            default);
        await context.PlatformUserRoles.Where(row => row.UserId == fixture.UserId).ExecuteDeleteAsync();
        var scope = new HumanAdministratorCommitScope(context, subject!,
            HumanAdministratorCommitPurpose.RuntimeTenant);
        IHumanSubjectCommitAuthority authority = new ExternalApiKeyIssuanceAuthority(context, http, scope);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await authority.IsSubjectCurrentForCommitAsync(fixture.UserId, default)).IsTrue();
            await Assert.That(await authority.IsSubjectCurrentForCommitAsync(Guid.CreateVersion7(), default)).IsFalse();
            await Assert.That(await ((IInstanceAdministratorCommitAuthority)new ExternalApiKeyIssuanceAuthority(context, http, scope))
                .IsInstanceAdministratorForCommitAsync(fixture.UserId, default)).IsFalse();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(owner);
            await owner.RollbackAsync();
        }
        await context.Users.Where(row => row.Id == fixture.UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await using var nextOwner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await authority.IsSubjectCurrentForCommitAsync(fixture.UserId, default)).IsFalse();
        await nextOwner.RollbackAsync();
    }

    [Test]
    public async Task PortableFenceRejectsUnboundAndForgedActorsAndRetainsOwningTransaction()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var subject = await HumanCommitSubject.ResolveAsync(http.HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None);
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        var scope = new HumanAdministratorCommitScope(context, subject!,
            HumanAdministratorCommitPurpose.RuntimeTenant);
        IInstanceAdministratorCommitAuthority authority = new ExternalApiKeyIssuanceAuthority(context, http, scope);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.IsInstanceAdministratorForCommitAsync(fixture.UserId, CancellationToken.None));
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await ((IInstanceAdministratorCommitAuthority)new ExternalApiKeyIssuanceAuthority(context, http))
            .IsInstanceAdministratorForCommitAsync(fixture.UserId, CancellationToken.None)).IsFalse();
        await Assert.That(await new ExternalApiKeyIssuanceAuthority(context, http).IsAuthorizedForCommitAsync(
            fixture.UserId, null, ExternalApiKeyOwnerType.InstanceAdmin, fixture.UserId, CancellationToken.None)).IsTrue();
        await Assert.That(await new ExternalApiKeyIssuanceAuthority(context, http)
            .IsInstanceAdministratorForCommitAsync(fixture.UserId, CancellationToken.None)).IsTrue();
        await Assert.That(await authority.IsInstanceAdministratorForCommitAsync(
            Guid.CreateVersion7(), CancellationToken.None)).IsFalse();
        // The immutable subject, not a subsequently replaced ambient/audit identity, owns the scope.
        http.HttpContext.User = new ClaimsPrincipal();
        await Assert.That(await authority.IsInstanceAdministratorForCommitAsync(
            fixture.UserId, CancellationToken.None)).IsTrue();
        await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, owner)).IsTrue();
        await owner.RollbackAsync();
    }

    [Test]
    public async Task PortableTenantFenceRechecksRevocationAndRollsBackBusinessWrite()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        var subject = await HumanCommitSubject.ResolveAsync(http.HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None);
        var membership = await context.TenantUsers.SingleAsync(member => member.UserId == fixture.UserId);
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        context.TenantUserRoleGrants.Add(grant);
        await context.SaveChangesAsync();
        var scope = new HumanAdministratorCommitScope(context, subject!,
            HumanAdministratorCommitPurpose.RuntimeTenant);
        ITenantAdministratorCommitAuthority authority = new ExternalApiKeyIssuanceAuthority(context, http, scope);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await authority.IsTenantAdministratorForCommitAsync(
                fixture.UserId, Guid.CreateVersion7(), CancellationToken.None)).IsFalse();
            await Assert.That(await authority.IsTenantAdministratorForCommitAsync(
                fixture.UserId, fixture.TenantId, CancellationToken.None)).IsTrue();
            await context.Tenants.Where(tenant => tenant.Id == fixture.TenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(tenant => tenant.FullName, "Uncommitted"));
            await owner.RollbackAsync();
        }
        await context.TenantUserRoleGrants.Where(value => value.Id == grant.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(value => value.RevokedAt, DateTime.UtcNow));
        await using var retry = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await authority.IsTenantAdministratorForCommitAsync(
            fixture.UserId, fixture.TenantId, CancellationToken.None)).IsFalse();
        await Assert.That(await context.Tenants.Where(tenant => tenant.Id == fixture.TenantId)
            .Select(tenant => tenant.FullName).SingleAsync()).IsEqualTo("Visitor authority");
        await retry.RollbackAsync();
    }
}
