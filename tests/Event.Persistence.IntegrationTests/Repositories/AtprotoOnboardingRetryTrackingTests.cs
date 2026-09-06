// ABOUTME: Verifies EF tracking recovery for retryable ATProto tenant onboarding.
// ABOUTME: Ensures reloaded User and Actor owners are not inserted again after tracking is cleared.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

public class AtprotoOnboardingRetryTrackingTests
{
    [Test]
    public async Task RetryReloadsTrackedOwnersBeforeCreatingMissingTenantUser()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"atproto-onboarding-retry-{Guid.NewGuid():N}")
            .Options;
        await using var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("ATProto onboarding retry tracking test.");

        var userId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var user = new User
        {
            Id = userId,
            Pii = new UserPii { Email = "retry@example.test", FirstName = "Retry", LastName = "User" }
        };
        var actorType = new ActorType { Id = (int)ActorTypeEnum.User, MasterCode = "USER", FullName = "User" };
        context.Users.Add(user);
        context.ActorTypes.Add(actorType);
        context.Actors.Add(new Actor
        {
            Id = actorId,
            UserId = userId,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = actorType,
            Pii = new ActorPii { DisplayName = "Retry User" }
        });
        await context.SaveChangesAsync();

        var users = new UserRepository(context);
        var actors = new ActorRepository(context);
        var firstUser = await users.GetById(userId);
        var firstActor = await actors.GetTrackedActorByUserId(userId);
        context.ChangeTracker.Clear();

        var retryUser = await users.GetById(userId);
        var retryActor = await actors.GetTrackedActorByUserId(userId);
        await new TenantUserRepository(context).Create(new TenantUser
        {
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = retryUser!,
            ActorId = actorId,
            Actor = retryActor!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow
        });

        await Assert.That(firstUser).IsNotNull();
        await Assert.That(firstActor).IsNotNull();
        await Assert.That(context.Entry(retryUser!).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(context.Entry(retryActor!).State).IsEqualTo(EntityState.Unchanged);
        await Assert.That(await context.Users.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Actors.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.TenantUsers.CountAsync()).IsEqualTo(1);
    }
}
