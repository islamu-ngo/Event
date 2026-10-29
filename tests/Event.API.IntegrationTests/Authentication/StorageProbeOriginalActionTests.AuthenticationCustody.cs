using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeStorageConfigurationAcceptsReauthenticationAndRejectsChangedOriginalSources(bool renewPrincipal)
    {
        var clock = new LocalIdentityLifecycleHttpFixture.LifecycleClock();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, timeProvider: clock);
        Guid providerSubject = Guid.CreateVersion7();
        Guid actor;
        Guid binding = Guid.CreateVersion7();
        Guid grant;
        await using (var seed = CreateProbeDatabase(native))
        {
            actor = await seed.Users.Select(row => row.Id).FirstAsync();
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = binding,
                UserId = actor,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    native.ExternalIssuer, providerSubject.ToString("D")).Value,
                CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
            grant = await SeedProbeAdministratorAsync(seed, actor);
        }
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (!renewPrincipal) return;
            services.AddTransient<AdminClaimsTransformation>();
            services.AddTransient<IClaimsTransformation, RenewingPrincipalTransformation>();
        }));
        string bearer = native.CreateExternalProviderToken(
            [
                new Claim("sub", providerSubject.ToString("D")),
                new Claim("typ", "Bearer"),
                new Claim("azp", "islamu-event-blazor"),
                new Claim("auth_provider", "keycloak")
            ], native.ExternalIssuer);
        await using var request = await ProbeRequest.CreateAsync(host.Services, bearer);
        request.Context.Request.Method = HttpMethods.Patch;
        request.Context.Request.Path = "/api/instance/settings/storage";
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        if (renewPrincipal)
        {
            var first = await request.Context.AuthenticateAsync();
            var second = await request.Context.AuthenticateAsync();
            await Assert.That(ReferenceEquals(first.Principal, second.Principal)).IsFalse();
            await Assert.That(first.Principal!.Identities.SequenceEqual(
                second.Principal!.Identities, ReferenceEqualityComparer.Instance)).IsTrue();
        }
        var source = await selected.Services.GetRequiredService<IStorageConfigurationAuthority>()
            .CaptureAsync(new UpdateInstanceStorageSettingsCommand { UserId = actor, Patch = new() }, null, default);
        await source.RequireSourcesAsync(default);
        source.RequireTime();

        var context = request.Context;
        var principal = context.User;
        var identity = (ClaimsIdentity)principal.Identity!;
        var options = request.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        var originalScheme = options.DefaultAuthenticateScheme;
        var properties = (await context.AuthenticateAsync()).Properties!;
        var issuedAt = properties.IssuedUtc;
        var expiresAt = properties.ExpiresUtc;
        var accessor = request.Services.GetRequiredService<IHttpContextAccessor>();
        bool tenantMarkerPresent = context.Items.TryGetValue(TenantContextAccessor.TenantIdItemKey, out var tenantMarker);
        void RestoreTenantMarker()
        {
            if (tenantMarkerPresent) context.Items[TenantContextAccessor.TenantIdItemKey] = tenantMarker;
            else context.Items.Remove(TenantContextAccessor.TenantIdItemKey);
        }
        string authorization = context.Request.Headers.Authorization.ToString();
        var addedClaim = new Claim("role", "substituted-administrator", ClaimValueTypes.String,
            ClaimsIdentity.DefaultIssuer, ClaimsIdentity.DefaultIssuer, identity);
        foreach (var change in new (string Name, Action Tamper, Action Restore)[]
        {
            ("principal", () => context.User = new ClaimsPrincipal(principal), () => context.User = principal),
            ("credential", () => context.Request.Headers.Authorization = "Bearer replaced", () => context.Request.Headers.Authorization = authorization),
            ("method", () => context.Request.Method = HttpMethods.Delete, () => context.Request.Method = HttpMethods.Patch),
            ("path", () => context.Request.Path = "/api/instance/settings/other", () => context.Request.Path = "/api/instance/settings/storage"),
            ("request", () => accessor.HttpContext = new DefaultHttpContext(), () => accessor.HttpContext = context),
            ("tenant-value", () => context.Items[TenantContextAccessor.TenantIdItemKey] = Guid.CreateVersion7(), RestoreTenantMarker),
            ("tenant-type", () => context.Items[TenantContextAccessor.TenantIdItemKey] = "invalid-tenant", RestoreTenantMarker),
            ("tenant-presence", () =>
            {
                if (tenantMarkerPresent) context.Items.Remove(TenantContextAccessor.TenantIdItemKey);
                else context.Items[TenantContextAccessor.TenantIdItemKey] = Guid.CreateVersion7();
            }, RestoreTenantMarker),
            ("claims", () => identity.AddClaim(addedClaim), () => identity.RemoveClaim(addedClaim)),
            ("issued", () => properties.IssuedUtc = clock.GetUtcNow().AddYears(1), () => properties.IssuedUtc = issuedAt),
            ("expiry", () => properties.ExpiresUtc = clock.GetUtcNow().AddYears(1), () => properties.ExpiresUtc = expiresAt),
            ("scheme", () => options.DefaultAuthenticateScheme = ApiAuthenticationSchemeNames.SetupSecret,
                () => options.DefaultAuthenticateScheme = originalScheme)
        })
        {
            change.Tamper();
            await Assert.That(async () => await source.RequireSourcesAsync(default))
                .Throws<UnauthorizedAccessException>().Because(change.Name);
            change.Restore();
            await source.RequireSourcesAsync(default);
        }

        await using (var independent = CreateProbeDatabase(native))
        {
            var original = await independent.PlatformUserRoles.SingleAsync(row => row.Id == grant);
            independent.PlatformUserRoles.Remove(original);
            await independent.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.RequireSourcesAsync(default));
            independent.PlatformUserRoles.Add(original);
            await independent.SaveChangesAsync();
        }
        await source.RequireSourcesAsync(default);
        await using (var independent = CreateProbeDatabase(native))
        {
            var original = await independent.UserExternalLogins.SingleAsync(row => row.Id == binding);
            independent.UserExternalLogins.Remove(original);
            await independent.SaveChangesAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.RequireSourcesAsync(default));
            independent.UserExternalLogins.Add(original);
            await independent.SaveChangesAsync();
        }
        await source.RequireSourcesAsync(default);
        var originalTime = clock.GetUtcNow();
        clock.Advance(source.Lifetime.ExpiresAt.Add(source.Lifetime.ClockSkew).AddTicks(1) - originalTime);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.RequireSourcesAsync(default));
        Assert.Throws<UnauthorizedAccessException>(source.RequireTime);
        clock.Advance(originalTime - clock.GetUtcNow());
        await source.RequireSourcesAsync(default);
        principal.AddIdentity(new ClaimsIdentity(identity));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => source.RequireSourcesAsync(default));

        await using var driftRequest = await ProbeRequest.CreateAsync(host.Services, bearer);
        driftRequest.Context.Request.Method = HttpMethods.Patch;
        driftRequest.Context.Request.Path = "/api/instance/settings/storage";
        await using var driftSelection = await driftRequest.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var driftSource = await driftSelection.Services.GetRequiredService<IStorageConfigurationAuthority>()
            .CaptureAsync(new UpdateInstanceStorageSettingsCommand { UserId = actor, Patch = new() }, null, default);
        await driftSource.RequireSourcesAsync(default);
        driftSource.RequireTime();
        driftRequest.Context.User.AddIdentity(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "request-membership-drift")]));
        await Assert.That(async () => await driftSource.RequireSourcesAsync(default))
            .Throws<UnauthorizedAccessException>().Because("the original request gained an unauthenticated identity after capture");
    }

    private sealed class RenewingPrincipalTransformation(AdminClaimsTransformation original) : IClaimsTransformation
    {
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal) =>
            new((await original.TransformAsync(principal)).Identities);
    }
}
