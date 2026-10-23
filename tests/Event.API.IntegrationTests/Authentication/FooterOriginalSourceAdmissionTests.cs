using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Footer.Handlers.Commands;
using Explore.Application.Features.Footer.Handlers.Queries;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Features.Footer.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Explore.API.Authorization;
using Explore.Application.DTOs.Footer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Grpc.Core;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class FooterOriginalSourceAdmissionTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("grant")]
    [Arguments("session")]
    [Arguments("route")]
    [Arguments("credential")]
    [Arguments("principal")]
    [Arguments("support_active")]
    [Arguments("support_revoke")]
    [Arguments("support_extend_after_reads")]
    [Arguments("pdp_allow")]
    [Arguments("pdp_deny")]
    public async Task OriginalAdmissionSurvivesTheActualFooterBodyOrRollsItBack(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grantId = Guid.CreateVersion7();
        Guid? supportId = null;
        var supportExpiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId);
            if (member is null)
            {
                member = new TenantUser
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
            }
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = grantId,
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            if (change.StartsWith("support_", StringComparison.Ordinal))
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
                var support = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                    SupportAccessModeEnum.Write, "maintenance", "Footer administration", "fixture-ticket",
                    fixture.Clock.GetUtcNow().AddMinutes(-1), supportExpiry);
                seed.SupportAccessSessions.Add(support);
                supportId = support.Id;
            }
            foreach (var (key, value) in new[]
            {
                (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
                (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""),
                (GovernanceSettingKeys.Footer.LockTenantLinkGroups, "false")
            })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
                else row.Value = value;
            }
            if (change.StartsWith("pdp_", StringComparison.Ordinal))
            {
                foreach (var (key, value) in new[]
                {
                    (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
                    (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
                    (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://footer-tenant-pdp.invalid:3593\"")
                })
                {
                    var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
                    if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
                    else row.Value = value;
                }
            }
            await seed.SaveChangesAsync();
        }
        await using var request = fixture.Host.Services.CreateAsyncScope();
        var services = request.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = services };
        accessor.HttpContext = http;
        try
        {
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            string credential = await fixture.SignInAsync();
            accessor.HttpContext = http;
            http.Request.Headers.Authorization = "Bearer " + credential;
            var authenticated = await http.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            http.User = authenticated.Principal!;
            if (supportId is { } forwarded)
            {
                http.Request.Headers[SupportAccessHeaderNames.SessionId] = forwarded.ToString("D");
                await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
            }
            await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
            var context = services.GetRequiredService<ExploreDbContext>();
            string title = "Original Footer " + Guid.CreateVersion7().ToString("N");
            var command = new CreateFooterLinkGroupCommand
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                UserId = fixture.Binding.LocalSubjectId,
                Title = title
            };
            bool bodyObserved = false;
            var owner = new AfterBody(new EfCoreUnitOfWork(context), async token =>
            {
                bodyObserved = await context.TenantFooterLinkGroups.AnyAsync(row => row.Title == title, token);
                switch (change)
                {
                    case "principal":
                        await context.Users.Where(row => row.Id == fixture.Binding.LocalSubjectId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
                        break;
                    case "support_revoke":
                        await context.SupportAccessSessions.Where(row => row.Id == supportId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                                (int)SupportAccessSessionStatusEnum.Revoked), token);
                        break;
                    case "support_extend_after_reads":
                        await context.SupportAccessSessions.Where(row => row.Id == supportId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                                supportExpiry.AddMinutes(5)), token);
                        services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            await context.TenantFooterLinkGroups.AsNoTracking().SingleAsync(row => row.Title == title, finalToken);
                            fixture.Clock.Advance(supportExpiry - fixture.Clock.GetUtcNow());
                        }, _ => { });
                        break;
                    case "grant":
                        await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                                fixture.Clock.GetUtcNow().UtcDateTime), token);
                        break;
                    case "session":
                        await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                                Guid.CreateVersion7().ToString("N")), token);
                        break;
                    case "route":
                        await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                        break;
                    case "credential":
                        services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            await context.TenantFooterLinkGroups.AsNoTracking().SingleAsync(row => row.Title == title, finalToken);
                            var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                                .ObserveAsync(http.User, finalToken))!;
                            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                        }, _ => { });
                        break;
                }
            });
            var transport = change.StartsWith("pdp_", StringComparison.Ordinal)
                ? new Transport(context, change == "pdp_deny" ? Effect.Deny : Effect.Allow) : null;
            var execution = Execution(services, fixture.Clock, owner, cerbos: transport is null ? null
                : ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(services, transport.Client, transport.Factory));
            var repository = new FooterLinkGroupRepository(context, execution);
            var handler = new CreateFooterLinkGroupCommandHandler(repository,
                new FooterLinkMutationGuard(services.GetRequiredService<IHierarchicalSettingsResolver>(),
                    services.GetRequiredService<IDeploymentModeProvider>()), execution);
            var result = execution.ExecuteAsync(command, async token =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution.RequireOwnedAsync(command with { }, token));
                return await handler.ExecuteAsync(command, token);
            });
            bool accepted = change is "unchanged" or "support_active" or "pdp_allow";
            if (accepted)
                await Assert.That((await result).IsSuccess).IsTrue();
            else if (change == "pdp_deny")
                await Assert.ThrowsAsync<AuthorizationException>(() => result);
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => result);
            await Assert.That(bodyObserved).IsEqualTo(change != "pdp_deny");
            if (transport is not null)
            {
                var sent = transport.Requests.Single().Resources.Single();
                await Assert.That(sent.Resource.Kind).IsEqualTo(ResourceKinds.Tenant);
                await Assert.That(sent.Resource.Id).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
                await Assert.That(sent.Actions.Single()).IsEqualTo(AuthorizationActions.Update);
                await Assert.That(sent.Resource.Attr["tenantId"].StringValue).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
                await Assert.That(transport.Endpoint).IsEqualTo("http://footer-tenant-pdp.invalid:3593");
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.Create(new TenantFooterLinkGroup
            {
                TenantId = command.TenantId,
                Title = "Cannot borrow a completed producer"
            }));
            await using var reader = fixture.Native.CreateDatabase();
            await Assert.That(await reader.TenantFooterLinkGroups.AnyAsync(row => row.Title == title))
                .IsEqualTo(accepted);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InstanceGovernanceSelectsOrdinaryGlobalAndAdmitsOriginalPlatformGrant(bool revoke)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grant = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = grant,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        await using var request = fixture.Host.Services.CreateAsyncScope();
        var services = request.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = services };
        accessor.HttpContext = http;
        try
        {
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            string credential = await fixture.SignInAsync();
            accessor.HttpContext = http;
            http.Request.Headers.Authorization = "Bearer " + credential;
            var authenticated = await http.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            http.User = authenticated.Principal!;
            var factory = new FooterExecutionScopeFactory(services, services.GetRequiredService<IPolicyMutationExecutionScopeFactory>());
            await using var global = await factory.CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
            var context = global.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(context.TenantFilterTenantId).IsNull();
            var selected = new FooterExecutionScopeFactory(global.Services,
                global.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            {
                await using var denied = await selected.CreateAsync(PolicyMutationScope.EmailInstance, CancellationToken.None);
            });
            bool bodyObserved = false;
            var owner = new AfterBody(new EfCoreUnitOfWork(context), async token =>
            {
                bodyObserved = true;
                if (revoke)
                    await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            });
            var execution = Execution(global.Services, fixture.Clock, owner);
            var command = new UpdateFooterGovernanceSettingsCommand
            {
                Patch = new PatchFooterGovernanceSettingsDto
                {
                    LockTenantTemplate = Explore.Application.Models.Common.OptionalUpdate<bool>.Set(true)
                }
            };
            var writer = new FooterSettingsWriter(context,
                global.Services.GetRequiredService<ExploreDbContextExecutionLease>(), execution, fixture.Clock);
            var handler = new UpdateFooterGovernanceSettingsCommandHandler(writer, execution);
            var result = execution.ExecuteAsync(command, token => handler.ExecuteAsync(command, token));
            if (revoke)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => result);
            else
                await Assert.That((await result).IsSuccess).IsTrue();
            await Assert.That(bodyObserved).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.WriteGovernanceAsync(command, CancellationToken.None));
            await using var reader = fixture.Native.CreateDatabase();
            await Assert.That(await reader.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Footer.LockTenantTemplate)
                .AnyAsync(row => row.Value == "true")).IsEqualTo(!revoke);
        }
        finally { accessor.HttpContext = null; }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AuthenticatedSetupReadRevalidatesBeforeReturningTheRealGovernanceDto(bool completeDuringRead)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = fixture.Host.Services.CreateAsyncScope();
        var services = request.ServiceProvider;
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var http = new DefaultHttpContext { RequestServices = services };
        accessor.HttpContext = http;
        try
        {
            // The existing controller also accepted an authenticated non-admin with a
            // validated active setup secret. Authenticate that human through the real scheme.
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            string credential = await fixture.SignInAsync();
            accessor.HttpContext = http;
            http.Request.Headers.Authorization = "Bearer " + credential;
            var authenticated = await http.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            http.User = authenticated.Principal!;
            string secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            http.Request.Headers["X-Setup-Secret"] = secret;
            bool completed = false;
            var secrets = Substitute.For<ISetupSecretProvider>();
            secrets.ValidateSecretAsync(secret, Arg.Any<CancellationToken>()).Returns(_ =>
                completed ? SetupSecretValidationOutcome.SetupCompleted : SetupSecretValidationOutcome.Accepted);
            var factory = new FooterExecutionScopeFactory(services, services.GetRequiredService<IPolicyMutationExecutionScopeFactory>());
            await using var global = await factory.CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
            var context = global.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(context.TenantFilterTenantId).IsNull();
            var execution = Execution(global.Services, fixture.Clock, new EfCoreUnitOfWork(context),
                new HttpFooterSetupReadAuthority(accessor, secrets));
            var query = new GetFooterGovernanceSettingsQuery();
            var handler = new GetFooterGovernanceSettingsQueryHandler(
                global.Services.GetRequiredService<IHierarchicalSettingsResolver>(),
                global.Services.GetRequiredService<IHumanCommitScopePreparation>(), execution);
            bool bodyObserved = false;
            var result = execution.ExecuteAsync(query, async token =>
            {
                var dto = await handler.QueryAsync(query, token);
                bodyObserved = true;
                completed = completeDuringRead;
                return dto;
            });
            if (completeDuringRead)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => result);
            else
                await Assert.That(await result).IsNotNull();
            await Assert.That(bodyObserved).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.QueryAsync(query, CancellationToken.None));
        }
        finally { accessor.HttpContext = null; }
    }

    private static HumanFooterOperationExecution Execution(IServiceProvider services, TimeProvider clock,
        ISerializableAttemptOwner owner, IFooterSetupReadAuthority? setup = null,
        CerbosAuthorizationService? cerbos = null) =>
        new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock,
            cerbos ?? services.GetRequiredService<CerbosAuthorizationService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions()),
            services.GetRequiredService<IOptions<CerbosSettings>>(),
            services.GetRequiredService<IMachinePrincipalAccessor>(),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => owner,
            () => new RelationalSettingMutationLock(services.GetRequiredService<ExploreDbContext>(),
                new EfCoreUnitOfWork(services.GetRequiredService<ExploreDbContext>())),
            () => new FooterExecutionScopeFactory(services, services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()),
            () => services.GetRequiredService<IHierarchicalSettingsResolver>(),
            () => setup ?? new HttpFooterSetupReadAuthority(services.GetRequiredService<IHttpContextAccessor>(),
                services.GetRequiredService<ISetupSecretProvider>()));

    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal string? Endpoint { get; private set; }

        internal Transport(ExploreDbContext owner, Effect effect)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(call =>
            {
                Endpoint = call.Arg<string>();
                return Client;
            });
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Footer PDP evaluation must precede the original transaction.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), effect);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }

    private sealed class AfterBody(ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                var result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }
}
