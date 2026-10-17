using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.API.Extensions;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Federation;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Real registered authentication handlers validate every credential. Only the observation clock
/// moves across the commit boundary; no validation delegate, signature or lifetime check is bypassed.
/// </summary>
[NotInParallel]
public sealed class HumanAuthenticationLifetimeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ValidatedBearerLifetimeSurvivesNeitherExpiryNorPrincipalSubstitution(bool legacy)
    {
        await using var authority = new BearerAuthority(legacy);
        await using var request = new AuthenticatedRequest(authority.Services);
        await request.AuthenticateAsync(authority.Sign(authority.Payload()));
        await Assert.That(request.Result.Succeeded).IsTrue();
        HumanAuthenticationLifetime? lifetime = await request.ObserveAsync();
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(lifetime!.AuthenticationScheme).IsEqualTo(JwtBearerDefaults.AuthenticationScheme);
        await Assert.That(lifetime.AccountKey).IsEqualTo(request.Context.User.GetProviderIdentity()!.AccountKey);
        await Assert.That(lifetime.ExpiresAt).IsEqualTo(authority.ExpiresAt);
        await Assert.That(lifetime.ClockSkew).IsEqualTo(TimeSpan.FromMinutes(5));

        // Neither mutable ticket properties nor claims can extend the signed credential lifetime.
        request.Result.Properties!.ExpiresUtc = authority.ExpiresAt.AddYears(1);
        var identity = (ClaimsIdentity)request.Context.User.Identity!;
        identity.RemoveClaim(identity.FindFirst("exp")!);
        identity.AddClaim(new Claim("exp", authority.ExpiresAt.AddYears(1).ToUnixTimeSeconds().ToString()));
        await Assert.That(await request.ObserveAsync()).IsSameReferenceAs(lifetime);
        authority.Clock.Now = authority.ExpiresAt.AddMinutes(5);
        await Assert.That(lifetime.IsValidAt(authority.Clock.Now)).IsTrue();
        authority.Clock.Now = authority.Clock.Now.AddTicks(1);
        await Assert.That(lifetime.IsValidAt(authority.Clock.Now)).IsFalse();
        await Assert.That(await request.ObserveAsync()).IsNull();

        authority.Clock.Now = authority.InitialTime;
        var original = request.Context.User;
        request.Context.User = new ClaimsPrincipal(original.Identities.Select(value => new ClaimsIdentity(value)));
        await Assert.That(await request.ObserveAsync()).IsNull();
        request.Context.User = original;
        await Assert.That(await request.ObserveAsync()).IsSameReferenceAs(lifetime);
        identity.RemoveClaim(identity.FindFirst("sub")!);
        identity.AddClaim(new Claim("sub", Guid.NewGuid().ToString("D")));
        await Assert.That(await request.ObserveAsync()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SignedInvalidOrAmbiguousLifetimeCannotProduceObservation(bool legacy)
    {
        await using var authority = new BearerAuthority(legacy);
        foreach (string condition in new[]
        {
            "expired", "missing", "string", "duplicate", "overflow", "wrong-key", "wrong-purpose",
            "not-before", "invalid-start", "duplicate-start"
        })
        {
            Dictionary<string, object?> payload = authority.Payload();
            switch (condition)
            {
                case "expired":
                    payload["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds();
                    payload["exp"] = DateTimeOffset.UnixEpoch.AddDays(1).ToUnixTimeSeconds();
                    break;
                case "missing": payload.Remove("exp"); break;
                case "string": payload["exp"] = authority.ExpiresAt.ToUnixTimeSeconds().ToString(); break;
                case "overflow": payload["exp"] = long.MaxValue; break;
                case "wrong-purpose": payload["typ"] = "ID"; break;
                case "not-before": payload["nbf"] = authority.InitialTime.AddHours(1).ToUnixTimeSeconds(); break;
                case "invalid-start": payload["nbf"] = "invalid"; break;
            }
            string json = JsonSerializer.Serialize(payload);
            if (condition == "duplicate")
                json = json[..^1] + ",\"exp\":" + authority.ExpiresAt.AddDays(1).ToUnixTimeSeconds() + "}";
            if (condition == "duplicate-start")
                json = json[..^1] + ",\"nbf\":1}";
            await using (var rejected = new AuthenticatedRequest(authority.Services))
            {
                await rejected.AuthenticateAsync(authority.SignJson(json, condition == "wrong-key"));
                await Assert.That(await rejected.ObserveAsync()).IsNull();
                if (condition is "expired" or "missing" or "overflow" or "wrong-key" or "wrong-purpose" or "not-before")
                    await Assert.That(rejected.Result.Succeeded).IsFalse();
            }
            await using var control = new AuthenticatedRequest(authority.Services);
            await control.AuthenticateAsync(authority.Sign(authority.Payload()));
            await Assert.That(control.Result.Succeeded).IsTrue();
            await Assert.That(await control.ObserveAsync()).IsNotNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinalAuthenticationFailureOrReplacementCannotReuseValidationEvent(bool replacePrincipal)
    {
        await using var authority = new BearerAuthority(legacy: false, context =>
        {
            if (!context.Request.Headers.ContainsKey("X-Test-Reject-Custody"))
                return;
            if (replacePrincipal)
                context.Principal = new ClaimsPrincipal(context.Principal!.Identities.Select(identity => new ClaimsIdentity(identity)));
            else
                context.Fail("Test rejection after actual token validation.");
        });
        await using (var rejected = new AuthenticatedRequest(authority.Services))
        {
            rejected.Context.Request.Headers["X-Test-Reject-Custody"] = "true";
            await rejected.AuthenticateAsync(authority.Sign(authority.Payload()));
            await Assert.That(rejected.Result.Succeeded).IsEqualTo(replacePrincipal);
            await Assert.That(await rejected.ObserveAsync()).IsNull();
        }
        await using var control = new AuthenticatedRequest(authority.Services);
        await control.AuthenticateAsync(authority.Sign(authority.Payload()));
        await Assert.That(control.Result.Succeeded).IsTrue();
        await Assert.That(await control.ObserveAsync()).IsNotNull();
    }

    [Test]
    public async Task LifetimeCustodyDoesNotCrossRequestsOrAdditionalAuthenticatedIdentities()
    {
        await using var authority = new BearerAuthority(legacy: false);
        ClaimsPrincipal authenticated;
        await using (var request = new AuthenticatedRequest(authority.Services))
        {
            await request.AuthenticateAsync(authority.Sign(authority.Payload()));
            await Assert.That(await request.ObserveAsync()).IsNotNull();
            authenticated = request.Context.User;
            // The real admin transformation appends unauthenticated identities. They are not credentials.
            authenticated.AddIdentity(new ClaimsIdentity([new Claim("is_instance_admin", "true")]));
            await Assert.That(await request.ObserveAsync()).IsNotNull();
            authenticated.AddIdentity(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "other"));
            await Assert.That(await request.ObserveAsync()).IsNull();
        }
        await using var unrelated = new AuthenticatedRequest(authority.Services);
        unrelated.Context.User = authenticated;
        unrelated.Context.Items["expires_at"] = authority.ExpiresAt.AddYears(1);
        await Assert.That(await unrelated.ObserveAsync()).IsNull();
    }

    [Test]
    public async Task MissingNotBeforeDoesNotInventExpiryOrRejectSupportedBearer()
    {
        await using var authority = new BearerAuthority(legacy: false);
        var payload = authority.Payload();
        payload.Remove("nbf");
        await using var request = new AuthenticatedRequest(authority.Services);
        await request.AuthenticateAsync(authority.Sign(payload));
        await Assert.That(request.Result.Succeeded).IsTrue();
        var lifetime = await request.ObserveAsync();
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(lifetime!.NotBefore).IsNull();
        await Assert.That(lifetime.ExpiresAt).IsEqualTo(authority.ExpiresAt);
    }

    [Test]
    public async Task LocalReceiptRequiresRealSessionValidationAndKeepsLocalSkew()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(token);
        await Assert.That(request.Result.Succeeded).IsTrue();
        var lifetime = await request.ObserveAsync();
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(lifetime!.AuthenticationScheme).IsEqualTo(ApiAuthenticationSchemeNames.LocalIdentity);
        await Assert.That(lifetime.AccountKey.ProviderKind).IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(lifetime.ClockSkew).IsEqualTo(TimeSpan.FromMinutes(1));
        var originalSession = request.Context.User.TryGetLocalSessionAuthority()!;
        await Assert.That(lifetime.LocalSession!.SecurityStamp).IsEqualTo(originalSession.SecurityStamp);
        var identity = (ClaimsIdentity)request.Context.User.Identity!;
        identity.RemoveClaim(identity.FindFirst(LocalSessionToken.SecurityStampClaim)!);
        identity.AddClaim(new Claim(LocalSessionToken.SecurityStampClaim, Guid.NewGuid().ToString("N")));
        await Assert.That(await request.ObserveAsync()).IsSameReferenceAs(lifetime);
        await Assert.That(lifetime.LocalSession.SecurityStamp).IsEqualTo(originalSession.SecurityStamp);
        var services = request.Context.RequestServices;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var facts = await principalSource.ObserveAsync();
        await Assert.That(facts.PrincipalId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(facts.LocalSession!.Accounts.Single().StampMatches).IsTrue();
        await Assert.That(facts.SelectedTenantId)
            .IsEqualTo(services.GetRequiredService<ExploreDbContext>().TenantFilterTenantId);
        var routeSource = services.GetRequiredService<IHumanAuthorizationRouteSource>();
        var route = await routeSource.ObserveAsync();
        await Assert.That(route.PrincipalId).IsEqualTo(facts.PrincipalId);
        await Assert.That(route.SelectedTenantId).IsEqualTo(facts.SelectedTenantId);
        await Assert.That(route.Settings.Select(setting => setting.Key)
            .SequenceEqual(HumanAuthorizationRouteFacts.Keys)).IsTrue();
        IHumanAuthorizationRouteLease? heldRoute = null;
        await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            var exclusion = await services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
            heldRoute = await routeSource.AcquireAsync(exclusion, token);
            var heldPrincipal = await principalSource.AcquireAsync(exclusion, token);
            await heldPrincipal.ValidateAsync(token);
            var currentRoute = await heldRoute.ValidateAsync(token);
            await Assert.That(currentRoute.PrincipalId).IsEqualTo(facts.PrincipalId);
            await Assert.That(currentRoute.SelectedTenantId).IsEqualTo(facts.SelectedTenantId);
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => heldRoute!.ValidateAsync());
        fixture.Clock.Advance(lifetime.ExpiresAt.AddMinutes(1).AddTicks(1) - fixture.Clock.GetUtcNow());
        await Assert.That(await request.ObserveAsync()).IsNull();
        await Assert.That(lifetime.IsValidAt(fixture.Clock.GetUtcNow())).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedHumanFinalizerUsesOriginalSourcesAndCredentialAtActualCommit(bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Context.RequestServices;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var lifetime = (await request.ObserveAsync())!;
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var routeSource = services.GetRequiredService<IHumanAuthorizationRouteSource>();
        var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
        var context = services.GetRequiredService<ExploreDbContext>();
        Guid targetTenant = context.TenantFilterTenantId ?? Guid.CreateVersion7();
        var assignments = services.GetRequiredService<IHumanEventAssignmentSourceFactory>()
            .Create(targetTenant, []);
        await Assert.That((await assignments.ObserveAsync()).EventIds.Length).IsEqualTo(0);
        var support = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync();
        var supportFacts = await support.ObserveAsync();
        await Assert.That(supportFacts.Forwarding.WasForwarded).IsFalse();
        var webhook = services.GetRequiredService<IHumanWebhookResourceSourceFactory>().Create(
            HumanWebhookResourceTarget.From(new Explore.Application.Features.Webhooks.Requests.Commands.ArchiveWebhookEndpointCommand
            {
                EndpointId = Guid.CreateVersion7()
            }));
        await Assert.That((await webhook.ObserveAsync()).Endpoints.Length).IsEqualTo(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            finalizer.Register(_ => Task.CompletedTask, _ => { });
            return Task.CompletedTask;
        });
        string key = $"selected-human-finalizer-{Guid.NewGuid():N}";
        var execution = services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            var exclusion = await services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(token);
            var principal = await principalSource.AcquireAsync(exclusion, token);
            var route = await routeSource.AcquireAsync(exclusion, token);
            var assignment = await assignments.AcquireAsync(exclusion, token);
            var supportLease = await support.AcquireAsync(exclusion, principal, assignment, token);
            var webhookLease = await webhook.AcquireAsync(exclusion, principal, supportLease,
                context.TenantFilterTenantId.HasValue ? assignment : null, token);
            HumanSupportAccessSourceFacts? finalSupport = null;
            finalizer.Register(async finalToken =>
            {
                var current = await principal.ValidateAsync(finalToken);
                Explore.Infrastructure.Services.HumanPrincipalSourceValidation.RequireCurrent(
                    services.GetRequiredService<IHumanCommitScopePreparation>().Subject!,
                    lifetime.LocalSession, current);
                await route.ValidateAsync(finalToken);
                await Assert.That((await assignment.ValidateAsync(finalToken)).EventIds.Length).IsEqualTo(0);
                finalSupport = await supportLease.ValidateAsync(finalToken);
                await Assert.That((await webhookLease.ValidateAsync(finalToken)).Endpoints.Length).IsEqualTo(0);
                if (expire)
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }, now =>
            {
                if (Explore.Infrastructure.SupportAccess.HumanSupportAccessProjection.BoundaryDenialReason(
                    finalSupport!, now, ResourceKinds.Tenant, AuthorizationActions.Update,
                    new Dictionary<string, object> { ["tenantId"] = targetTenant }) is not null)
                    throw new UnauthorizedAccessException("The original support boundary no longer permits the attempt.");
            });
            context.SystemSettings.Add(new Explore.Domain.SystemSetting { SettingKey = key, Value = "staged" });
            await context.SaveChangesAsync(token);
            return true;
        });
        if (expire)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else
            await execution;
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(setting => setting.SettingKey == key)).IsEqualTo(!expire);
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("before_acquisition")]
    [Arguments("own_final_write")]
    [Arguments("callback-skips-acquisition")]
    [Arguments("forged-rollback")]
    [Arguments("genuine-conflict")]
    [Arguments("expired_before_acquisition")]
    public async Task PreparedTenantActionCannotCarryRevokedPrincipalInputsIntoCommit(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Context.RequestServices;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var subject = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        Guid grantId = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            int roleId = await seed.Roles.Where(role => role.MasterCode == "platform.admin"
                && role.RoleScopeId == (int)RoleScopeEnum.Platform).Select(role => role.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new Explore.Domain.PlatformUserRole
            {
                Id = grantId,
                UserId = subject.PrincipalId,
                User = null!,
                RoleId = roleId,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        var context = services.GetRequiredService<ExploreDbContext>();
        Guid target = context.TenantFilterTenantId ?? Guid.CreateVersion7();
        var preparation = new Explore.Infrastructure.Services.HumanTenantMutationPreparation(
            services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<Explore.Application.Contracts.Infrastructure.ITenantContext>(),
            fixture.Clock, services.GetRequiredService<Explore.Infrastructure.Services.CerbosAuthorizationService>(),
            Options.Create(new Explore.Infrastructure.Services.AuthorizationProviderDeploymentOptions { Provider = "local" }),
            services.GetRequiredService<IOptions<Explore.Infrastructure.Services.CerbosSettings>>(),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
        await Assert.ThrowsAsync<Explore.Application.Exceptions.AuthorizationException>(() =>
            preparation.PrepareAsync(HumanTenantMutationKind.Delete, target));
        var prepared = await preparation.PrepareAsync(HumanTenantMutationKind.RegistrationProviderConnections, target);
        if (change == "before_acquisition")
        {
            await using var revoke = fixture.Native.CreateDatabase();
            await revoke.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();
        }
        if (change == "expired_before_acquisition")
        {
            var lifetime = (await request.ObserveAsync())!;
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
        }
        string key = $"prepared-tenant-source-{Guid.NewGuid():N}";
        int bodyCalls = 0;
        var execution = preparation.ExecutePreparedAsync(HumanTenantMutationKind.RegistrationProviderConnections,
            target, prepared, async (admitted, token) =>
        {
            bodyCalls++;
            if (change != "callback-skips-acquisition")
                await preparation.RequireOwnedAsync(admitted, target, HumanTenantMutationKind.RegistrationProviderConnections, token);
            if (change == "own_final_write")
                await context.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync(token);
            context.SystemSettings.Add(new Explore.Domain.SystemSetting { SettingKey = key, Value = "staged" });
            await context.SaveChangesAsync(token);
            if (change == "forged-rollback")
                throw new Explore.Application.Exceptions.HumanMutationAttemptRolledBackException(
                    new InvalidOperationException("Callback did not observe physical rollback."));
            if (change == "genuine-conflict")
                throw new Explore.Application.Exceptions.ConcurrencyConflictException(
                    Explore.Application.Exceptions.ConcurrencyConflictException.ConcurrentUpdate,
                    "A real single-attempt owner must settle this conflict.");
            return true;
        });
        bool committed = change is "unchanged" or "callback-skips-acquisition";
        if (committed)
            await execution;
        else if (change == "forged-rollback")
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else if (change == "genuine-conflict")
            await Assert.ThrowsAsync<Explore.Application.Exceptions.HumanMutationAttemptRolledBackException>(() => execution);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(setting => setting.SettingKey == key))
            .IsEqualTo(committed);
        await Assert.That(bodyCalls).IsEqualTo(change is "unchanged" or "callback-skips-acquisition"
            or "own_final_write" or "forged-rollback" or "genuine-conflict" ? 1 : 0);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.RequireOwnedAsync(
            prepared, target, HumanTenantMutationKind.RegistrationProviderConnections));
        await Assert.ThrowsAsync<InvalidOperationException>(() => preparation.ExecutePreparedAsync(
            HumanTenantMutationKind.RegistrationProviderConnections, target, prepared,
            (_, _) => Task.FromResult(true)));
    }

    [Test]
    [Arguments("delete", "allowed")]
    [Arguments("delete", "revoked")]
    [Arguments("delete", "expired")]
    [Arguments("delete", "revoked-after-body")]
    [Arguments("create", "allowed")]
    [Arguments("create", "revoked")]
    [Arguments("create", "expired")]
    [Arguments("create", "revoked-after-body")]
    [Arguments("update", "allowed")]
    [Arguments("update", "revoked")]
    [Arguments("update", "expired")]
    [Arguments("update", "revoked-after-body")]
    [Arguments("origins", "allowed")]
    [Arguments("origins", "revoked")]
    [Arguments("origins", "expired")]
    [Arguments("origins", "revoked-after-body")]
    public async Task ProviderConnectionMutationsRequireOneOriginalHumanOwner(string operation, string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Context.RequestServices;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var subject = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        var context = services.GetRequiredService<ExploreDbContext>();
        Guid tenantId = context.TenantFilterTenantId ?? Guid.CreateVersion7();
        Guid grantId = Guid.CreateVersion7();
        Guid connectionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (!await seed.Tenants.AnyAsync(tenant => tenant.Id == tenantId))
                seed.Tenants.Add(new Explore.Domain.Tenant
                {
                    Id = tenantId,
                    FullName = "Prepared provider tenant",
                    Slug = $"prepared-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            int roleId = await seed.Roles.Where(role => role.MasterCode == "platform.admin"
                && role.RoleScopeId == (int)RoleScopeEnum.Platform).Select(role => role.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new Explore.Domain.PlatformUserRole
            {
                Id = grantId,
                UserId = subject.PrincipalId,
                User = null!,
                RoleId = roleId,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            var connection = Explore.Domain.RegistrationProviderConnection.Create(tenantId, "Prepared provider",
                RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
                "prepared", "hosted", "v1", "policy", "evidence",
                "https://forms.example.org/api", "https://forms.example.org", "workspace", null, null,
                fixture.Clock.GetUtcNow().UtcDateTime);
            seed.RegistrationProviderConnections.Add(connection);
            connectionId = connection.Id;
            await seed.SaveChangesAsync();
            if (outcome == "revoked")
                await seed.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync();
        }
        Func<CancellationToken, Task>? afterBody = null;
        var preparation = new Explore.Infrastructure.Services.HumanTenantMutationPreparation(
            services.GetRequiredService<IHumanCommitScopePreparation>(), services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<Explore.Application.Contracts.Infrastructure.ITenantContext>(),
            fixture.Clock, services.GetRequiredService<Explore.Infrastructure.Services.CerbosAuthorizationService>(),
            Options.Create(new Explore.Infrastructure.Services.AuthorizationProviderDeploymentOptions { Provider = "local" }),
            services.GetRequiredService<IOptions<Explore.Infrastructure.Services.CerbosSettings>>(),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => new ProviderConnectionAfterBodyOwner(
                services.GetRequiredService<ISerializableAttemptOwner>(), token =>
                    afterBody is null ? Task.CompletedTask : afterBody(token)));
        var repository = services.GetRequiredService<IRegistrationProviderRepository>();
        var tuple = Explore.Infrastructure.Registration.NativeRegistrationProviderDescriptor.NativeTuple;
        var registry = new Explore.Infrastructure.Registration.RegistrationProviderRegistry(
            [new Explore.Infrastructure.Registration.NativeRegistrationProviderDescriptor()]);
        var input = new Explore.Application.DTOs.RegistrationProviders.RegistrationProviderConnectionRequestDto
        {
            Name = "Updated provider",
            ProviderKindId = (int)RegistrationProviderKindEnum.Native,
            DeploymentKindId = (int)RegistrationProviderDeploymentKindEnum.Native,
            ProviderCode = tuple.ProviderCode,
            ProviderDeploymentCode = tuple.ProviderDeploymentCode,
            ApiVersion = tuple.ApiVersion,
            AdapterPolicyVersion = tuple.AdapterPolicyVersion,
            ConformanceEvidenceRevision = tuple.ConformanceEvidenceRevision,
            ManagementApiBaseUrl = "https://forms.example.org/api",
            PublicBaseUrl = "https://forms.example.org",
            ProviderWorkspaceId = "workspace",
            ApiTokenSecretBindingId = Guid.CreateVersion7()
        };
        Guid eventId = Guid.CreateVersion7();
        Task<Explore.Application.Responses.BaseCommandResponse<Guid>> Invoke() => operation switch
        {
            "create" or "update" => new Explore.Application.Features.RegistrationProviders.Commands.UpsertRegistrationProviderConnectionCommandHandler(
                repository, registry, fixture.Clock, preparation).ExecuteAsync(new(
                    tenantId, eventId, operation == "create" ? null : connectionId, input)),
            "origins" => new Explore.Application.Features.RegistrationProviders.Commands.ReplaceRegistrationProviderApprovedOriginsCommandHandler(
                repository, fixture.Clock, preparation).ExecuteAsync(new(
                    tenantId, eventId, connectionId, ["https://approved.example.org"])),
            "delete" => new Explore.Application.Features.RegistrationProviders.Commands.DeleteRegistrationProviderConnectionCommandHandler(
                repository, fixture.Clock, preparation).ExecuteAsync(new(tenantId, eventId, connectionId)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        bool bodyObserved = false;
        if (outcome is "expired" or "revoked-after-body")
            afterBody = async token =>
            {
                var rows = await context.RegistrationProviderConnections.IgnoreQueryFilters()
                    .AsNoTracking().Where(row => row.TenantId == tenantId).ToListAsync(token);
                var changed = rows.Single(row => row.Id == connectionId);
                await Assert.That(operation switch
                {
                    "delete" => changed.IsDeleted,
                    "create" => rows.Any(row => row.Name == "Updated provider"),
                    "update" => changed.Name == "Updated provider",
                    "origins" => await context.Set<Explore.Domain.RegistrationProviderApprovedOrigin>()
                        .AnyAsync(origin => origin.RegistrationProviderConnectionId == connectionId, token),
                    _ => false
                }).IsTrue();
                bodyObserved = true;
                if (outcome == "revoked-after-body")
                    await context.PlatformUserRoles.Where(grant => grant.Id == grantId).ExecuteDeleteAsync(token);
                else
                {
                    var lifetime = (await request.ObserveAsync())!;
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                }
            };
        if (outcome == "revoked")
            await Assert.ThrowsAsync<Explore.Application.Exceptions.AuthorizationException>(async () => await Invoke());
        else if (outcome is "expired" or "revoked-after-body")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await Invoke());
        else
            await Assert.That((await Invoke()).IsSuccess).IsTrue();
        await Assert.That(bodyObserved).IsEqualTo(outcome is "expired" or "revoked-after-body");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.RegistrationProviderConnections.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(connection => connection.Id == connectionId);
        bool accepted = outcome == "allowed";
        await Assert.That(persisted.IsDeleted).IsEqualTo(accepted && operation == "delete");
        await Assert.That(persisted.Name).IsEqualTo(accepted && operation == "update" ? "Updated provider" : "Prepared provider");
        await Assert.That(await observer.RegistrationProviderConnections.IgnoreQueryFilters()
            .CountAsync(row => row.TenantId == tenantId)).IsEqualTo(accepted && operation == "create" ? 2 : 1);
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(grant => grant.Id == grantId))
            .IsEqualTo(outcome != "revoked");
        if (operation == "origins" && accepted)
            await Assert.That(await observer.Set<Explore.Domain.RegistrationProviderApprovedOrigin>()
                .Where(origin => origin.RegistrationProviderConnectionId == connectionId)
                .Select(origin => origin.Origin).ToListAsync()).IsEquivalentTo(["https://approved.example.org"]);
        else
            await Assert.That(await observer.Set<Explore.Domain.RegistrationProviderApprovedOrigin>()
                .AnyAsync(origin => origin.RegistrationProviderConnectionId == connectionId)).IsFalse();
    }

    [Test]
    public async Task AuthenticatedRequestCannotSelectNonhumanEmailEntry()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        string value = $"\"internal-denial-{Guid.NewGuid():N}\"";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            request.Context.RequestServices.GetRequiredService<IInternalEmailDeliverySettingsWriter>()
                .ApplyInternalAsync([new(null, Explore.Domain.Constants.GovernanceSettingKeys.Email.FromName,
                    EmailDeliverySettingMutationKind.SetValue, value)], null));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(
            setting => setting.SettingKey == Explore.Domain.Constants.GovernanceSettingKeys.Email.FromName
                && setting.Value == value)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalValidatedLocalLifetimeControlsFinalPersistedCommit(bool expireDuringAdmission)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var lifetime = (await request.ObserveAsync())!;
        await Assert.That(lifetime).IsNotNull();
        var subject = (await HumanCommitSubject.ResolveAsync(request.Context.User,
            request.Context.RequestServices.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None))!;
        await Assert.That(subject.AccountKey).IsEqualTo(lifetime.AccountKey);
        await using var owner = fixture.Native.CreateDatabase();
        ISerializableAttemptOwner attempt = new EfCoreUnitOfWork(owner);
        string key = $"human-lifetime-final-{Guid.NewGuid():N}";
        var action = attempt.ExecuteAsync(async token =>
        {
            owner.RegisterHumanCommitAdmission(subject, lifetime, fixture.Clock, _ =>
            {
                if (expireDuringAdmission)
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
            owner.SystemSettings.Add(new Explore.Domain.SystemSetting { SettingKey = key, Value = "original attempt" });
            await owner.SaveChangesAsync(token);
            return true;
        });
        if (expireDuringAdmission)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => action);
        else
            await Assert.That(await action).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.SettingKey == key))
            .IsEqualTo(!expireDuringAdmission);
    }

    [Test]
    public async Task AuthenticatedLifetimeCannotBeSubstitutedForAnotherPersistedAccount()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var original = new AuthenticatedRequest(fixture.Host.Services);
        await original.AuthenticateAsync(await fixture.SignInAsync());
        var lifetime = (await original.ObserveAsync())!;
        var otherLogin = await fixture.Native.SeedLocalUserAsync(true);
        await using var other = new AuthenticatedRequest(fixture.Host.Services);
        await other.AuthenticateAsync(await fixture.SignInAsync(otherLogin));
        var foreignSubject = (await HumanCommitSubject.ResolveAsync(other.Context.User,
            other.Context.RequestServices.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            CancellationToken.None))!;
        await Assert.That(foreignSubject.AccountKey).IsNotEqualTo(lifetime.AccountKey);
        await using var owner = fixture.Native.CreateDatabase();
        ISerializableAttemptOwner attempt = new EfCoreUnitOfWork(owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt.ExecuteAsync(token =>
        {
            owner.RegisterHumanCommitAdmission(foreignSubject, lifetime, fixture.Clock,
                _ => Task.CompletedTask, _ => { });
            return Task.FromResult(true);
        }));
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task LocalExpiredOrInvalidStampCredentialCannotMintLifetimeReceipt()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        SecurityKey key = fixture.Host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(ApiAuthenticationSchemeNames.LocalIdentity).TokenValidationParameters.IssuerSigningKey;
        foreach (string condition in new[] { "expired", "stamp" })
        {
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
            if (condition == "expired")
            {
                parsed.Payload["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds();
                parsed.Payload["exp"] = DateTimeOffset.UnixEpoch.AddDays(1).ToUnixTimeSeconds();
            }
            else
                parsed.Payload[LocalSessionToken.SecurityStampClaim] = Guid.NewGuid().ToString("N");
            string candidate = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.HmacSha256)), parsed.Payload));
            await using (var rejected = new AuthenticatedRequest(fixture.Host.Services))
            {
                await rejected.AuthenticateAsync(candidate);
                await Assert.That(rejected.Result.Succeeded).IsFalse();
                await Assert.That(await rejected.ObserveAsync()).IsNull();
            }
            await using var control = new AuthenticatedRequest(fixture.Host.Services);
            await control.AuthenticateAsync(token);
            await Assert.That(control.Result.Succeeded).IsTrue();
            await Assert.That(await control.ObserveAsync()).IsNotNull();
        }
    }

    [Test]
    public async Task AtprotoReceiptUsesSessionKeyAndRetainsCanonicalIdentitySemantics()
    {
        await using var fixture = await LocalAdmissionWebApplicationFactory.CreateAsync(enableAtproto: true);
        await using var issuance = fixture.Services.CreateAsyncScope();
        var issuer = issuance.ServiceProvider.GetRequiredService<AtprotoJwtService>();
        var tenant = issuance.ServiceProvider.GetRequiredService<ITenantContext>();
        AtprotoDid did = AtprotoDid.Parse("did:plc:lifetime-observation");
        var issued = await issuer.IssueAsync(Guid.NewGuid(), tenant.TenantId, did, CancellationToken.None);
        await using var request = new AuthenticatedRequest(fixture.Services);
        await request.AuthenticateAsync(issued.Token);
        await Assert.That(request.Result.Succeeded).IsTrue();
        var lifetime = await request.ObserveAsync();
        await Assert.That(lifetime).IsNotNull();
        await Assert.That(lifetime!.AuthenticationScheme).IsEqualTo(ApiAuthenticationSchemeNames.AtprotoSession);
        await Assert.That(lifetime.AccountKey).IsEqualTo(PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(did));
        await Assert.That(lifetime.ClockSkew).IsEqualTo(TimeSpan.FromSeconds(5));
        await Assert.That(lifetime.IsValidAt(lifetime.ExpiresAt.AddSeconds(5).AddTicks(1))).IsFalse();

        // A valid session alone does not satisfy the separate current-session bridge purpose.
        await using var missingBridge = new AuthenticatedRequest(fixture.Services);
        missingBridge.Context.Request.Path = AtprotoJwtOptions.CurrentSessionPath;
        await missingBridge.AuthenticateAsync(issued.Token);
        await Assert.That(missingBridge.Result.Succeeded).IsFalse();
        await Assert.That(await missingBridge.ObserveAsync()).IsNull();

        var secrets = issuance.ServiceProvider.GetRequiredService<ISecretResolver>();
        foreach (string condition in new[] { "oauth-key", "expired", "foreign-tenant", "missing-expiry" })
        {
            string keyName = condition == "oauth-key"
                ? SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks
                : SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks;
            var resolved = await secrets.ResolveAsync(keyName, null, CancellationToken.None);
            var ring = InfrastructureAtprotoKeyRing.Parse(resolved.Value);
            using var signingKey = ring.CreateEcdsaKey(ring.ActiveKeyId!, includePrivateKey: true);
            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(issued.Token);
            switch (condition)
            {
                case "expired":
                    parsed.Payload["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds();
                    parsed.Payload["exp"] = DateTimeOffset.UnixEpoch.AddDays(1).ToUnixTimeSeconds();
                    break;
                case "foreign-tenant":
                    parsed.Payload[AtprotoJwtOptions.TenantClaim] = Guid.NewGuid().ToString("D");
                    break;
                case "missing-expiry": parsed.Payload.Remove("exp"); break;
            }
            var key = new ECDsaSecurityKey(signingKey)
            {
                KeyId = ring.ActiveKeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            string candidate = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                new JwtHeader(new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)), parsed.Payload));
            await using (var rejected = new AuthenticatedRequest(fixture.Services))
            {
                await rejected.AuthenticateAsync(candidate);
                await Assert.That(rejected.Result.Succeeded).IsFalse();
                await Assert.That(await rejected.ObserveAsync()).IsNull();
            }
            await using var control = new AuthenticatedRequest(fixture.Services);
            await control.AuthenticateAsync(issued.Token);
            await Assert.That(control.Result.Succeeded).IsTrue();
            await Assert.That(await control.ObserveAsync()).IsNotNull();
        }

        // Even genuinely accepted, finite-lived bootstrap authority is not a human session.
        await using var bootstrap = new AuthenticatedRequest(fixture.Services);
        bootstrap.Context.Request.Method = HttpMethods.Post;
        bootstrap.Context.Request.Path = AtprotoJwtOptions.BridgePath;
        bootstrap.Context.Request.Headers[AtprotoJwtOptions.BootstrapHeaderName] =
            fixture.CreateAtprotoBootstrapAssertion(did, AtprotoSubjectClassification.Person, tenant.TenantId);
        await bootstrap.AuthenticateAsync();
        await Assert.That(bootstrap.Result.Succeeded).IsTrue();
        await Assert.That(await bootstrap.ObserveAsync()).IsNull();
    }

    private sealed class ProviderConnectionAfterBodyOwner(
        ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody) : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                T result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }

    private sealed class AuthenticatedRequest : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal DefaultHttpContext Context { get; }
        internal AuthenticateResult Result { get; private set; } = AuthenticateResult.NoResult();

        internal AuthenticatedRequest(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
            Context.Request.Path = "/lifetime-observation";
            _accessor = _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        }

        internal async Task AuthenticateAsync(string? token = null)
        {
            _accessor.HttpContext = Context;
            if (token is not null)
                Context.Request.Headers.Authorization = "Bearer " + token;
            Result = await Context.AuthenticateAsync();
            if (Result.Succeeded) Context.User = Result.Principal!;
        }

        internal ValueTask<HumanAuthenticationLifetime?> ObserveAsync()
        {
            _accessor.HttpContext = Context;
            return _scope.ServiceProvider.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(Context.User, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class ObservationClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BearerAuthority : IAsyncDisposable
    {
        private readonly RSA _key = RSA.Create(2048);
        private readonly RSA _wrongKey = RSA.Create(2048);
        private readonly string _issuer = $"https://lifetime-{Guid.NewGuid():N}.invalid/realms/event";
        internal DateTimeOffset InitialTime { get; } = DateTimeOffset.UtcNow;
        internal DateTimeOffset ExpiresAt { get; }
        internal ObservationClock Clock { get; }
        internal ServiceProvider Services { get; }

        internal BearerAuthority(bool legacy, Action<TokenValidatedContext>? afterValidation = null)
        {
            // Authentic validation uses its unchanged system clock with a broad live control.
            // Expiry races use the injected observation clock, never a sleep or a short-lived token.
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(InitialTime.AddDays(1).ToUnixTimeSeconds());
            Clock = new(InitialTime);
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = _issuer,
                ["Keycloak:RequireHttpsMetadata"] = "false",
                ["Authentication:Local:JwtKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            }).Build();
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.EnvironmentName.Returns(Environments.Development);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configuration);
            services.AddSingleton(environment);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddApiAuthentication(configuration, environment, skipAuthorityWarmup: true);
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.UseSecurityTokenValidators = legacy;
                var metadata = new OpenIdConnectConfiguration { Issuer = _issuer };
                metadata.SigningKeys.Add(new RsaSecurityKey(_key.ExportParameters(false)) { KeyId = "lifetime-key" });
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                var validate = options.Events.OnTokenValidated;
                options.Events.OnTokenValidated = async context =>
                {
                    await validate(context);
                    if (context.Result?.Failure is null)
                        afterValidation?.Invoke(context);
                };
            });
            Services = services.BuildServiceProvider();
        }

        internal Dictionary<string, object?> Payload() => new()
        {
            ["iss"] = _issuer,
            ["sub"] = Guid.NewGuid().ToString("D"),
            ["aud"] = "islamu-event-api",
            ["typ"] = "Bearer",
            ["nbf"] = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds(),
            ["exp"] = ExpiresAt.ToUnixTimeSeconds()
        };

        internal string Sign(Dictionary<string, object?> payload) => SignJson(JsonSerializer.Serialize(payload));

        internal string SignJson(string payload, bool wrongKey = false)
        {
            string header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(
                new { alg = "RS256", kid = "lifetime-key", typ = "JWT" }));
            string input = header + "." + Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
            byte[] signature = (wrongKey ? _wrongKey : _key).SignData(
                Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return input + "." + Base64UrlEncoder.Encode(signature);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            _key.Dispose();
            _wrongKey.Dispose();
        }
    }
}
