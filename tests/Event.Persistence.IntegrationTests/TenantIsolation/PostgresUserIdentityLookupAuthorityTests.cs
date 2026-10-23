using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresUserIdentityLookupAuthorityTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task StandaloneBindingLookupUsesTheExactProviderPairWithoutJoiningUserPii()
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var global = await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new ExternalLoginAuthenticationRepository(global.Factory, fixture.NativeQueueEnrollment);

        UserExternalLogin? binding = await repository.GetByProviderAndKey(rows.Account);

        await Assert.That(binding?.UserId).IsEqualTo(rows.UserId);
        await Assert.That(binding!.User).IsNull();
        await Assert.That(binding.ProviderKey).IsEqualTo(rows.Account.Value);
        await Assert.That(await repository.GetByProviderAndKey(new(
            AuthenticationProviderKind.Local, rows.Account.Value))).IsNull();
        await using var owner = fixture.CreateDbContext();
        await owner.Users.Where(user => user.Id == rows.UserId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsDeleted, true));
        await Assert.That(await new UserRepository(owner).GetById(rows.UserId)).IsNull();
        await Assert.That((await repository.GetByProviderAndKey(rows.Account))?.UserId).IsEqualTo(rows.UserId)
            .Because("the binding lookup cannot replace the native user-state and privacy checks");
    }

    [Test]
    public async Task ExactUserMembershipEnumerationIsCompleteWithAnAlreadyBoundRequestTenant()
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        await Assert.That(await request.TenantUsers.IgnoreQueryFilters()
            .Where(row => row.UserId == rows.UserId).Select(row => row.TenantId).ToArrayAsync())
            .IsEquivalentTo([rows.A]);
        await using var global = await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        var repository = new UserTenantMembershipRepository(global.Factory, new TenantExploreDbContextFactory(rows.RuntimeOptions), fixture.NativeQueueEnrollment);

        List<TenantUser> memberships = await repository.GetActiveTenantsForUserAsync(rows.UserId);

        await Assert.That(memberships.Select(row => row.TenantId)).IsEquivalentTo([rows.A, rows.B]);
        await Assert.That(memberships.All(row => row.UserId == rows.UserId)).IsTrue();
        await Assert.That(memberships.All(row => row.Tenant.Id == row.TenantId &&
            !string.IsNullOrEmpty(row.Tenant.Slug) &&
            row.Tenant.TenantStatus.Id == (int)TenantStatusEnum.Active)).IsTrue();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.A);
    }

    [Test]
    public async Task ExactTargetMembershipReadDoesNotSubstituteTheCurrentRequestTenant()
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var target = rows.Context(rows.B);
        await rows.AssertOrdinaryRoleAsync(target);
        await Assert.That(await new TenantUserRepository(target)
            .IsActiveTenantUserAsync(rows.B, rows.UserId)).IsTrue();
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);

        await using var global = await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        bool active = await new UserTenantMembershipRepository(global.Factory, new TenantExploreDbContextFactory(rows.RuntimeOptions), fixture.NativeQueueEnrollment)
            .IsActiveTenantUserAsync(rows.B, rows.UserId);

        await Assert.That(active).IsTrue();
        await Assert.That(request.TenantFilterTenantId).IsEqualTo(rows.A);
    }

    [Test]
    public async Task GlobalIdentityBindingRemainsVisibleInsideItsOwningTransaction()
    {
        await using var rows = await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await using var context = fixture.CreateDbContext();
        var account = new Explore.Application.Authentication.ProviderAccountKey(
            AuthenticationProviderKind.Google, $"transaction-{Guid.CreateVersion7():N}");
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var binding = new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                User = null!,
                AuthenticationProviderId = (int)account.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = account.Value
            };
            context.UserExternalLogins.Add(binding);
            await context.SaveChangesAsync();

            UserExternalLogin? visible = await new UserExternalLoginRepository(context).GetByProviderAndKey(account);

            await Assert.That(visible?.Id).IsEqualTo(binding.Id);
            await transaction.RollbackAsync();
        });
        await using var verify = fixture.CreateDbContext();
        await Assert.That(await new UserExternalLoginRepository(verify).GetByProviderAndKey(account)).IsNull();
    }
}
