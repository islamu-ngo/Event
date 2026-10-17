using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Handlers.Queries;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class ApiKeyManagementCommitAdmissionTests
{
    [Test]
    [Arguments(false, "active")]
    [Arguments(true, "active")]
    [Arguments(false, "revoke")]
    [Arguments(true, "revoke")]
    [Arguments(false, "extend_then_expire")]
    [Arguments(true, "extend_then_expire")]
    public async Task OriginalSupportHorizonCannotBeExtendedByTheManagementOwner(bool revoke, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "API-key administration", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        var services = request.Services;
        await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        bool bodyObserved = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            bodyObserved = true;
            if (change == "revoke")
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            if (change == "extend_then_expire")
            {
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddMinutes(5)), token);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId, finalToken);
                    fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
                }, _ => { });
            }
        });
        async Task InvokeAsync()
        {
            if (revoke)
                await Assert.That(await ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(
                    services, owner, Preparation(services, fixture.Clock)).ExecuteAsync(new(keyId), default)).IsTrue();
            else
                await Assert.That(await ActivatorUtilities.CreateInstance<GetExternalApiKeyDetailsRequestHandler>(
                    services, owner, Preparation(services, fixture.Clock)).QueryAsync(new(keyId), default)).IsNotNull();
        }
        if (change == "active") await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(bodyObserved).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var sessionAfter = await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
        await Assert.That(sessionAfter.ExpiresAtUtc).IsEqualTo(expiry);
        await Assert.That(sessionAfter.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == keyId)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo(
                revoke && change == "active" ? (int)ExternalApiKeyStatusEnum.Revoked : (int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("stamp")]
    [Arguments("expiry")]
    [Arguments("grant")]
    [Arguments("route")]
    public async Task SelectedTenantQuotaOwnerAdmitsOriginalSourcesBeforeReturningData(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        var query = new GetExternalApiKeyUsageReportRequest
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            From = new(2026, 1, 1),
            To = new(2026, 1, 31)
        };
        Guid grantId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = await seed.TenantUsers.SingleAsync(row => row.UserId == fixture.Binding.LocalSubjectId);
            var grant = new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUserRoleGrants.Add(grant);
            grantId = grant.Id;
            seed.ExternalApiKeyQuotas.Add(new ExternalApiKeyQuota
            {
                Id = Guid.CreateVersion7(),
                ExternalApiKeyId = keyId,
                ExternalApiKey = null!,
                PeriodStart = query.From,
                PeriodEnd = query.To,
                RequestCount = 7,
                CreditsUsed = 2
            });
            var setting = await seed.SystemSettings.SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
            setting.Value = "\"local\"";
            await seed.SaveChangesAsync();
        }
        bool bodyObserved = false;
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHumanApiKeyManagementPreparation();
            services.AddScoped<ISerializableAttemptOwner>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                return new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
                {
                    bodyObserved = true;
                    if (change == "stamp")
                        await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                                Guid.CreateVersion7().ToString("N")), token);
                    if (change == "grant")
                        await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                                fixture.Clock.GetUtcNow().UtcDateTime), token);
                    if (change == "route")
                        await context.SystemSettings.Where(row =>
                                row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    if (change == "expiry")
                        provider.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            await context.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId, finalToken);
                            var principal = provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()
                                .HttpContext!.User;
                            var lifetime = (await provider.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                                .ObserveAsync(principal, finalToken))!;
                            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                                - fixture.Clock.GetUtcNow());
                        }, _ => { });
                });
            });
        }));
        await using var request = new Request(host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var execution = request.Services.GetRequiredService<IExternalApiKeyTenantUsageReader>().ReadAsync(query, default);
        if (change == "unchanged")
        {
            var rows = await execution;
            await Assert.That(rows.Single().ApiKeyId).IsEqualTo(keyId);
            await Assert.That(rows.Single().TotalRequestCount).IsEqualTo(7L);
        }
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(bodyObserved).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.Where(row => row.Id == grantId)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }

    [Test]
    public async Task PublicTenantReaderCannotReleaseDataWithoutAnAuthenticatedOriginalRequest()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            request.Services.GetRequiredService<IExternalApiKeyTenantUsageReader>().ReadAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                From = new(2026, 1, 1),
                To = new(2026, 1, 31)
            }, default));
    }
}
