using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Api.IntegrationTests.Authentication;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeTenantPreferenceHttpTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    public async Task OriginalSignedHumanSettlesOnlyCurrentSelfMembership(bool revoked, bool foreignTarget, bool suspendedTenant)
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
        Guid otherUser = Guid.CreateVersion7();
        Guid target = Guid.CreateVersion7();
        using (var migrator = baseFactory.CreateMigratorContext())
        {
            var user = User(subject);
            migrator.Users.AddRange(user, User(otherUser));
            migrator.UserExternalLogins.Add(jwt.Binding(user));
            migrator.Tenants.Add(new Tenant
            {
                Id = target, FullName = "Preference target", Slug = $"preference-{target:N}",
                TenantStatusId = (int)(suspendedTenant ? TenantStatusEnum.Suspended : TenantStatusEnum.Active), TenantStatus = null!
            });
            migrator.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = target, Tenant = null!,
                UserId = foreignTarget ? otherUser : subject, User = null!,
                StatusId = (int)(revoked ? TenantUserStatusEnum.Suspended : TenantUserStatusEnum.Active)
            });
            await migrator.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token(subject));

        using var response = await client.PostAsync($"/api/User/active-tenant/{target:D}", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        bool admitted = !revoked && !foreignTarget;
        await Assert.That(await response.Content.ReadFromJsonAsync<bool>()).IsEqualTo(admitted);
        using var verify = baseFactory.CreateMigratorContext();
        var settled = await verify.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == subject);
        await Assert.That(settled.LastActiveTenantId).IsEqualTo(admitted ? target : (Guid?)null);
        await Assert.That(settled.UpdatedBy).IsEqualTo(admitted ? subject : (Guid?)null);
        await Assert.That(settled.Pii.Email).IsEqualTo($"{subject:N}@example.test");
        await Assert.That((await verify.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == otherUser))
            .LastActiveTenantId).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CredentialExpiryOrIdentityAugmentationAfterPrivateWriteRollsBack(bool expire)
    {
        using var jwt = new LocalizationNativeJwtAuthority();
        var clock = new AdmissionClock();
        bool privateWriteCompleted = false;
        await using var baseFactory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider(),
            SeedActiveDefaultTenant = true
        };
        baseFactory.AdditionalConfiguration["Keycloak:Authority"] = jwt.Issuer;
        baseFactory.AdditionalConfiguration["Keycloak:MetadataAddress"] = $"{jwt.Issuer}/.well-known/openid-configuration";
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                jwt.ConfigureServices(services);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<IHumanTenantPreferenceStore>();
                services.AddScoped<HumanTenantPreferenceStore>();
                services.AddScoped<IHumanTenantPreferenceStore>(provider => new AfterWriteStore(
                    provider.GetRequiredService<HumanTenantPreferenceStore>(), () =>
                    {
                        privateWriteCompleted = true;
                        if (expire) clock.Expire();
                        else
                        {
                            var request = provider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                            request.User.AddIdentity(new ClaimsIdentity(request.User.Identities.First()));
                        }
                    }));
            }));
        _ = factory.Services;
        Guid subject = Guid.CreateVersion7();
        Guid tenant = Guid.CreateVersion7();
        Guid? originalUpdatedBy;
        using (var migrator = baseFactory.CreateMigratorContext())
        {
            var user = User(subject);
            migrator.Users.Add(user);
            migrator.UserExternalLogins.Add(jwt.Binding(user));
            migrator.Tenants.Add(new Tenant
            {
                Id = tenant, FullName = "Terminal preference target", Slug = $"terminal-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
            });
            migrator.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenant, Tenant = null!, UserId = subject, User = null!,
                StatusId = (int)TenantUserStatusEnum.Active
            });
            await migrator.SaveChangesAsync();
            originalUpdatedBy = user.UpdatedBy;
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.Token(subject));

        using var response = await client.PostAsync($"/api/User/active-tenant/{tenant:D}", null);

        await Assert.That(privateWriteCompleted).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var verify = baseFactory.CreateMigratorContext();
        var restored = await verify.Users.IgnoreQueryFilters().SingleAsync(user => user.Id == subject);
        await Assert.That(restored.LastActiveTenantId).IsNull();
        await Assert.That(restored.UpdatedBy).IsEqualTo(originalUpdatedBy);
    }

    private sealed class AfterWriteStore(IHumanTenantPreferenceStore inner, Action afterWrite) : IHumanTenantPreferenceStore
    {
        public async Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
        {
            bool result = await inner.WriteAsync(mutation, token);
            if (result) afterWrite();
            return result;
        }
        public Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token) =>
            inner.RequireCompleteAsync(mutation, token);
    }

    private sealed class AdmissionClock : TimeProvider
    {
        private long _ticks;
        public override DateTimeOffset GetUtcNow()
        {
            long forced = Interlocked.Read(ref _ticks);
            return forced == 0 ? DateTimeOffset.UtcNow : new(forced, TimeSpan.Zero);
        }
        internal void Expire() => Interlocked.Exchange(ref _ticks, DateTimeOffset.UtcNow.AddDays(1).UtcTicks);
    }

    private static User User(Guid id) => new()
    {
        Id = id, Pii = new UserPii { Email = $"{id:N}@example.test", FirstName = "Preference", LastName = "Owner" }
    };
}
