using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Retains real Local HTTP login requests while creating a fresh selected email owner for each
/// operation. The second actor is another provisioned, verified account on the same authority.
/// Setup and revocation never borrow the selected human connection.
/// </summary>
internal sealed class EmailDisableCommitAuthenticatedFixture : IAsyncDisposable
{
    private readonly LocalIdentityLifecycleHttpFixture local;
    private readonly WebApplicationFactory<Program> host;
    private readonly List<AuthenticatedRequest> requests = [];
    private AuthenticatedRequest primary = null!;
    private AuthenticatedRequest? secondary;

    internal Guid TenantId => PlatformDefaults.DefaultTenantId;
    internal Guid InheritedTenantId { get; private set; }
    internal Guid ActorId => primary.Subject.PrincipalId;
    internal RecordingNotifications Notifications { get; } = new();

    private EmailDisableCommitAuthenticatedFixture(LocalIdentityLifecycleHttpFixture local)
    {
        this.local = local;
        host = local.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Restore the production selected-lease adoption displaced by the HTTP fixture.
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
            // Observe actual post-commit publication without replacing production subscribers.
            services.AddSingleton<INotificationHandler<SettingChangedNotification>>(Notifications);
        }));
    }

    internal static async Task<EmailDisableCommitAuthenticatedFixture> CreateAsync(
        bool tenantAdministrator, bool secondAdministrator = false)
    {
        var local = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: true, emailEnabled: true);
        EmailDisableCommitAuthenticatedFixture? fixture = null;
        try
        {
            fixture = new(local);
            fixture.primary = await fixture.AuthenticateAsync(local.Login, local.Binding.LocalSubjectId);
            if (secondAdministrator)
            {
                var login = await local.Native.SeedLocalUserAsync(emailConfirmed: true);
                await using var identity = local.Native.CreateIdentityDatabase();
                Guid subject = await identity.Set<LocalIdentityUser>().Where(row => row.Email == login.Identifier)
                    .Select(row => row.Id).SingleAsync();
                fixture.secondary = await fixture.AuthenticateAsync(login, subject);
                await Assert.That(fixture.secondary.Subject.PrincipalId).IsNotEqualTo(fixture.ActorId);
                await Assert.That(fixture.secondary.Subject.AccountKey).IsNotEqualTo(fixture.primary.Subject.AccountKey);
            }
            await fixture.SeedAsync(tenantAdministrator);
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
    }

    private async Task<AuthenticatedRequest> AuthenticateAsync(LocalAuthRequestDto login, Guid expectedSubject)
    {
        var scope = host.Services.CreateAsyncScope();
        var request = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(TenantId);
            request.Request.Headers.Authorization = "Bearer " + await local.SignInAsync(login);
            accessor.HttpContext = request;
            var authentication = await request.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            request.User = authentication.Principal!;
            var subject = await HumanCommitSubject.ResolveAsync(request.User,
                scope.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
                CancellationToken.None)
                ?? throw new InvalidOperationException("The original linked Local account is required.");
            var lifetime = await scope.ServiceProvider.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(request.User, CancellationToken.None)
                ?? throw new InvalidOperationException("The original validated Local credential is required.");
            await Assert.That(subject.PrincipalId).IsEqualTo(expectedSubject);
            await Assert.That(subject.AccountKey).IsEqualTo(lifetime.AccountKey);
            await Assert.That(lifetime.LocalSession).IsNotNull();
            await Assert.That(lifetime.IsValidAt(local.Clock.GetUtcNow())).IsTrue();
            var retained = new AuthenticatedRequest(scope, request, accessor, subject, lifetime);
            requests.Add(retained);
            return retained;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    internal ExploreDbContext CreateIndependentContext() => local.Native.CreateDatabase();

    internal async Task<TResult> ExecuteAsync<TResult>(Guid? selectedTenant,
        Func<IServiceProvider, Task<TResult>> operation, bool differentActor = false)
    {
        var original = differentActor
            ? secondary ?? throw new InvalidOperationException("The second Local account must be authenticated first.")
            : primary;
        original.Accessor.HttpContext = original.Request;
        try
        {
            await using var selected = host.Services.CreateAsyncScope();
            var services = selected.ServiceProvider;
            services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(selectedTenant ?? TenantId);
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                PrimaryDatabaseConfiguration.Bind(services.GetRequiredService<IConfiguration>(),
                    selectedTenant.HasValue ? PrimaryDatabaseRole.EmailPolicyTenantMutation
                        : PrimaryDatabaseRole.EmailPolicyInstanceMutation));
            options.UseSnakeCaseNamingConvention();
            await using var context = new ExploreDbContext(options.Options);
            context.TenantContext = selectedTenant is Guid tenant ? new SelectedTenant(tenant) : null;
            context.CurrentUserService = original.Scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
            context.ClearTenantFilterBypass();
            await context.Database.OpenConnectionAsync();
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, selectedTenant, PolicyMutationPurpose.EmailDelivery);
            lease.BindHuman(original.Subject, selectedTenant.HasValue ? HumanAdministratorCommitPurpose.EmailTenant
                : HumanAdministratorCommitPurpose.EmailInstance, enrollment: null, original.Lifetime);
            await Assert.That(ReferenceEquals(services.GetRequiredService<ExploreDbContext>(), context)).IsTrue();
            var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
            await preparation.PrepareAsync(CancellationToken.None);
            await Assert.That(ReferenceEquals(preparation.Subject, original.Subject)).IsTrue();
            await Assert.That(ReferenceEquals(lease.HumanLifetime, original.Lifetime)).IsTrue();
            _ = services.GetRequiredService<IHumanActionCommitFinalizer>();
            _ = services.GetRequiredService<IInstanceAdministratorCommitAuthority>();
            _ = services.GetRequiredService<ITenantAdministratorCommitAuthority>();
            _ = services.GetRequiredService<IUnitOfWork>();
            _ = services.GetRequiredService<EmailDeliverySettingsWriter>();
            return await operation(services);
        }
        finally
        {
            original.Accessor.HttpContext = null;
        }
    }

    private async Task SeedAsync(bool tenantAdministrator)
    {
        await using var context = CreateIndependentContext();
        DateTime now = local.Clock.GetUtcNow().UtcDateTime;
        if (tenantAdministrator)
        {
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                UserId = ActorId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now,
                CreatedAt = now
            };
            context.TenantUsers.Add(membership);
            context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = now,
                CreatedAt = now
            });
        }
        else
            AddPlatformAdministrator(context, ActorId, now);
        if (secondary is not null)
            AddPlatformAdministrator(context, secondary.Subject.PrincipalId, now);
        await context.SaveChangesAsync();
        InheritedTenantId = await AddTenantAsync(context);
        await ApplyInternalAsync(context,
            [new(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, EmailDeliverySettingMutationKind.SetValue, "false"),
             new(null, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true"),
             new(null, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.fixture.test\""),
             new(null, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@fixture.test\""),
             new(null, GovernanceSettingKeys.Email.SmtpSecurity, EmailDeliverySettingMutationKind.SetValue, "\"StartTls\"")]);
        await ApplyInternalAsync(context,
            [new(TenantId, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true", IsLocked: true),
             new(TenantId, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.independent.test\""),
             new(TenantId, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@independent.test\"")],
            ActorId);
        var processor = await context.EmailDispatchProcessorStates.SingleAsync();
        processor.IsPaused = true;
        processor.PauseReason = "Operator maintenance";
        processor.PausedAt = now;
        processor.GlobalSmtpRateLimitPerMinuteOverride = 23;
        processor.OptionalRemindersDeferred = true;
        processor.SmtpAvailableTokens = 7;
        processor.SmtpRefillAt = now;
        foreach (Guid id in new[] { TenantId, InheritedTenantId })
        {
            var control = await context.EmailDispatchTenantControls
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .SingleOrDefaultAsync(row => row.TenantId == id);
            if (control is null)
            {
                control = new EmailDispatchTenantControl { Id = Guid.CreateVersion7(), TenantId = id, CreatedAt = now };
                context.EmailDispatchTenantControls.Add(control);
            }
            control.IsPaused = true;
            control.PauseReason = "Tenant maintenance";
            control.PausedAt = now;
            control.SmtpAvailableTokens = 3;
            control.SmtpRefillAt = now;
        }
        await context.SaveChangesAsync();
    }

    internal static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Confirmed disable tenant",
            Slug = $"disable-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    internal static async Task ApplyInternalAsync(ExploreDbContext context,
        System.Collections.Immutable.ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId = null)
    {
        var unitOfWork = new EfCoreUnitOfWork(context);
        var writer = new InternalEmailDeliverySettingsWriter(context,
            new RelationalSettingMutationLock(context, unitOfWork), unitOfWork);
        var result = await writer.ApplyInternalAsync(mutations, actorUserId, CancellationToken.None);
        await Assert.That(result.Status is EmailDeliverySettingsWriteStatus.Applied
            or EmailDeliverySettingsWriteStatus.NoChange).IsTrue();
    }

    private static void AddPlatformAdministrator(ExploreDbContext context, Guid actor, DateTime now) =>
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = actor,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = now
        });

    public async ValueTask DisposeAsync()
    {
        foreach (var request in requests)
        {
            request.Accessor.HttpContext = null;
            await request.Scope.DisposeAsync();
        }
        await host.DisposeAsync();
        await local.DisposeAsync();
    }

    internal sealed class RecordingNotifications : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record AuthenticatedRequest(AsyncServiceScope Scope, DefaultHttpContext Request,
        IHttpContextAccessor Accessor, HumanCommitSubject Subject, HumanAuthenticationLifetime Lifetime);
}
