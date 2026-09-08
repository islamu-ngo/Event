using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class SupportAccessRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SessionRepository_UsesActorTenantAndActivePredicates()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var scope = await SeedScopeAsync(context, "support-access-repository");
        var startedAt = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero);
        var session = NewSession(scope, startedAt);
        var sessionRepository = new SupportAccessSessionRepository(context);
        var auditRepository = new SupportAccessAuditEventRepository(context);

        await sessionRepository.CreateAsync(session, CancellationToken.None);
        await auditRepository.CreateAsync(
            SupportAccessAuditEvent.CreateLifecycleEvent(
                session,
                SupportAccessAuditEventTypeEnum.Started,
                "started",
                startedAt),
            CancellationToken.None);

        var owned = await sessionRepository.GetActiveOwnedSessionAsync(
            session.Id,
            scope.ActorUser.Id,
            scope.TargetTenant.Id,
            startedAt.AddMinutes(1),
            CancellationToken.None);
        var wrongTenant = await sessionRepository.GetActiveOwnedSessionAsync(
            session.Id,
            scope.ActorUser.Id,
            Guid.NewGuid(),
            startedAt.AddMinutes(1),
            CancellationToken.None);
        var wrongActor = await sessionRepository.GetActiveOwnedSessionAsync(
            session.Id,
            Guid.NewGuid(),
            scope.TargetTenant.Id,
            startedAt.AddMinutes(1),
            CancellationToken.None);
        var expiredByClock = await sessionRepository.GetActiveOwnedSessionAsync(
            session.Id,
            scope.ActorUser.Id,
            scope.TargetTenant.Id,
            startedAt.AddHours(1),
            CancellationToken.None);

        var tenantSessions = await sessionRepository.ListForTargetTenantAsync(scope.TargetTenant.Id, 10, CancellationToken.None);
        var foreignTenantSessions = await sessionRepository.ListForTargetTenantAsync(Guid.NewGuid(), 10, CancellationToken.None);
        var sessionAudit = await auditRepository.ListForSessionAsync(session.Id, 10, CancellationToken.None);
        var tenantAudit = await auditRepository.ListForTargetTenantAsync(scope.TargetTenant.Id, 10, CancellationToken.None);

        await Assert.That(owned).IsNotNull();
        await Assert.That(wrongTenant).IsNull();
        await Assert.That(wrongActor).IsNull();
        await Assert.That(expiredByClock).IsNull();
        await Assert.That(tenantSessions.Select(x => x.Id)).IsEquivalentTo([session.Id]);
        await Assert.That(foreignTenantSessions).IsEmpty();
        await Assert.That(sessionAudit.Select(x => x.SupportAccessSessionId)).IsEquivalentTo([session.Id]);
        await Assert.That(tenantAudit.Select(x => x.TargetTenantId)).IsEquivalentTo([scope.TargetTenant.Id]);
    }

    [Test]
    public async Task SessionRepository_DatabaseRejectsSecondActiveSessionForSameActor()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var scope = await SeedScopeAsync(context, "support-access-active-unique");
        var startedAt = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var repository = new SupportAccessSessionRepository(context);

        await repository.CreateAsync(NewSession(scope, startedAt), CancellationToken.None);

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await repository.CreateAsync(NewSession(scope, startedAt.AddMinutes(1)), CancellationToken.None);
        });
    }

    private static SupportAccessSession NewSession(SupportAccessScope scope, DateTimeOffset startedAt)
    {
        return SupportAccessSession.Start(
            scope.ActorUser.Id,
            scope.TargetTenant.Id,
            SupportAccessModeEnum.ReadOnly,
            "support_case",
            "Investigating tenant issue",
            "TICKET-123",
            startedAt,
            startedAt.AddMinutes(30),
            scope.TargetTenantUser.Id);
    }

    private static async Task<SupportAccessScope> SeedScopeAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = "Support Access " + slugPrefix,
            Slug = slugPrefix + "-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        var actorUser = NewUser("actor");
        var targetUser = NewUser("target");

        context.Tenants.Add(tenant);
        context.Users.AddRange(actorUser, targetUser);
        await context.SaveChangesAsync();

        var tenantUser = new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = null!,
            UserId = targetUser.Id,
            User = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };

        context.TenantUsers.Add(tenantUser);
        await context.SaveChangesAsync();

        return new SupportAccessScope(tenant, actorUser, tenantUser);
    }

    private static User NewUser(string prefix) =>
        new()
        {
            Id = Guid.NewGuid(),
            Pii = new UserPii
            {
                Email = "support-access-" + prefix + "-" + Guid.NewGuid().ToString("N")[..8] + "@example.com",
                FirstName = "Support",
                LastName = prefix,
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };

    private sealed record SupportAccessScope(Tenant TargetTenant, User ActorUser, TenantUser TargetTenantUser);
}
