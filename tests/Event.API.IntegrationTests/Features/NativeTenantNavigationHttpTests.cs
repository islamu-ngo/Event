using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Authentication;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.User;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeTenantNavigationHttpTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task SignedSelfNavigationSelectsOnlyOwnCurrentMemberships(int count, bool rememberMember)
    {
        using var jwt = new LocalizationNativeJwtAuthority();
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider(),
            SeedActiveDefaultTenant = true
        };
        baseFactory.AdditionalConfiguration["Keycloak:Authority"] = jwt.Issuer;
        baseFactory.AdditionalConfiguration["Keycloak:MetadataAddress"] = $"{jwt.Issuer}/.well-known/openid-configuration";
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(jwt.ConfigureServices));
        _ = factory.Services;
        Guid subject = Guid.CreateVersion7();
        Guid other = Guid.CreateVersion7();
        Guid[] tenants = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
        DateTime joined = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var migrator = baseFactory.CreateMigratorContext())
        {
            var user = User(subject);
            user.LastActiveTenantId = rememberMember ? tenants[0] : tenants[2];
            migrator.Users.AddRange(user, User(other));
            migrator.UserExternalLogins.Add(jwt.Binding(user));
            for (int index = 0; index < tenants.Length; index++)
            {
                migrator.Tenants.Add(new Tenant
                {
                    Id = tenants[index],
                    FullName = "Navigation tenant",
                    Slug = $"navigation-{tenants[index]:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
            }
            await migrator.SaveChangesAsync();
            for (int index = 0; index < count; index++)
                migrator.TenantUsers.Add(Membership(subject, tenants[index], joined.AddDays(index)));
            migrator.TenantUsers.Add(Membership(other, tenants[2], joined.AddDays(3)));
            await migrator.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token(subject));

        using var response = await client.GetAsync("/api/User/tenants/redirection");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UserTenantRedirectionDto>();
        await Assert.That(result).IsNotNull();
        Guid? expected = count == 0 ? null : rememberMember ? tenants[0] : tenants[count - 1];
        await Assert.That(result!.TenantId).IsEqualTo(expected);
        await Assert.That(result.TenantSlug).IsEqualTo(expected is { } id ? $"navigation-{id:N}" : null);
        await Assert.That(result.HasMultipleTenants).IsEqualTo(count > 1);
        using var verify = baseFactory.CreateMigratorContext();
        await Assert.That((await verify.Users.AsNoTracking().SingleAsync(row => row.Id == subject)).LastActiveTenantId)
            .IsEqualTo(rememberMember ? tenants[0] : tenants[2]);
    }

    private static User User(Guid id) => new()
    {
        Id = id,
        Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "Navigation", LastName = "Owner" }
    };

    private static TenantUser Membership(Guid user, Guid tenant, DateTime joined) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = user,
        User = null!,
        TenantId = tenant,
        Tenant = null!,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = joined
    };
}
