using System.Security.Claims;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Application.UnitTests.Settings;

/// <summary>
/// Exercises real scalar handlers against conflicting storage images. The in-memory
/// preparation adapter is not evidence of PostgreSQL authority, transactions or fences.
/// </summary>
public sealed class AtprotoFederationSettingsCommandRoutingTests
{
    public enum Route { Update, Reset, Lock, Unlock, InstanceSet, InstanceLock, InstanceUnlock }

    [Test]
    [Arguments(Route.Update, SettingScope.Instance)]
    [Arguments(Route.Update, SettingScope.Tenant)]
    [Arguments(Route.Lock, SettingScope.Instance)]
    [Arguments(Route.Lock, SettingScope.Tenant)]
    [Arguments(Route.Unlock, SettingScope.Instance)]
    [Arguments(Route.Unlock, SettingScope.Tenant)]
    [Arguments(Route.InstanceSet, SettingScope.Tenant)]
    [Arguments(Route.InstanceLock, SettingScope.Tenant)]
    [Arguments(Route.InstanceUnlock, SettingScope.Tenant)]
    public async Task ScalarMutationChangesDedicatedStateWithoutRewritingLegacyState(Route route, SettingScope scope)
    {
        using var fixture = await Fixture.CreateAsync(route, scope);
        var legacyBefore = fixture.LegacySnapshot();

        BaseCommandResponse<Guid> result = await fixture.ExecuteAsync(route, scope);

        await Assert.That(result.IsSuccess).IsTrue();
        string expectedValue = route is Route.Update or Route.InstanceSet ? "true" : "false";
        bool expectedLock = route is Route.Lock or Route.InstanceLock;
        await Assert.That(fixture.DedicatedValue(scope)).IsEqualTo(expectedValue);
        await Assert.That(fixture.DedicatedLock(scope)).IsEqualTo(expectedLock);
        await Assert.That(fixture.DedicatedActor(scope)).IsEqualTo(fixture.ActorId);
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.Notifications.Items.Count).IsEqualTo(1);
        var notification = fixture.Notifications.Items.Single();
        await Assert.That(notification.ActorUserId).IsEqualTo(fixture.ActorId);
        await Assert.That(notification.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(notification.Key).IsEqualTo(Fixture.Key);
        if (route is Route.Update or Route.InstanceSet)
        {
            await Assert.That(notification.OldValue).IsEqualTo("false");
            await Assert.That(notification.NewValue).IsEqualTo("true");
        }
        await Assert.That(fixture.OtherTenant.Value).IsEqualTo("false");
        await Assert.That(fixture.OtherTenant.UpdatedBy).IsNull();
    }

