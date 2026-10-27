using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Real Local login with a retained original request and an explicitly selected SQLite email owner.
/// Only owner context construction is local, so deterministic interceptors reach the mutation
/// connection; human authority, writer, finalizer and hierarchical resolver come from production DI.
/// </summary>
internal sealed class SmtpAuthenticatedFixture : IAsyncDisposable
{
    private readonly LocalIdentityLifecycleHttpFixture local;
    private readonly WebApplicationFactory<Program> host;
    private readonly AsyncServiceScope requestScope;
    private readonly DefaultHttpContext request;
    private readonly IHttpContextAccessor accessor;

    internal Guid TenantId => PlatformDefaults.DefaultTenantId;
    internal HumanCommitSubject Subject { get; private set; } = null!;
    internal HumanAuthenticationLifetime Lifetime { get; private set; } = null!;
    internal LocalIdentityLifecycleHttpFixture.LifecycleClock Clock => local.Clock;
    internal ExploreDbContext CreateIndependentContext() => local.Native.CreateDatabase();

    private SmtpAuthenticatedFixture(LocalIdentityLifecycleHttpFixture local,
        Action<IServiceCollection>? configureServices)
    {
        this.local = local;
        host = local.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // The base HTTP fixture replaces the production context registration. Restore selected
            // lease adoption here, without changing that shared fixture or any production root.
            services.AddScoped(provider =>
            {
                if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                    return selected;
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                context.TenantContext = provider.GetService<ITenantContext>();
                context.CurrentUserService = provider.GetService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                return context;
            });
            configureServices?.Invoke(services);
        }));
        requestScope = host.Services.CreateAsyncScope();
        request = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        requestScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(TenantId);
    }

    internal static async Task<SmtpAuthenticatedFixture> CreateAsync(
        bool instanceAdministrator = true, bool tenantAdministrator = true,
        Action<IServiceCollection>? configureServices = null)
    {
        var local = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: true, emailEnabled: true);
        SmtpAuthenticatedFixture? fixture = null;
        try
        {
            fixture = new(local, configureServices);
            await fixture.SeedAsync(instanceAdministrator, tenantAdministrator);
            fixture.accessor.HttpContext = fixture.request;
            fixture.request.Request.Headers.Authorization = "Bearer " + await local.SignInAsync();
            var authentication = await fixture.request.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            fixture.request.User = authentication.Principal!;
            var services = fixture.requestScope.ServiceProvider;
            fixture.Subject = await HumanCommitSubject.ResolveAsync(fixture.request.User,
                services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
                CancellationToken.None)
                ?? throw new InvalidOperationException("SMTP administration requires the original linked Local account.");
            fixture.Lifetime = await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(fixture.request.User, CancellationToken.None)
                ?? throw new InvalidOperationException("SMTP administration requires real validated credential custody.");
            await Assert.That(fixture.Subject.PrincipalId).IsEqualTo(local.Binding.LocalSubjectId);
            await Assert.That(fixture.Subject.AccountKey).IsEqualTo(fixture.Lifetime.AccountKey);
            await Assert.That(fixture.Lifetime.LocalSession).IsNotNull();
            await Assert.That(fixture.Lifetime.IsValidAt(fixture.Clock.GetUtcNow())).IsTrue();
            await Assert.That((await local.ReadIdentityAsync()).Verified).IsTrue();
            await Assert.That((await local.ReadMirrorAsync()).Verified).IsEqualTo(true);
            return fixture;
        }
        catch
        {
            if (fixture is not null)
                await fixture.DisposeAsync();
            else
                await local.DisposeAsync();
            throw;
        }
        finally
        {
            if (fixture is not null)
                fixture.accessor.HttpContext = null;
        }
    }

    internal async Task ExecuteAsync(bool tenantOwner, Func<IServiceProvider, Task> operation,
        IInterceptor? interceptor = null, Guid? selectedTenantId = null, IInterceptor? commandInterceptor = null,
        bool planAssignment = false)
    {
        // Retain the actual authenticated request, not a reconstructed principal or a new login.
        accessor.HttpContext = request;
        try
        {
            await using var selected = host.Services.CreateAsyncScope();
            var services = selected.ServiceProvider;
            Guid? tenant = tenantOwner ? selectedTenantId ?? TenantId : null;
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenant ?? TenantId);
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                PrimaryDatabaseConfiguration.Bind(services.GetRequiredService<IConfiguration>(),
                    planAssignment ? PrimaryDatabaseRole.Global
                        : tenantOwner ? PrimaryDatabaseRole.EmailPolicyTenantMutation
                        : PrimaryDatabaseRole.EmailPolicyInstanceMutation));
            options.UseSnakeCaseNamingConvention();
            if (interceptor is not null)
                options.AddInterceptors(interceptor);
            if (commandInterceptor is not null)
                options.AddInterceptors(commandInterceptor);
            await using var context = new ExploreDbContext(options.Options);
            context.TenantContext = tenant.HasValue ? new SelectedTenant(tenant.Value) : null;
            context.CurrentUserService = requestScope.ServiceProvider.GetRequiredService<ICurrentUserService>();
            context.ClearTenantFilterBypass();
            await context.Database.OpenConnectionAsync();
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, tenant, planAssignment ? PolicyMutationPurpose.MixedSettings : PolicyMutationPurpose.EmailDelivery);
            lease.BindHuman(Subject, planAssignment ? HumanAdministratorCommitPurpose.MixedSettingsTenant
                : tenantOwner ? HumanAdministratorCommitPurpose.EmailTenant
                : HumanAdministratorCommitPurpose.EmailInstance, enrollment: null, Lifetime);

            await Assert.That(ReferenceEquals(services.GetRequiredService<ExploreDbContext>(), context)).IsTrue();
            var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
            await preparation.PrepareAsync(CancellationToken.None);
            await Assert.That(ReferenceEquals(preparation.Subject, Subject)).IsTrue();
            // Resolve the real mandatory finalizer on the same lease now. Production's human
            // email writer must consume it; the fixture never registers admission on its behalf.
            _ = services.GetRequiredService<IHumanActionCommitFinalizer>();
            _ = services.GetRequiredService<IInstanceAdministratorCommitAuthority>();
            _ = services.GetRequiredService<ITenantAdministratorCommitAuthority>();
            _ = services.GetRequiredService<IUnitOfWork>();
            _ = services.GetRequiredService<EmailDeliverySettingsWriter>();
            await operation(services);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private async Task SeedAsync(bool instanceAdministrator, bool tenantAdministrator)
    {
        await using var context = local.Native.CreateDatabase();
        var values = new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Deployment.Mode] = "\"MultiTenant\"",
            [GovernanceSettingKeys.Email.DeliveryEnabled] = "true",
            [GovernanceSettingKeys.Email.SmtpHost] = "\"smtp.instance.test\"",
            [GovernanceSettingKeys.Email.FromAddress] = "\"events@instance.test\"",
            [GovernanceSettingKeys.Email.SmtpPort] = "587",
            [GovernanceSettingKeys.Email.SmtpSecurity] = "\"StartTls\"",
            [GovernanceSettingKeys.TenantDelegation.LockSmtp] = "false"
        };
        // Persisted nonhuman starting state, never a human writer called with null audit identity.
        foreach (var (key, value) in values)
        {
            var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
            {
                var definition = SettingRegistry.Get(key)!;
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = value,
                    ValueType = definition.ValueType,
                    CreatedAt = Clock.GetUtcNow().UtcDateTime
                };
                context.SystemSettings.Add(setting);
            }
            setting.Value = value;
        }
        foreach (var (key, value) in new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Email.SmtpHost] = "\"smtp.tenant.test\"",
            [GovernanceSettingKeys.Email.FromAddress] = "\"events@tenant.test\""
        })
            context.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                SettingKey = key,
                Value = value,
                CreatedAt = Clock.GetUtcNow().UtcDateTime
            });
        if (instanceAdministrator)
            context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = local.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = Clock.GetUtcNow().UtcDateTime
            });
        if (tenantAdministrator)
        {
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                UserId = local.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = Clock.GetUtcNow().UtcDateTime
            };
            context.TenantUsers.Add(member);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = Clock.GetUtcNow().UtcDateTime
            });
        }
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        accessor.HttpContext = null;
        await requestScope.DisposeAsync();
        await host.DisposeAsync();
        await local.DisposeAsync();
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
