using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class AdminAuthorityFreshnessTests
{
    private const string Grants = "/api/tenant-user-role-grants";
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task CommittedHttpAndExternalGrantChangesReachIndependentHostsWithUnchangedClaims()
    {
        var factory = await AdminAuthorityFreshnessFactory.CreateAsync();
        try
        {
            await using (factory)
            {
                var seed = await SeedAsync(factory);
                await using var otherHost = factory.IndependentHost();
                using var first = Client(factory, seed.UserId);
                using var second = Client(otherHost, seed.UserId);
                using var administrator = Client(factory, seed.OperatorId);
                await StatusAsync(first, HttpStatusCode.OK);
                await StatusAsync(second, HttpStatusCode.OK);

                using var revoked = await administrator.DeleteAsync($"{Grants}/{seed.GrantId}");
                await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
                await StatusAsync(first, HttpStatusCode.Forbidden);
                await StatusAsync(second, HttpStatusCode.Forbidden);

                // Both hosts have now observed denial. A real grant must become usable without changing claims.
                using var created = await administrator.PostAsJsonAsync(Grants, new CreateTenantUserRoleGrantDto
                {
                    TenantUserId = seed.TenantUserId,
                    RoleId = (int)RoleEnum.TenantAdmin
                });
                await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
                var grant = await created.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
                await Assert.That(grant!.IsSuccess).IsTrue();
                await StatusAsync(first, HttpStatusCode.OK);
                await StatusAsync(second, HttpStatusCode.OK);

                // No application mutation handler or cache invalidator participates in this write.
                await RevokeExternallyAsync(factory, grant.Id);
                await StatusAsync(first, HttpStatusCode.Forbidden);
                await StatusAsync(second, HttpStatusCode.Forbidden);
                await StatusAsync(administrator, HttpStatusCode.OK);
            }
        }
        finally { factory.DeleteDatabase(); }
    }

    [Test]
    public async Task NondefaultTenantRevocationAndRollbackPreserveOtherTenantAndExplicitUserAuthority()
    {
        var factory = await AdminAuthorityFreshnessFactory.CreateAsync();
        try
        {
            await using (factory)
            {
                var seed = await SeedAsync(factory);
                using var client = Client(factory, seed.UserId);
                using var scope = AuthorityScope(factory, seed.UserId, seed.ForeignTenantId);
                var admin = scope.ServiceProvider.GetRequiredService<IAdminContext>();
                var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                await Assert.That(await admin.IsTenantAdminAsync(seed.ForeignTenantId)).IsTrue();
                await Assert.That(await admin.GetAdminTenantIdsAsync(seed.UserId)).Contains(seed.ForeignTenantId);
                await Assert.That(await admin.GetAdminTenantIdsAsync(seed.OperatorId)).DoesNotContain(seed.ForeignTenantId);

                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    await db.TenantUserRoleGrants.Where(row => row.Id == seed.ForeignGrantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
                    await Assert.That(await admin.IsTenantAdminAsync(seed.ForeignTenantId)).IsFalse();
                    await transaction.RollbackAsync();
                }
                // No transaction-local denial may survive rollback, even in this same DI scope.
                await Assert.That(await admin.IsTenantAdminAsync(seed.ForeignTenantId)).IsTrue();

                using var foreignDelete = await client.DeleteAsync($"{Grants}/{seed.ForeignGrantId}");
                await Assert.That(foreignDelete.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
                await Assert.That(await admin.IsTenantAdminAsync(seed.ForeignTenantId)).IsTrue();
                await RevokeExternallyAsync(factory, seed.ForeignGrantId);
                await Assert.That(await admin.IsTenantAdminAsync(seed.ForeignTenantId)).IsFalse();
                await Assert.That(await admin.GetAdminTenantIdsAsync(seed.UserId)).DoesNotContain(seed.ForeignTenantId);
                await Assert.That(await admin.GetAdminTenantIdsAsync(seed.UserId)).Contains(PlatformDefaults.DefaultTenantId);
                await StatusAsync(client, HttpStatusCode.OK);
            }
        }
        finally { factory.DeleteDatabase(); }
    }

    [Test]
    public async Task EarlierDatabaseReadCannotRepopulateAuthorityForTheNextRequest()
    {
        var gate = new TenantAuthorityReadGate();
        var factory = await AdminAuthorityFreshnessFactory.CreateAsync(gate);
        try
        {
            await using (factory)
            {
                var seed = await SeedAsync(factory);
                using var client = Client(factory, seed.UserId);
                // Subscribe/arm before dispatch. Only the tenant-admin EXISTS query is held.
                gate.Arm();
                var earlier = client.GetAsync(Grants);
                HttpStatusCode earlierStatus;
                try
                {
                    await gate.ReaderOpened.Task.WaitAsync(SignalTimeout);
                    await RevokeExternallyAsync(factory, seed.GrantId);
                }
                finally
                {
                    gate.Release.TrySetResult();
                    using var earlierResponse = await earlier.WaitAsync(SignalTimeout);
                    earlierStatus = earlierResponse.StatusCode;
                }
                // The pre-commit reader owns an older snapshot; this test does not demand retroactive denial.
                await Assert.That(earlierStatus).IsEqualTo(HttpStatusCode.OK);
                await StatusAsync(client, HttpStatusCode.Forbidden);
            }
        }
        finally { gate.Release.TrySetResult(); factory.DeleteDatabase(); }
    }

    private static HttpClient Client(AdminAuthorityFreshnessFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static async Task StatusAsync(HttpClient client, HttpStatusCode expected)
    {
        using var response = await client.GetAsync(Grants);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        if (expected == HttpStatusCode.Forbidden)
        {
            var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
            await Assert.That(problem!.Status).IsEqualTo(403);
        }
    }

    private static IServiceScope AuthorityScope(AdminAuthorityFreshnessFactory factory, Guid userId, Guid tenantId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())], "Test"))
        };
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        return scope;
    }

    private static async Task RevokeExternallyAsync(AdminAuthorityFreshnessFactory factory, Guid grantId)
    {
        await using var db = factory.ExternalDatabase();
        var affected = await db.TenantUserRoleGrants
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Where(row => row.Id == grantId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
        await Assert.That(affected).IsEqualTo(1);
    }

    private static async Task<Seed> SeedAsync(AdminAuthorityFreshnessFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var subject = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var administrator = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.SingleAsync(row => row.UserId == subject.UserId);
        var operatorMembership = await db.TenantUsers.SingleAsync(row => row.UserId == administrator.UserId);
        var foreignMembership = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = foreign.TenantId,
            Tenant = null!,
            UserId = subject.UserId,
            User = null!,
            ActorId = subject.ActorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.TenantUsers.Add(foreignMembership);
        var grant = Grant(membership);
        var foreignGrant = Grant(foreignMembership);
        db.TenantUserRoleGrants.AddRange(grant, foreignGrant, Grant(operatorMembership));
        await db.SaveChangesAsync();
        return new(subject.UserId, administrator.UserId, membership.Id, grant.Id, foreign.TenantId, foreignGrant.Id);
    }

    private static TenantUserRoleGrant Grant(TenantUser membership) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = membership.TenantId,
        Tenant = null!,
        TenantUserId = membership.Id,
        TenantUser = membership,
        RoleId = (int)RoleEnum.TenantAdmin,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant
    };

    private sealed record Seed(Guid UserId, Guid OperatorId, Guid TenantUserId, Guid GrantId,
        Guid ForeignTenantId, Guid ForeignGrantId);

    private sealed class TenantAuthorityReadGate : DbCommandInterceptor
    {
        private int _armed;
        public TaskCompletionSource ReaderOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Arm() => Interlocked.Exchange(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("tenant_user_role_grants", StringComparison.Ordinal)
                && command.CommandText.Contains("EXISTS", StringComparison.Ordinal)
                && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                ReaderOpened.TrySetResult();
                await Release.Task.WaitAsync(SignalTimeout, cancellationToken);
            }
            return result;
        }
    }
}
