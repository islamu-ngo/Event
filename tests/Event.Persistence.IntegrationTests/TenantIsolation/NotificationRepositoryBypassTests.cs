using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class NotificationRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task DeduplicationBypass_WithAmbientTenant_ReturnsOnlyExactTenantUserKeyPredicate()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("notification-a");
        var tenantB = CreateTenant("notification-b");
        var sharedUser = CreateUser("shared-recipient");
        var otherUser = CreateUser("other-recipient");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        seedContext.Users.AddRange(sharedUser, otherUser);
        await seedContext.SaveChangesAsync();

        const string sharedDeduplicationKey = "event-published:shared";
        var tenantANotification = CreateNotification(tenantA.Id, sharedUser.Id, sharedDeduplicationKey);
        var tenantBNotification = CreateNotification(tenantB.Id, sharedUser.Id, sharedDeduplicationKey);
        var tenantAOtherUserNotification = CreateNotification(tenantA.Id, otherUser.Id, "event-published:other-user");
        seedContext.Notifications.AddRange(tenantANotification, tenantBNotification, tenantAOtherUserNotification);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.DeduplicationKey == sharedDeduplicationKey)
            .Select(notification => notification.Id)
            .ToListAsync();

        var repository = new NotificationRepository(tenantBContext);
        var tenantAExactTupleExists = await repository.ExistsByDeduplicationKeyAsync(
            tenantA.Id,
            sharedUser.Id,
            sharedDeduplicationKey);
        var tenantBExactTupleExists = await repository.ExistsByDeduplicationKeyAsync(
            tenantB.Id,
            sharedUser.Id,
            sharedDeduplicationKey);
        var tenantAOtherUserDoesNotMatch = await repository.ExistsByDeduplicationKeyAsync(
            tenantA.Id,
            otherUser.Id,
            sharedDeduplicationKey);
        var tenantAMissingKeyDoesNotMatch = await repository.ExistsByDeduplicationKeyAsync(
            tenantA.Id,
            sharedUser.Id,
            "event-published:missing");

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBNotification.Id]);
        await Assert.That(tenantAExactTupleExists).IsTrue();
        await Assert.That(tenantBExactTupleExists).IsTrue();
        await Assert.That(tenantAOtherUserDoesNotMatch).IsFalse();
        await Assert.That(tenantAMissingKeyDoesNotMatch).IsFalse();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Notification Repository {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Notification",
                LastName = "Recipient",
            },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private static Notification CreateNotification(Guid tenantId, Guid userId, string deduplicationKey)
    {
        return new Notification
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            NotificationTypeId = (int)NotificationTypeEnum.General,
            NotificationType = null!,
            Title = $"Notification {Guid.NewGuid():N}",
            DeduplicationKey = deduplicationKey,
            NotificationScopeId = (int)ActorTypeEnum.User,
            NotificationScope = null!,
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
