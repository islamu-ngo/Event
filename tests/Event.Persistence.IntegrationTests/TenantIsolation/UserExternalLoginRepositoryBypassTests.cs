using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class UserExternalLoginRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task GetByProviderAndKey_WithAmbientTenant_ReturnsOnlyExactGlobalBinding()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var userA = CreateUser("external-login-a");
        var userB = CreateUser("external-login-b");
        seedContext.Users.AddRange(userA, userB);
        await seedContext.SaveChangesAsync();

        ProviderAccountKey tenantAKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://auth.example.test/realms/ISLAMU",
            "subject-a");
        ProviderAccountKey tenantBKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://auth.example.test/realms/ISLAMU",
            "subject-b");
        ProviderAccountKey missingKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            "https://auth.example.test/realms/ISLAMU",
            "subject-missing");
        var tenantALogin = CreateExternalLogin(userA.Id, "keycloak", tenantAKey.Value);
        var tenantBLogin = CreateExternalLogin(userB.Id, "keycloak", tenantBKey.Value);
        seedContext.UserExternalLogins.AddRange(tenantALogin, tenantBLogin);
        await seedContext.SaveChangesAsync();

        await using var tenantContext = fixture.CreateTenantFilteredDbContext(
            new TestTenantContext(Guid.CreateVersion7()));
        var visibleBindings = await tenantContext.UserExternalLogins
            .AsNoTracking()
            .Select(login => login.Id)
            .ToListAsync();

        var repository = new UserExternalLoginRepository(tenantContext);
        var tenantAByProviderKey = await repository.GetByProviderAndKey(tenantAKey);
        var missing = await repository.GetByProviderAndKey(missingKey);
        var tenantAByUser = await repository.GetByUser(userA.Id);

        await Assert.That(visibleBindings)
            .IsEquivalentTo([tenantALogin.Id, tenantBLogin.Id]);
        await Assert.That(tenantAByProviderKey).IsNotNull();
        await Assert.That(tenantAByProviderKey!.Id).IsEqualTo(tenantALogin.Id);
        await Assert.That(missing).IsNull();
        await Assert.That(tenantAByUser.Select(login => login.Id))
            .IsEquivalentTo([tenantALogin.Id]);
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "External",
                LastName = "Login",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static UserExternalLogin CreateExternalLogin(
        Guid userId,
        string provider,
        string providerKey)
    {
        return new UserExternalLogin { Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!, AuthenticationProviderId = (int)provider.ParseAuthenticationProviderKind(), AuthenticationProvider = null!, ProviderKey = providerKey,
        ProviderDisplayName = provider,
        CreatedAt = DateTime.UtcNow, };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
