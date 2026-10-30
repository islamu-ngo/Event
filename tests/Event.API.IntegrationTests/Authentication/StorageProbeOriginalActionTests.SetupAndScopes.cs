using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("pending-replaced")]
    [Arguments("completed")]
    [Arguments("header-replaced")]
    [Arguments("principal-replaced")]
    [Arguments("secret-locked")]
    [Arguments("expired")]
    [Arguments("wrong-route")]
    [Arguments("invented-setup-principal")]
    [Arguments("setup-as-administrator")]
    public async Task AcceptedSetupProbeRetainsRealTicketSecretAndPendingGeneration(string change)
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            AuthenticationProviderKind.Keycloak, incompleteSetup: true);
        Guid providerSubject = Guid.CreateVersion7();
        Guid actor;
        await using (var seed = CreateProbeDatabase(native))
        {
            actor = await seed.Users.Select(row => row.Id).FirstAsync();
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = actor,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    native.ExternalIssuer, providerSubject.ToString("D")).Value,
                CreatedAt = DateTime.UtcNow
            });
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        var clock = new LocalIdentityLifecycleHttpFixture.LifecycleClock();
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            remote.Register(services);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        await using var request = await ProbeRequest.CreateAsync(host.Services,
            native.CreateExternalProviderToken(
            [
                new Claim("sub", providerSubject.ToString("D")),
                new Claim("typ", "Bearer"),
                new Claim("azp", "islamu-event-blazor"),
                new Claim("email", "probe-setup@example.test"),
                new Claim("email_verified", "true", ClaimValueTypes.Boolean),
                new Claim("auth_provider", "keycloak")
            ], native.ExternalIssuer), native.SetupSecret);
        if (change == "wrong-route") request.Context.Request.Path = "/api/instance/settings/storage";
        if (change == "invented-setup-principal")
            request.Context.User = new(new System.Security.Claims.ClaimsIdentity(
                Array.Empty<System.Security.Claims.Claim>(), ApiAuthenticationSchemeNames.SetupSecret));
        if (change == "invented-setup-principal")
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services
                .GetRequiredService<IInstanceStorageSettingService>()
                .TestProviderAsync(new TestInstanceStorageProviderDuringSetupQuery()));
            return;
        }
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterVersioning = async () =>
        {
            if (change == "header-replaced")
                request.Context.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change == "secret-locked")
                selected.Services.GetRequiredService<ISetupSecretProvider>().Lock();
            if (change == "expired")
            {
                var lifetime = selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
                clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - clock.GetUtcNow());
            }
            if (change is "pending-replaced" or "completed")
            {
                await using var independent = CreateProbeDatabase(native);
                var pending = await independent.InstanceBootstrapStates.SingleAsync();
                if (change == "pending-replaced")
                {
                    independent.Remove(pending);
                    await independent.SaveChangesAsync();
                    independent.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                        Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
                }
                else pending.CompleteInteractive(actor, DateTime.UtcNow);
                await independent.SaveChangesAsync();
            }
        };
        async Task<InstanceStorageProviderStatusDto> ThroughActualController()
        {
            var controller = ActivatorUtilities.CreateInstance<InstanceStorageSettingsController>(selected.Services);
            controller.ControllerContext = new ControllerContext { HttpContext = request.Context };
            var response = await controller.TestStorageConnection();
            await Assert.That(response.Result).IsTypeOf<OkObjectResult>();
            return (InstanceStorageProviderStatusDto)((OkObjectResult)response.Result!).Value!;
        }
        Task<InstanceStorageProviderStatusDto> execution = change == "unchanged" ? ThroughActualController()
            : change == "setup-as-administrator"
            ? ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
                .QueryAsync(new TestInstanceStorageProviderQuery { UserId = actor }, CancellationToken.None)
            : ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
                .QueryAsync(new TestInstanceStorageProviderDuringSetupQuery(), CancellationToken.None);
        if (change == "unchanged") await Assert.That((await execution).IsAvailable).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(remote.Puts).IsEqualTo(change == "unchanged" ? 1 : 0);
        await Assert.That(remote.Deletes).IsEqualTo(change == "unchanged" ? 1 : 0);
    }

    [Test]
    [Arguments("tenant-administrator")]
    [Arguments("instance-administrator")]
    [Arguments("tenant-grant-revoked")]
    public async Task TenantProbePreservesBothRealAdministratorAlternatives(string authority)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grantId = Guid.Empty;
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            if (authority == "instance-administrator")
                await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            else
            {
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                seed.TenantUsers.Add(member);
                var grant = new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = member.TenantId,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = null!,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow
                };
                grantId = grant.Id;
                seed.TenantUserRoleGrants.Add(grant);
            }
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        request.Context.Request.Path = "/api/tenant/settings/storage/test";
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterVersioning = async () =>
        {
            if (authority != "tenant-grant-revoked") return;
            await using var independent = CreateProbeDatabase(fixture.Native, PlatformDefaults.DefaultTenantId);
            await Assert.That(await independent.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteDeleteAsync())
                .IsEqualTo(1);
        };
        var execution = ActivatorUtilities.CreateInstance<TestTenantStorageProviderCommandHandler>(selected.Services)
            .ExecuteAsync(new TestTenantStorageProviderCommand { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None);
        if (authority == "tenant-grant-revoked")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That((await execution).IsAvailable).IsTrue();
        await Assert.That(remote.Puts).IsEqualTo(authority == "tenant-grant-revoked" ? 0 : 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSupportHorizonCannotBeExtendedForProbeCleanup(bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(3);
        Guid sessionId;
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            foreach (var key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else row.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Storage probe", "probe-fixture",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            sessionId = session.Id;
            seed.SupportAccessSessions.Add(session);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        remote.AfterPut = () =>
        {
            if (expire) fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
            return Task.CompletedTask;
        };
        var execution = ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
            .QueryAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None);
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await Assert.That((await execution).IsAvailable).IsTrue();
        await Assert.That(remote.Puts).IsEqualTo(1);
        await Assert.That(remote.Deletes).IsEqualTo(expire ? 0 : 1);
    }

    [Test]
    public async Task ActualControllerCarriesOriginalHumanIntoTheClosedProbeHandler()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = CreateProbeDatabase(fixture.Native))
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        var controller = ActivatorUtilities.CreateInstance<InstanceStorageSettingsController>(request.Services);
        controller.ControllerContext = new ControllerContext { HttpContext = request.Context };
        var response = await controller.TestStorageConnection();
        await Assert.That(response.Result).IsTypeOf<OkObjectResult>();
        var result = (InstanceStorageProviderStatusDto)((OkObjectResult)response.Result!).Value!;
        await Assert.That(result.IsAvailable).IsTrue();
        await Assert.That(remote.Objects.Single().Version).IsEqualTo("foreign-version");
    }
}
