using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments("resolver-human")]
    [Arguments("operator-human")]
    [Arguments("operator-setup")]
    [Arguments("auth-human")]
    [Arguments("auth-setup")]
    [Arguments("authz-human")]
    [Arguments("authz-setup")]
    public async Task NativeSiblingConfigurationRetainsTheSelectedCredential(string branch)
    {
        bool setup = branch.EndsWith("-setup", StringComparison.Ordinal);
        var clock = new LocalIdentityLifecycleHttpFixture.LifecycleClock();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: setup, timeProvider: clock);
        Guid providerSubject = Guid.CreateVersion7();
        Guid actor;
        Guid binding = Guid.CreateVersion7();
        Guid grant = Guid.Empty;
        await using (var seed = CreateProbeDatabase(native))
        {
            actor = await seed.Users.Select(row => row.Id).FirstAsync();
            if (setup)
                seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                    Guid.CreateVersion7(), DeploymentMode.SingleTenant, clock.GetUtcNow().UtcDateTime));
            else
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
            if (!setup) grant = await SeedProbeAdministratorAsync(seed, actor);
        }
        string? bearer = setup ? null : native.CreateExternalProviderToken(
        [
            new Claim("sub", providerSubject.ToString("D")),
            new Claim("typ", "Bearer"),
            new Claim("azp", "islamu-event-blazor"),
            new Claim("auth_provider", "keycloak")
        ], native.ExternalIssuer);
        await using var request = await SiblingConfigurationRequestAsync(native.Services, branch, bearer,
            setup ? native.SetupSecret : null);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        Func<CancellationToken, Task> requireSources;
        Action requireTime;
        if (branch == "resolver-human")
        {
            var source = await services.GetRequiredService<IResolverConfigurationMutationAuthority>()
                .CaptureAsync(new() { UserId = actor, Patch = new() }, default);
            requireSources = source.RequireSourcesAsync;
            requireTime = source.RequireTime;
        }
        else if (branch.StartsWith("operator-", StringComparison.Ordinal))
        {
            var source = await services.GetRequiredService<IOperatorIdentityMutationAuthority>()
                .CaptureAsync(new SaveInstanceOperatorIdentityCommand(), default);
            await Assert.That(source.IsHuman).IsEqualTo(!setup);
            await Assert.That(source.IsSetup).IsEqualTo(setup);
            requireSources = source.RequireSourcesAsync;
            requireTime = source.RequireTime;
        }
        else
        {
            object command = branch switch
            {
                "auth-human" => new UpdateAuthProviderConfigurationCommand { UserId = actor, Patch = new() },
                "auth-setup" => new UpdateAuthProviderConfigurationDuringSetupCommand { Patch = new() },
                "authz-human" => new UpdateAuthorizationProviderConfigurationCommand { UserId = actor, Patch = new() },
                "authz-setup" => new UpdateAuthorizationProviderConfigurationDuringSetupCommand { Patch = new() },
                _ => throw new ArgumentOutOfRangeException(nameof(branch))
            };
            var source = await services.GetRequiredService<IProviderConfigurationMutationAuthority>().CaptureAsync(command, default);
            await Assert.That(source is IProviderConfigurationSetupSource).IsEqualTo(setup);
            requireSources = source.RequireSourcesAsync;
            requireTime = source.RequireTime;
        }
        await requireSources(default);
        requireTime();
        var context = request.Context;
        var principal = context.User;
        var identity = (ClaimsIdentity)principal.Identity!;
        var lifetime = await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>().ObserveAsync(principal, default);
        await Assert.That(lifetime is null).IsEqualTo(setup);
        await Assert.That(services.GetRequiredService<IHumanCommitScopePreparation>().Subject is null).IsEqualTo(setup);
        var properties = (await context.AuthenticateAsync()).Properties!;
        var issuedAt = properties.IssuedUtc;
        var expiresAt = properties.ExpiresUtc;
        var options = request.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        var originalScheme = options.DefaultAuthenticateScheme;
        var addedClaim = new Claim("role", "substituted-administrator", ClaimValueTypes.String,
            ClaimsIdentity.DefaultIssuer, ClaimsIdentity.DefaultIssuer, identity);
        foreach (var change in new (string Name, Action Tamper, Action Restore)[]
        {
            ("principal", () => context.User = new ClaimsPrincipal(principal.Identities.Select(value => new ClaimsIdentity(value))),
                () => context.User = principal),
            ("claims", () => identity.AddClaim(addedClaim), () => identity.RemoveClaim(addedClaim)),
            ("issued", () => properties.IssuedUtc = clock.GetUtcNow().AddYears(1), () => properties.IssuedUtc = issuedAt),
            ("expiry", () => properties.ExpiresUtc = clock.GetUtcNow().AddYears(1), () => properties.ExpiresUtc = expiresAt),
            ("scheme", () => options.DefaultAuthenticateScheme = setup
                ? JwtBearerDefaults.AuthenticationScheme : ApiAuthenticationSchemeNames.SetupSecret,
                () => options.DefaultAuthenticateScheme = originalScheme)
        })
        {
            change.Tamper();
            await Assert.That(async () => await requireSources(default))
                .Throws<UnauthorizedAccessException>().Because(branch + ": " + change.Name);
            change.Restore();
            await requireSources(default);
        }
        if (setup)
        {
            string original = context.Request.Headers["X-Setup-Secret"].ToString();
            context.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requireSources(default));
            context.Request.Headers["X-Setup-Secret"] = original;
            await requireSources(default);
            services.GetRequiredService<ISetupSecretProvider>().Lock();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requireSources(default));
            Assert.Throws<UnauthorizedAccessException>(requireTime);
        }
        else
        {
            if (branch is "auth-human" or "authz-human")
            {
                await using var independent = CreateProbeDatabase(native);
                var role = await independent.PlatformUserRoles.SingleAsync(row => row.Id == grant);
                independent.PlatformUserRoles.Remove(role);
                await independent.SaveChangesAsync();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requireSources(default));
                independent.PlatformUserRoles.Add(role);
                await independent.SaveChangesAsync();
                await requireSources(default);
                var login = await independent.UserExternalLogins.SingleAsync(row => row.Id == binding);
                independent.UserExternalLogins.Remove(login);
                await independent.SaveChangesAsync();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requireSources(default));
                independent.UserExternalLogins.Add(login);
                await independent.SaveChangesAsync();
                await requireSources(default);
            }
            var originalTime = clock.GetUtcNow();
            clock.Advance(lifetime!.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - originalTime);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => requireSources(default));
            Assert.Throws<UnauthorizedAccessException>(requireTime);
            clock.Advance(originalTime - clock.GetUtcNow());
            await requireSources(default);
        }
    }

    private static async Task<ProbeRequest> SiblingConfigurationRequestAsync(
        IServiceProvider services, string branch, string? bearer, string? secret)
    {
        var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        bool identity = branch.StartsWith("operator-", StringComparison.Ordinal);
        context.Request.Method = identity ? HttpMethods.Put : HttpMethods.Patch;
        context.Request.Path = identity ? "/api/instance-operator-identity"
            : branch == "resolver-human" ? "/api/instance/settings/resolver-config"
            : branch.StartsWith("authz-", StringComparison.Ordinal)
                ? "/api/instance/settings/authz-provider" : "/api/instance/settings/auth-provider";
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (secret is not null) context.Request.Headers["X-Setup-Secret"] = secret;
        accessor.HttpContext = context;
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        try
        {
            var authentication = await context.AuthenticateAsync();
            if (!authentication.Succeeded)
                throw new InvalidOperationException("The actual sibling configuration request did not authenticate.");
            context.User = authentication.Principal!;
            return new(scope, accessor, context);
        }
        catch
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw;
        }
    }
}
