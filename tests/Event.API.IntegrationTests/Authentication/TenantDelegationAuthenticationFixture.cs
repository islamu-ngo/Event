using System.Diagnostics;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Retains a genuine Local HTTP login while production scope factories select Standard
/// Global or tenant ownership. Only context adoption and observation are test composition.
/// </summary>
internal sealed class TenantDelegationAuthenticationFixture : IAsyncDisposable
{
    private readonly LocalIdentityLifecycleHttpFixture local;
    private readonly WebApplicationFactory<Program> host;
    private readonly ResourceVisitorImportConsumptionTests.AuthenticatedRequest request;

    internal Guid UserId => local.Binding.LocalSubjectId;
    internal Guid TenantId => PlatformDefaults.DefaultTenantId;
    internal Notifications Published { get; }
    internal ExploreDbContext CreateIndependentContext()
    {
        var context = local.Native.CreateDatabase();
        context.TenantContext = new SnapshotTenant(TenantId);
        return context;
    }

    private TenantDelegationAuthenticationFixture(LocalIdentityLifecycleHttpFixture local,
        WebApplicationFactory<Program> host,
        ResourceVisitorImportConsumptionTests.AuthenticatedRequest request, Notifications published)
    {
        this.local = local;
        this.host = host;
        this.request = request;
        Published = published;
    }

    internal static async Task<TenantDelegationAuthenticationFixture> CreateAsync(
        Func<string, CancellationToken, Task>? beforeLock = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var local = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        WebApplicationFactory<Program>? host = null;
        ResourceVisitorImportConsumptionTests.AuthenticatedRequest? request = null;
        try
        {
            await using (var seed = local.Native.CreateDatabase())
            {
                seed.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = local.Binding.LocalSubjectId,
                    User = null!,
                    RoleId = (int)RoleEnum.Admin,
                    Role = null!,
                    GrantedAt = local.Clock.GetUtcNow().UtcDateTime
                });
                await seed.SaveChangesAsync();
            }
            var published = new Notifications();
            host = local.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                // Restore production selected-context adoption replaced by the base HTTP fixture.
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
                ConfigureObservations(services, published, beforeLock);
                configureServices?.Invoke(services);
            }));
            request = new(host.Services);
            await request.AuthenticateAsync(await local.SignInAsync());
            return new(local, host, request, published);
        }
        catch
        {
            if (request is not null) await request.DisposeAsync();
            if (host is not null) await host.DisposeAsync();
            await local.DisposeAsync();
            throw;
        }
    }

    internal Task<IPolicyMutationExecutionScope> SelectAsync(bool tenant = false)
    {
        // Bind synchronously in the test caller's execution context: an AsyncLocal assignment
        // inside an awaited factory does not flow back to the code invoking the selected handler.
        request.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = request.Context;
        return SelectCoreAsync(tenant);
    }

    private async Task<IPolicyMutationExecutionScope> SelectCoreAsync(bool tenant)
    {
        var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new PolicyMutationScope(TenantId) : PolicyMutationScope.Instance,
                CancellationToken.None);
        try
        {
            var services = selected.Services;
            var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
            await preparation.PrepareAsync(CancellationToken.None);
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            await Assert.That(lease.PolicyPurpose).IsEqualTo(PolicyMutationPurpose.Standard);
            await Assert.That(lease.HumanScope!.Purpose).IsEqualTo(tenant
                ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance);
            await Assert.That(ReferenceEquals(lease.Context, services.GetRequiredService<ExploreDbContext>())).IsTrue();
            await Assert.That(ReferenceEquals(lease.HumanScope.Subject, preparation.Subject)).IsTrue();
            await Assert.That(preparation.Subject!.PrincipalId).IsEqualTo(UserId);
            await Assert.That(preparation.Subject.AccountKey).IsEqualTo(lease.HumanLifetime!.AccountKey);
            await Assert.That(lease.HumanLifetime.LocalSession).IsNotNull();
            await Assert.That(lease.HumanLifetime.IsValidAt(local.Clock.GetUtcNow())).IsTrue();
            return selected;
        }
        catch
        {
            await selected.DisposeAsync();
            throw;
        }
    }

    internal static void ConfigureObservations(IServiceCollection services, Notifications notifications,
        Func<string, CancellationToken, Task>? beforeLock = null)
    {
        services.AddScoped<ISettingMutationLock>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var lease = provider.GetRequiredService<ExploreDbContextExecutionLease>();
            return new RelationalSettingMutationLock(context, provider.GetRequiredService<IUnitOfWork>(),
                ReferenceEquals(lease.Context, context) && lease.HumanScope is not null ? beforeLock : null);
        });
        services.AddScoped<INotificationHandler<SettingChangedNotification>>(provider =>
            new CommittedNotifications(provider.GetRequiredService<ExploreDbContext>(), notifications));
    }

    /// <summary>Nonhuman persisted starting state, separate from every authenticated mutation.</summary>
    internal static async Task SeedSettingAsync(ExploreDbContext context, string key, string value, bool locked = false)
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
                CreatedAt = DateTime.UtcNow
            };
            context.SystemSettings.Add(setting);
        }
        setting.Value = value;
        setting.IsLocked = locked;
        await context.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await request.DisposeAsync();
        await host.DisposeAsync();
        await local.DisposeAsync();
    }

    internal sealed class Notifications
    {
        internal List<SettingChangedNotification> Items { get; } = [];
        internal Func<SettingChangedNotification, Task>? OnPublishing { get; set; }
    }

    internal sealed record SnapshotTenant(Guid TenantId) : ITenantContext;

    private sealed class CommittedNotifications(ExploreDbContext context, Notifications notifications)
        : INotificationHandler<SettingChangedNotification>
    {
        public async Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Settings notifications must follow the selected owner's commit.");
            if (notifications.OnPublishing is not null)
                await notifications.OnPublishing(notification);
            notifications.Items.Add(notification);
        }
    }

    /// <summary>
    /// Observes actual transactions on the selected context without replacing the production
    /// Global factory, which has no interceptor-injection port.
    /// </summary>
    internal sealed class TransactionObserver : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext owner;
        private readonly IDisposable allListeners;
        private readonly List<IDisposable> subscriptions = [];
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TransactionObserver(ExploreDbContext owner)
        {
            this.owner = owner;
            allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(this,
                    name => name == RelationalEventId.TransactionStarted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is TransactionEndEventData data && ReferenceEquals(data.Context, owner))
                Started.TrySetResult();
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => Started.TrySetException(error);

        public void Dispose()
        {
            allListeners.Dispose();
            foreach (var subscription in subscriptions)
                subscription.Dispose();
        }
    }
}
