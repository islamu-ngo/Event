using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Behaviors;

public sealed class CurrentHumanPrincipalFixtureBorrowingTests
{
    [Test]
    public async Task PostgresBorrowingRequiresStructuredPurposesBeforeAuthentication()
    {
        using var context = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql().Options);

        await Assert.That(() => new CurrentHumanPrincipalFixture(Guid.CreateVersion7(), Guid.CreateVersion7(),
            borrowedContext: context)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SignedBorrowerRetainsItsTenantOwnerWhileInstanceChildrenOwnTheirContext()
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7();
        await using var database = new CurrentHumanPrincipalFixture(user, tenant);
        await database.InitializeAsync(authenticate: false);
        var original = database.Context;
        var clock = new BorrowingClock();

        await using (var human = new CurrentHumanPrincipalFixture(user, tenant, borrowedContext: original, timeProvider: clock))
        {
            await human.InitializeAsync();
            await Assert.That(human.Services.GetRequiredService<IUserContext>().UserId).IsEqualTo(user);
            await Assert.That(await human.Admin.ResolveUserIdAsync()).IsEqualTo(user);

            ExploreDbContext instance;
            await using (var selected = await human.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default))
            {
                instance = selected.Services.GetRequiredService<ExploreDbContext>();
                await Assert.That(ReferenceEquals(instance, original)).IsFalse();
                await Assert.That(instance.TenantFilterTenantId).IsNull();
                await Assert.That(selected.Services.GetRequiredService<IHumanCommitScopePreparation>()
                    .Subject?.PrincipalId).IsEqualTo(user);
                await Assert.That(await instance.Users.AnyAsync(row => row.Id == user)).IsTrue();
            }
            await Assert.ThrowsAsync<ObjectDisposedException>(() => instance.Users.AnyAsync());

            await human.PrepareAsync();
            await using (var runtime = await human.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(new PolicyMutationScope(tenant), default))
            {
                await Assert.That(runtime.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(original);
                await using var transaction = await original.Database.BeginTransactionAsync();
                await Assert.That(original.TenantFilterTenantId).IsEqualTo(tenant);
                await transaction.RollbackAsync();
            }

            clock.Current = clock.Current.AddDays(2);
            var request = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            await Assert.That(await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(request.User, default)).IsNull();
            var account = request.User.GetProviderIdentity()!.AccountKey;
            await original.UserExternalLogins.Where(row => row.AuthenticationProviderId == (int)account.ProviderKind
                && row.ProviderKey == account.Value).ExecuteDeleteAsync();
            await Assert.That(await human.Admin.ResolveUserIdAsync()).IsNull();
        }

        await Assert.That(await original.Users.AnyAsync(row => row.Id == user)).IsTrue();
        await Assert.That(original.TenantFilterTenantId).IsEqualTo(tenant);
    }

    private sealed class BorrowingClock : TimeProvider
    {
        internal DateTimeOffset Current { get; set; } = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Current;
    }
}
