using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class ResourceVisitorImportConsumptionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedHumanWriterCannotSwitchToTheInternalSetupEntry(bool visitor)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        string key = visitor ? GovernanceSettingKeys.PublicExperience.VisitorAccessMode
            : GovernanceSettingKeys.EventResources.MaxActiveResources;
        if (visitor)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                selected.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyInternalAsync(
                    [new(null, key, VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")],
                    actorUserId: null, CancellationToken.None));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                selected.Services.GetRequiredService<IEventResourceSettingsWriter>().ApplyInternalAsync(
                    [new(null, key, EventResourceSettingMutationKind.SetValue, "12")],
                    actorUserId: null, CancellationToken.None));
        await Assert.That(await selected.Services.GetRequiredService<ISystemSettingRepository>().GetByKey(key)).IsNull();
    }

    [Test]
    public async Task BatchStorageFailure_RollsBackAcceptedVisitorWriteAndDefersAllNotifications()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(new RejectCatalogLabelSave());
        await GrantInstanceAsync(fixture);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var notifications = new Notifications();
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        string modeKey = GovernanceSettingKeys.PublicExperience.VisitorAccessMode;
        await Assert.ThrowsAsync<RejectedStorageWriteException>(async () =>
        {
            try
            {
                await handler.ExecuteAsync(new()
                {
                    Category = Explore.Domain.Settings.SettingRegistry.Get(modeKey)!.Category,
                    Scope = Explore.Domain.Settings.SettingScope.Instance,
                    Mode = BatchUpdateMode.Strict,
                    Values = new Dictionary<string, string>
                    {
                        [modeKey] = "AnonymousOnly",
                        [GovernanceSettingKeys.PublicExperience.EventCatalogLabel] = "Rejected label"
                    }
                }, CancellationToken.None);
            }
            catch (UnauthorizedAccessException refusal)
            {
                Console.WriteLine($"batch_source_refusal={refusal.Message}");
                throw;
            }
        });
        var systems = fixture.Services.GetRequiredService<ISystemSettingRepository>();
        await Assert.That(await systems.GetByKey(modeKey)).IsNull();
        await Assert.That(await systems.GetByKey(GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsNull();
        await Assert.That(notifications.Published).IsEmpty();
    }

    private sealed class RejectedStorageWriteException : Exception;

    private sealed class RejectCatalogLabelSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                    entry.Entity.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel
                    && entry.State is EntityState.Added or EntityState.Modified))
                throw new RejectedStorageWriteException();
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    public async Task NativeStrictBatchRejectsInvalidResourcePolicyAtomically()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var notifications = new Notifications();
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        var response = await handler.ExecuteAsync(new UpdateSettingBatchCommand
        {
            Category = EventResourceSettingDefinitions.Category,
            Scope = Explore.Domain.Settings.SettingScope.Instance,
            Mode = BatchUpdateMode.Strict,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.EventResources.AuditRetentionDays] = "5",
                [GovernanceSettingKeys.EventResources.MaxActiveResources] = "501"
            }
        }, CancellationToken.None);
        await Assert.That(response.Success).IsFalse();
        var settings = selected.Services.GetRequiredService<ISystemSettingRepository>();
        await Assert.That(await settings.GetByKey(GovernanceSettingKeys.EventResources.AuditRetentionDays)).IsNull();
        await Assert.That(await settings.GetByKey(GovernanceSettingKeys.EventResources.MaxActiveResources)).IsNull();
        await Assert.That(notifications.Published).IsEmpty();
    }

    [Test]
    public async Task SettingUpsertServiceUsesCoordinatedResourceWriter()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        await GrantInstanceAsync(fixture);
        var notifications = new Notifications();
        var service = ActivatorUtilities.CreateInstance<SettingUpsertService>(fixture.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[notifications]);
        string key = GovernanceSettingKeys.EventResources.MaxActiveResources;
        await service.UpsertValueAsync(key, "10");
        await service.UpsertLockAsync(key, "10", isLocked: true, actorId: null);
        var stored = (await fixture.Services.GetRequiredService<ISystemSettingRepository>().GetByKey(key))!;
        await Assert.That(stored.Value).IsEqualTo("10");
        await Assert.That(stored.IsLocked).IsTrue();
        await Assert.That(notifications.Published.Select(notification => notification.Key)).IsEquivalentTo([key]);
    }

    private static async Task GrantInstanceAsync(ResourceVisitorAuthenticatedFixture fixture)
    {
        fixture.Context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await fixture.Context.SaveChangesAsync();
    }

    private sealed class Notifications : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }
    }

    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "own_revocation")]
    [Arguments(true, "own_revocation")]
    [Arguments(false, "own_account_reassignment")]
    [Arguments(true, "own_account_reassignment")]
    [Arguments(false, "credential_expiry")]
    [Arguments(true, "credential_expiry")]
    public async Task BorrowedSettingsWriteRequiresOriginalHumanAtPhysicalCommit(bool visitor, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        Guid grantId = await GrantTenantAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        var subject = preparation.Subject!;
        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        var context = services.GetRequiredService<ExploreDbContext>();
        string key = visitor ? GovernanceSettingKeys.PublicExperience.VisitorAccessMode
            : GovernanceSettingKeys.EventResources.MaxActiveResources;
        string[] keys = visitor ? VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray()
            : EventResourceSettingMutationGuard.Keys.ToArray();
        var producer = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var original = visitor
            ? await producer.PrepareVisitorAsync(
                [new(PlatformDefaults.DefaultTenantId, key, VisitorAccessSettingMutationKind.SetValue,
                    "\"DirectoryListingOnly\"")], SettingsMutationOperation.TenantSettings, null, default)
            : await producer.PrepareResourceAsync(
                [new(PlatformDefaults.DefaultTenantId, key, EventResourceSettingMutationKind.SetValue, "12")],
                SettingsMutationOperation.TenantSettings, null, default);
        var execution = services.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync<bool>(
            [keys], token => producer.ExecutePreparedAsync(original, async (_, inner) =>
            {
                if (visitor)
                    (await services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                        [new(PlatformDefaults.DefaultTenantId, key, VisitorAccessSettingMutationKind.SetValue,
                            "\"DirectoryListingOnly\"")], subject, SettingsMutationOperation.TenantSettings,
                        actorUserId: null, inner)).EnsureAccepted();
                else
                    (await services.GetRequiredService<IEventResourceSettingsWriter>().ApplyAsync(
                        [new(PlatformDefaults.DefaultTenantId, key, EventResourceSettingMutationKind.SetValue, "12")],
                        subject, SettingsMutationOperation.TenantSettings, actorUserId: null, inner)).EnsureAccepted();

                if (change == "own_revocation")
                    await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                            fixture.Clock.GetUtcNow().UtcDateTime), inner);
                if (change == "own_account_reassignment")
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.NewGuid().ToString()), inner);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.Id == grantId, finalToken);
                    if (change == "credential_expiry")
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            - fixture.Clock.GetUtcNow());
                }, _ => { });
                return true;
            }, token));

        if (change == "unchanged")
            await Assert.That(await execution).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        observer.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
        observer.BindTenantLease();
        await Assert.That(await observer.TenantSettingOverrides.AsNoTracking()
            .AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == key))
            .IsEqualTo(change == "unchanged");
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking()
            .Where(row => row.Id == grantId).Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryTenantEntryCannotBorrowInstanceAdministration(bool visitor)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new AuthenticatedRequest(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        var preparation = request.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        if (visitor)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
                    [new(PlatformDefaults.DefaultTenantId, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                        VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")],
                    preparation.Subject!, SettingsMutationOperation.TenantSettings, null));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<IEventResourceSettingsWriter>().ApplyAsync(
                    [new(PlatformDefaults.DefaultTenantId, GovernanceSettingKeys.EventResources.MaxActiveResources,
                        EventResourceSettingMutationKind.SetValue, "12")],
                    preparation.Subject!, SettingsMutationOperation.TenantSettings, null));
    }

    private static async Task<Guid> GrantTenantAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        seed.TenantContext = new SelectedTenant(PlatformDefaults.DefaultTenantId);
        seed.BindTenantLease();
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
        await seed.SaveChangesAsync();
        return grant.Id;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    internal sealed class AuthenticatedRequest : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal IServiceProvider Services
        {
            get
            {
                // AuthenticateAsync's AsyncLocal assignment does not flow back to its caller.
                // Re-enter this real authenticated request before resolving its scoped ports.
                if (!ReferenceEquals(_accessor.HttpContext, Context))
                    _accessor.HttpContext = Context;
                return _scope.ServiceProvider;
            }
        }
        internal DefaultHttpContext Context { get; }

        internal AuthenticatedRequest(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
            _accessor = _scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            _scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal async Task AuthenticateAsync(string token)
        {
            _accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }
}