    [Test]
    [Arguments(Route.Update)]
    [Arguments(Route.InstanceSet)]
    public async Task SettingATenantValuePreservesItsOwnLock(Route route)
    {
        using var fixture = await Fixture.CreateAsync(route, SettingScope.Tenant);
        fixture.DedicatedTenant!.SetLock(true, Fixture.CreatedAt, null);
        fixture.LegacyTenant!.IsLocked = true;

        var result = await fixture.ExecuteAsync(route, SettingScope.Tenant);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fixture.DedicatedTenant.Value).IsEqualTo("true");
        await Assert.That(fixture.DedicatedTenant.IsLocked).IsTrue();
        await Assert.That(fixture.DedicatedTenant.UpdatedBy).IsEqualTo(fixture.ActorId);
    }

    [Test]
    public async Task TenantResetRemovesOnlyTheDedicatedOverrideAndRestoresInheritance()
    {
        using var fixture = await Fixture.CreateAsync(Route.Reset, SettingScope.Tenant);
        fixture.DedicatedInstance.SetValue("true", Fixture.CreatedAt, null);
        var legacyBefore = fixture.LegacySnapshot();

        var result = await fixture.ExecuteAsync(Route.Reset, SettingScope.Tenant);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(fixture.DedicatedTenant).IsNull();
        await Assert.That(fixture.DedicatedInstance.Value).IsEqualTo("true");
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.OtherTenant.Value).IsEqualTo("false");
        await Assert.That(fixture.Notifications.Items.Count).IsEqualTo(1);
        var notification = fixture.Notifications.Items.Single();
        await Assert.That(notification.OldValue).IsEqualTo("false");
        await Assert.That(notification.NewValue).IsNull();
        await Assert.That(notification.ActorUserId).IsEqualTo(fixture.ActorId);
        await Assert.That(notification.TenantId).IsEqualTo(fixture.TenantId);
    }

    [Test]
    public async Task InstanceResetRemainsRejectedWithoutChangingEitherStore()
    {
        using var fixture = await Fixture.CreateAsync(Route.Reset, SettingScope.Instance);
        var legacyBefore = fixture.LegacySnapshot();

        var result = await fixture.ExecuteAsync(Route.Reset, SettingScope.Instance);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fixture.DedicatedInstance.Value).IsEqualTo("false");
        await Assert.That(fixture.DedicatedInstance.UpdatedBy).IsNull();
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.Notifications.Items).IsEmpty();
    }

    [Test]
    [Arguments(Route.Update)]
    [Arguments(Route.InstanceSet)]
    [Arguments(Route.InstanceLock)]
    [Arguments(Route.InstanceUnlock)]
    public async Task DedicatedInstanceLockCannotBeOverriddenByAnUnlockedLegacyParent(Route route)
    {
        using var fixture = await Fixture.CreateAsync(route, SettingScope.Tenant);
        fixture.DedicatedInstance.SetLock(true, Fixture.CreatedAt, null);
        var legacyBefore = fixture.LegacySnapshot();
        bool tenantLock = fixture.DedicatedTenant!.IsLocked;

        var result = await fixture.ExecuteAsync(route, SettingScope.Tenant);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fixture.DedicatedTenant.Value).IsEqualTo("false");
        await Assert.That(fixture.DedicatedTenant.IsLocked).IsEqualTo(tenantLock);
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.Notifications.Items).IsEmpty();
    }

    [Test]
    [Arguments(Route.InstanceSet)]
    [Arguments(Route.InstanceLock)]
    [Arguments(Route.InstanceUnlock)]
    public async Task InstanceTenantRoutesStillRequireCurrentInstanceAdministratorAuthority(Route route)
    {
        using var fixture = await Fixture.CreateAsync(route, SettingScope.Tenant);
        fixture.InstanceAdministrator = false;
        var legacyBefore = fixture.LegacySnapshot();

        var result = await fixture.ExecuteAsync(route, SettingScope.Tenant);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fixture.DedicatedTenant!.Value).IsEqualTo("false");
        await Assert.That(fixture.DedicatedTenant.UpdatedBy).IsNull();
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.Notifications.Items).IsEmpty();
    }

    [Test]
    [Arguments(Route.Update)]
    [Arguments(Route.Reset)]
    [Arguments(Route.Lock)]
    [Arguments(Route.Unlock)]
    public async Task OrdinaryTenantRoutesRejectMissingTenantAdministratorAuthority(Route route)
    {
        using var fixture = await Fixture.CreateAsync(route, SettingScope.Tenant);
        fixture.TenantAdministrator = false;
        var legacyBefore = fixture.LegacySnapshot();

        var result = await fixture.ExecuteAsync(route, SettingScope.Tenant);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(fixture.DedicatedTenant!.Value).IsEqualTo("false");
        await Assert.That(fixture.LegacySnapshot()).IsEqualTo(legacyBefore);
        await Assert.That(fixture.Notifications.Items).IsEmpty();
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled;
        internal static readonly DateTime CreatedAt = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        private readonly ServiceCollection services = new();
        private ServiceProvider provider = null!;
        internal Guid ActorId { get; } = Guid.CreateVersion7();
        internal Guid TenantId { get; } = Guid.CreateVersion7();
        internal bool InstanceAdministrator { get; set; } = true;
        internal bool TenantAdministrator { get; set; } = true;
        internal AtprotoFederationInstanceSetting DedicatedInstance { get; private set; } = null!;
        internal AtprotoFederationTenantSetting? DedicatedTenant { get; set; }
        internal AtprotoFederationTenantSetting OtherTenant { get; private set; } = null!;
        internal SystemSetting LegacyInstance { get; private set; } = null!;
        internal TenantSetting? LegacyTenant { get; set; }
        internal RecordingNotifications Notifications { get; } = new();

        internal static async Task<Fixture> CreateAsync(Route route, SettingScope scope)
        {
            var fixture = new Fixture();
            bool locked = route is Route.Unlock or Route.InstanceUnlock;
            fixture.DedicatedInstance = AtprotoFederationInstanceSetting.Create(Key, "false", scope == SettingScope.Instance && locked, CreatedAt, null);
            fixture.DedicatedTenant = AtprotoFederationTenantSetting.Create(fixture.TenantId, Key, "false", scope == SettingScope.Tenant && locked, CreatedAt, null);
            fixture.OtherTenant = AtprotoFederationTenantSetting.Create(Guid.CreateVersion7(), Key, "false", false, CreatedAt, null);
            fixture.LegacyInstance = new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = Key, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = scope == SettingScope.Instance && locked, CreatedAt = CreatedAt };
            fixture.LegacyTenant = new TenantSetting { Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!, SettingKey = Key, Value = "true", IsLocked = scope == SettingScope.Tenant && locked, CreatedAt = CreatedAt };
            await fixture.ConfigureAsync();
            return fixture;
        }

        internal Task<BaseCommandResponse<Guid>> ExecuteAsync(Route route, SettingScope scope) => route switch
        {
            Route.Update => Handler<UpdateSettingCommandHandler>().ExecuteAsync(new() { Key = Key, Scope = scope, Value = "true" }, CancellationToken.None),
            Route.Reset => Handler<ResetSettingCommandHandler>().ExecuteAsync(new() { Key = Key, Scope = scope }, CancellationToken.None),
            Route.Lock => Handler<LockSettingCommandHandler>().ExecuteAsync(new() { Key = Key, Scope = scope }, CancellationToken.None),
            Route.Unlock => Handler<UnlockSettingCommandHandler>().ExecuteAsync(new() { Key = Key, Scope = scope }, CancellationToken.None),
            Route.InstanceSet => Handler<SetInstanceTenantSettingCommandHandler>().ExecuteAsync(new(TenantId, Key, "true"), CancellationToken.None),
            Route.InstanceLock => Handler<LockInstanceTenantSettingCommandHandler>().ExecuteAsync(new(TenantId, Key), CancellationToken.None),
            Route.InstanceUnlock => Handler<UnlockInstanceTenantSettingCommandHandler>().ExecuteAsync(new(TenantId, Key), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

        private T Handler<T>() where T : class => ActivatorUtilities.CreateInstance<T>(provider);
        internal string DedicatedValue(SettingScope scope) => scope == SettingScope.Instance ? DedicatedInstance.Value : DedicatedTenant!.Value;
        internal bool DedicatedLock(SettingScope scope) => scope == SettingScope.Instance ? DedicatedInstance.IsLocked : DedicatedTenant!.IsLocked;
        internal Guid? DedicatedActor(SettingScope scope) => scope == SettingScope.Instance ? DedicatedInstance.UpdatedBy : DedicatedTenant!.UpdatedBy;
        internal string LegacySnapshot() => $"{LegacyInstance.Value}|{LegacyInstance.IsLocked}|{LegacyInstance.UpdatedBy}|{LegacyTenant?.Value}|{LegacyTenant?.IsLocked}|{LegacyTenant?.UpdatedBy}";

        private async Task ConfigureAsync()
        {
            services.AddLogging();
            var user = Add<ICurrentUserService>();
            user.UserId.Returns(ActorId);
            user.IsAuthenticated.Returns(true);
            Add<ITenantContext>().TenantId.Returns(TenantId);
            var admin = Add<IAdminContext>();
            admin.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(ActorId);
            admin.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(_ => InstanceAdministrator);
            admin.IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => InstanceAdministrator && call.Arg<Guid>() == ActorId);
            admin.IsTenantAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => TenantAdministrator && call.Arg<Guid>() == TenantId);
            admin.GetAdminTenantIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult<IReadOnlyList<Guid>>(TenantAdministrator && call.Arg<Guid>() == ActorId ? [TenantId] : []));
            Add<IInstanceAdministratorCommitAuthority>().IsInstanceAdministratorForCommitAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => InstanceAdministrator && call.Arg<Guid>() == ActorId);
            Add<ITenantAdministratorCommitAuthority>();
            Add<IHumanSubjectCommitAuthority>();
            var subject = await HumanCommitSubject.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", ActorId.ToString("D"))], "interactive")), Substitute.For<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), CancellationToken.None);
            if (subject is null) throw new InvalidOperationException("The fixture's interactive subject was not resolved.");
            var human = Add<IHumanCommitScopePreparation>();
            human.Subject.Returns(subject);

            var preparation = Add<IHumanSettingsMutationPreparation>();
            var prepared = Substitute.For<IPreparedHumanSettingsMutation>();
            preparation.PrepareAsync(Arg.Any<UpdateSettingCommand>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<ResetSettingCommand>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<LockSettingCommand>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<UnlockSettingCommand>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<SetInstanceTenantSettingCommand>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<LockInstanceTenantSettingCommand>(), Arg.Any<CancellationToken>()).Returns(prepared);
            preparation.PrepareAsync(Arg.Any<UnlockInstanceTenantSettingCommand>(), Arg.Any<CancellationToken>()).Returns(prepared);
            ExecutePrepared<bool>(preparation);
            ExecutePrepared<(BaseCommandResponse<Guid>, SettingChangedNotification?)>(preparation);
            services.AddSingleton<ISettingMutationLock>(new InMemoryMutationLock());
            services.AddSingleton<INotificationHandler<SettingChangedNotification>>(Notifications);
            ConfigureLegacyStorage();
            Add<IAtprotoFederationSettingsWriter>()
                .ApplyCurrentAsync(Arg.Any<HumanSettingsMutation>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(ApplyDedicated(call.Arg<HumanSettingsMutation>())));
            Add<IUserPreferenceRepository>();
            Add<IUnitOfWork>();
            Add<IPublicationPolicyMutationBoundary>();
            Add<IEmailDeliverySettingsWriter>();
            Add<IVisitorAccessSettingsWriter>();
            Add<IEventResourceSettingsWriter>();
            Add<ILocationPrivacyGovernanceMutationService>();
            Add<IHumanEmailMutationPreparation>();
            provider = services.BuildServiceProvider();
        }

        private void ConfigureLegacyStorage()
        {
            var system = Add<ISystemSettingRepository>();
            system.GetByKey(Key, Arg.Any<CancellationToken>()).Returns(_ => LegacyInstance);
            system.IsLocked(Key, Arg.Any<CancellationToken>()).Returns(_ => LegacyInstance.IsLocked);
            system.UpsertLockAsync(Arg.Any<SystemSetting>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                string previous = LegacyInstance.Value;
                LegacyInstance = call.Arg<SystemSetting>();
                return Task.FromResult<string?>(previous);
            });
            var tenant = Add<ITenantSettingRepository>();
            tenant.GetByTenantAndKey(TenantId, Key, Arg.Any<CancellationToken>()).Returns(_ => LegacyTenant);
            tenant.SetValueAsync(TenantId, Key, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid?>()).Returns(call =>
            {
                LegacyTenant!.Value = call.ArgAt<string>(2);
                LegacyTenant.UpdatedBy = call.ArgAt<Guid?>(4);
                return Task.CompletedTask;
            });
            tenant.LockAsync(TenantId, Key, ActorId, Arg.Any<CancellationToken>()).Returns(_ => ChangeLegacyLock(true));
            tenant.UnlockAsync(TenantId, Key, ActorId, Arg.Any<CancellationToken>()).Returns(_ => ChangeLegacyLock(false));

            var resolver = Add<IHierarchicalSettingsResolver>();
            resolver.ResolveWithMetadataAsync(Key, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var context = call.ArgAt<SettingContext>(1)
                    ?? throw new InvalidOperationException("The handler must supply its setting context.");
                return Task.FromResult(HierarchicalSettingMerge.Resolve(Key,
                    AtprotoFederationSettingFacts.Create(Key, DedicatedInstance,
                        context.TenantId.HasValue ? DedicatedTenant : null, context.TenantId)));
            });
            resolver.SetValueAsync(Key, Arg.Any<string>(), Arg.Any<SettingScope>(), Arg.Any<Guid>(), ActorId, Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.Arg<SettingScope>() == SettingScope.Instance) { LegacyInstance.Value = call.ArgAt<string>(1); LegacyInstance.UpdatedBy = ActorId; }
                else { LegacyTenant!.Value = call.ArgAt<string>(1); LegacyTenant.UpdatedBy = ActorId; }
                return Task.CompletedTask;
            });
            resolver.RemoveOverrideAsync(Key, SettingScope.Tenant, TenantId, ActorId, Arg.Any<CancellationToken>()).Returns(_ => { LegacyTenant = null; return Task.CompletedTask; });
            resolver.LockAsync(Key, SettingScope.Tenant, TenantId, ActorId, Arg.Any<CancellationToken>()).Returns(_ => { ChangeLegacyLock(true); return Task.CompletedTask; });
            resolver.UnlockAsync(Key, SettingScope.Tenant, TenantId, ActorId, Arg.Any<CancellationToken>()).Returns(_ => { ChangeLegacyLock(false); return Task.CompletedTask; });
        }

        private bool ChangeLegacyLock(bool value)
        {
            if (LegacyTenant is null || LegacyTenant.IsLocked == value) return false;
            LegacyTenant.IsLocked = value;
            LegacyTenant.UpdatedBy = ActorId;
            return true;
        }

        // Detached Domain state proves handler routing, not the production writer's source custody.
        private AtprotoFederationSettingWriteResult ApplyDedicated(HumanSettingsMutation effect)
        {
            bool instance = effect.Scope == SettingScope.Instance;
            if (effect.Key != Key || effect.UserId is not null ||
                (instance ? effect.TenantId is not null || effect.Operation != HumanSettingsOperation.InstanceSettings
                    : effect.Scope != SettingScope.Tenant || effect.TenantId != TenantId ||
                      effect.Operation is not (HumanSettingsOperation.TenantSettings or HumanSettingsOperation.InstanceTenantSettings)))
                throw new InvalidOperationException("The scalar effect does not belong to this fixture's selected setting owner.");

            string? previous = instance ? DedicatedInstance.Value : DedicatedTenant?.Value;
            bool? previousLock = instance ? DedicatedInstance.IsLocked : DedicatedTenant?.IsLocked;
            Guid? entityId = instance ? DedicatedInstance.Id : DedicatedTenant?.Id;
            AtprotoFederationSettingWriteResult Reject(string code) =>
                new(false, entityId, code, previous, previous, previousLock, previousLock);
            if (!instance && DedicatedInstance.IsLocked) return Reject("setting_locked");

            switch (effect.Kind)
            {
                case HumanSettingsMutationKind.SetValue:
                    string value = effect.Value ?? throw new InvalidOperationException("A scalar value is required.");
                    AtprotoFederationSettingPolicy.Validate(effect.Key, value, effect.Scope);
                    if (instance) DedicatedInstance.SetValue(value, CreatedAt, ActorId);
                    else if (DedicatedTenant is { } existing) existing.SetValue(value, CreatedAt, ActorId);
                    else DedicatedTenant = AtprotoFederationTenantSetting.Create(TenantId, Key, value, false, CreatedAt, ActorId);
                    break;
                case HumanSettingsMutationKind.SetLock:
                    bool locked = effect.IsLocked ?? throw new InvalidOperationException("A lock transition is required.");
                    if (instance) DedicatedInstance.SetLock(locked, CreatedAt, ActorId);
                    else if (DedicatedTenant is { } lockTarget) lockTarget.SetLock(locked, CreatedAt, ActorId);
                    else return Reject("setting_not_found");
                    break;
                case HumanSettingsMutationKind.Remove:
                    if (!AtprotoFederationSettingPolicy.CanReset(effect.Scope)) return Reject("invalid_scope");
                    DedicatedTenant = null;
                    break;
                default:
                    throw new InvalidOperationException("Unknown scalar federation transition.");
            }
            return new(true, entityId, null, previous,
                instance ? DedicatedInstance.Value : DedicatedTenant?.Value,
                previousLock, instance ? DedicatedInstance.IsLocked : DedicatedTenant?.IsLocked);
        }

        private T Add<T>() where T : class
        {
            var service = Substitute.For<T>();
            services.AddSingleton(service);
            return service;
        }

        private static void ExecutePrepared<T>(IHumanSettingsMutationPreparation preparation) =>
            preparation.ExecutePreparedAsync(Arg.Any<IPreparedHumanSettingsMutation>(), Arg.Any<Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<IPreparedHumanSettingsMutation, CancellationToken, Task<T>>>(1)(call.ArgAt<IPreparedHumanSettingsMutation>(0), call.ArgAt<CancellationToken>(2)));

        public void Dispose() => provider.Dispose();
    }

    private sealed class InMemoryMutationLock : ISettingMutationLock
    {
        public Task<T> ExecuteAsync<T>(string settingKey, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> settingKeys, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
        public Task<T> ExecuteOrderedGroupsAsync<T>(IEnumerable<IEnumerable<string>> settingKeyGroups, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class RecordingNotifications : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Items { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken) { Items.Add(notification); return Task.CompletedTask; }
    }
}
