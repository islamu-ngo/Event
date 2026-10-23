using System.Collections.Immutable;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    private const string MixedEmailKey = GovernanceSettingKeys.TenantDelegation.LockSmtp;
    private const string MixedOtherKey = GovernanceSettingKeys.TenantDelegation.LockAnalytics;

    [Test]
    [Arguments("unchanged")]
    [Arguments("unchanged-absence")]
    [Arguments("row-replaced")]
    [Arguments("absence-replaced")]
    [Arguments("metadata-replaced")]
    [Arguments("projection-replaced")]
    [Arguments("role-replaced")]
    [Arguments("credential-replaced")]
    [Arguments("request-principal-replaced")]
    [Arguments("expired-at-acquisition")]
    [Arguments("expired-after-effects")]
    [Arguments("rejected-after-effects")]
    public async Task ActualMixedBatchUsesOriginalSourcesAndSettlesTheCompleteGroup(string transition)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        Guid grantId = await SeedPlatformAdministratorAsync(fixture);
        bool originallyAbsent = transition is "unchanged-absence" or "absence-replaced";
        await SeedMixedSettingsAsync(fixture, originallyAbsent);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        var sink = new MixedNotifications(owner, observation);
        observation.AfterObservation = async (preparation, selectedServices, token) =>
        {
            await Assert.That(ReferenceEquals(preparation.Original, observation.Command)).IsTrue();
            var lease = selectedServices.GetRequiredService<ExploreDbContextExecutionLease>();
            await Assert.That(lease.PolicyPurpose).IsEqualTo(PolicyMutationPurpose.MixedSettings);
            await Assert.That(lease.HumanScope!.Purpose).IsEqualTo(HumanAdministratorCommitPurpose.MixedSettingsInstance);
            await Assert.That(owner.Database.CurrentTransaction).IsNull();
            if (transition == "request-principal-replaced")
                request.Context.User = new ClaimsPrincipal(request.Context.User);
            if (transition == "expired-at-acquisition")
                ExpireMixedCredential(fixture, selectedServices);
            if (transition == "credential-replaced")
            {
                await using var identity = fixture.Native.CreateIdentityDatabase();
                await identity.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")), token);
            }
            await using var competitor = fixture.Native.CreateDatabase();
            switch (transition)
            {
                case "row-replaced":
                    await competitor.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"), token);
                    break;
                case "absence-replaced":
                    competitor.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = MixedOtherKey,
                        Value = "true",
                        ValueType = SettingValueType.Boolean,
                        Category = "TenantDelegation",
                        CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                        CreatedBy = fixture.Binding.LocalSubjectId
                    });
                    await competitor.SaveChangesAsync(token);
                    break;
                case "metadata-replaced":
                    await competitor.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "external-metadata"), token);
                    break;
                case "projection-replaced":
                    await competitor.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.DeliveryPolicyRevision, 18L), token);
                    break;
                case "role-replaced":
                    var original = await competitor.PlatformUserRoles.SingleAsync(row => row.Id == grantId, token);
                    int roleId = original.RoleId;
                    competitor.PlatformUserRoles.Remove(original);
                    await competitor.SaveChangesAsync(token);
                    competitor.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = fixture.Binding.LocalSubjectId,
                        User = null!,
                        RoleId = roleId,
                        Role = null!,
                        GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                    });
                    await competitor.SaveChangesAsync(token);
                    break;
            }
        };
        observation.AfterEffects = async (_, selectedServices, token) =>
        {
            await Assert.That(await owner.SystemSettings.Where(row => row.SettingKey == MixedEmailKey)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo("false");
            await Assert.That(await owner.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo("false");
            if (transition == "expired-after-effects")
                selectedServices.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    ExpireMixedCredential(fixture, selectedServices);
                    return Task.CompletedTask;
                }, _ => { });
            if (transition == "rejected-after-effects")
                throw new MixedEffectRejected();
        };
        var command = MixedCommand();
        observation.Command = command;
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink]);
        var attempt = handler.ExecuteAsync(command, CancellationToken.None);
        bool accepted = transition is "unchanged" or "unchanged-absence";
        if (accepted)
            await Assert.That((await attempt).Success).IsTrue();
        else if (transition == "rejected-after-effects")
            await Assert.ThrowsAsync<MixedEffectRejected>(() => attempt);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await Assert.That(observation.Executions).IsEqualTo(1);
        await Assert.That(observation.EffectCallbacks).IsEqualTo(accepted || transition is "expired-after-effects" or "rejected-after-effects" ? 1 : 0);
        await Assert.That(sink.Published.Count).IsEqualTo(accepted ? 2 : 0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedEmailKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(accepted ? "false" : "true");
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
            .Select(row => row.Value).SingleOrDefaultAsync()).IsEqualTo(accepted || transition == "row-replaced" ? "false" : "true");
        await Assert.That(await observer.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(accepted || transition == "projection-replaced" ? 18L : 17L);
        if (transition == "metadata-replaced")
            await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
                .Select(row => row.Description).SingleAsync()).IsEqualTo("external-metadata");
    }

    [Test]
    [Arguments("email")]
    [Arguments("ordinary")]
    public async Task MissingRealWriterCompletionRollsBackTheOtherAuthenticSlice(string omitted)
    {
        var observation = new MixedObservation { SwallowOmission = true };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        var sink = new MixedNotifications(owner, observation);
        bool sawRealOtherEffect = false;
        observation.AfterEffects = async (_, _, token) =>
        {
            string key = omitted == "email" ? MixedOtherKey : MixedEmailKey;
            await Assert.That(await owner.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo("false");
            sawRealOtherEffect = true;
        };
        var command = MixedCommand();
        observation.Command = command;
        var genuine = services.GetRequiredService<IEmailDeliverySettingsWriter>();
        var adversary = new OmitMixedSlice(genuine, omitted);
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink], (IEmailDeliverySettingsWriter)adversary);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(sawRealOtherEffect).IsTrue();
        await Assert.That(sink.Published.Count).IsEqualTo(0);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedEmailKey || row.SettingKey == MixedOtherKey)
            .CountAsync(row => row.Value == "true")).IsEqualTo(2);
        await Assert.That(await observer.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(17L);
    }

    [Test]
    [Arguments(BatchUpdateMode.Strict)]
    [Arguments(BatchUpdateMode.BestEffort)]
    public async Task MixedClassificationExcludesInvalidInputBeforeTheAcceptedGroupIsFrozen(BatchUpdateMode mode)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var values = new Dictionary<string, string>
        {
            [MixedEmailKey] = "false",
            [MixedOtherKey] = "false",
            [GovernanceSettingKeys.TenantDelegation.LockMcp] = "not-a-boolean",
            [GovernanceSettingKeys.Email.SmtpPort] = "2526",
            ["unregistered.mixed.key"] = "false"
        };
        var command = MixedCommand() with { Values = values, Mode = mode };
        observation.Command = command;
        observation.AfterEffects = (mutation, _, _) =>
        {
            if (mutation.Accepted.Count != 2 || mutation.Accepted.ContainsKey(GovernanceSettingKeys.Email.SmtpPort)
                || mutation.Accepted.ContainsKey(GovernanceSettingKeys.TenantDelegation.LockMcp))
                throw new InvalidOperationException("Invalid input entered the accepted compound effect.");
            return Task.CompletedTask;
        };
        var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.Success).IsEqualTo(mode == BatchUpdateMode.BestEffort);
        await Assert.That(response.Results.Count(result => result.Applied)).IsEqualTo(mode == BatchUpdateMode.BestEffort ? 2 : 0);
        await Assert.That(response.Results.Single(result => result.Key == GovernanceSettingKeys.Email.SmtpPort).Applied).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == "unregistered.mixed.key")).IsFalse();
    }

    [Test]
    public async Task MixedEffectsCannotBeWidenedReplayedOrMovedToAnotherOriginalIssuer()
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        await using var other = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var command = MixedCommand();
        observation.Command = command;
        observation.AfterEffects = async (mutation, services, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                mutation.Store.ApplyOrdinaryAsync(mutation, GovernanceSettingKeys.TenantDelegation.LockStorage, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                mutation.Store.ApplyOrdinaryAsync(mutation, MixedOtherKey, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ((IMixedEmailDeliverySettingsWriter)services.GetRequiredService<IEmailDeliverySettingsWriter>())
                    .ApplyMixedAsync(mutation, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                other.Services.GetRequiredService<IHumanEmailMutationPreparation>()
                    .ExecuteMixedPreparedAsync<bool>(mutation, _ => throw new InvalidOperationException("Foreign issuer callback executed."), token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                other.Services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyMixedAsync(mutation, token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                    .CreateAsync(PolicyMutationScope.MixedSettingsTenant(Guid.CreateVersion7()), token));
        };
        var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.Success).IsTrue();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            selected.Services.GetRequiredService<IHumanEmailMutationPreparation>().ExecuteMixedPreparedAsync<bool>(
                observation.Prepared!, _ => throw new InvalidOperationException("Replayed callback executed.")));
        await Assert.That(observation.EffectCallbacks).IsEqualTo(1);
    }

    [Test]
    public async Task MixedNotificationsFollowPhysicalAcknowledgmentAndExpiredSourceCustody()
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        bool independentlyCommittedAfterAck = false;
        var sink = new MixedNotifications(owner, observation)
        {
            AfterAck = async token =>
            {
                if (independentlyCommittedAfterAck) return;
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => observation.Prepared!.Source.RequireSourcesAsync(token));
                await using var observer = fixture.Native.CreateDatabase();
                await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedEmailKey || row.SettingKey == MixedOtherKey)
                    .CountAsync(row => row.Value == "false", token)).IsEqualTo(2);
                await observer.SystemSettings.Where(row => row.SettingKey == MixedOtherKey)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "post-ack"), token);
                independentlyCommittedAfterAck = true;
            }
        };
        var command = MixedCommand();
        observation.Command = command;
        var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services,
                (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink])
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(independentlyCommittedAfterAck).IsTrue();
        await Assert.That(sink.Published.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MixedLocalCredentialsRejectExternalIdentityTopologyWithoutFallback()
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External,
            configureServices: services => ConfigureMixedObservation(services, observation, IdentityDatabaseTopology.External));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
                .ExecuteAsync(MixedCommand(), CancellationToken.None));
        await Assert.That(observation.Executions).IsEqualTo(0);
    }

    private static UpdateSettingBatchCommand MixedCommand() => new()
    {
        Category = "TenantDelegation",
        Scope = SettingScope.Instance,
        Mode = BatchUpdateMode.Strict,
        Values = new Dictionary<string, string> { [MixedEmailKey] = "false", [MixedOtherKey] = "false" }
    };

    private static async Task SeedMixedSettingsAsync(LocalIdentityLifecycleHttpFixture fixture, bool absent = false)
    {
        await using var seed = fixture.Native.CreateDatabase();
        await SeedSpecializedSettingAsync(seed, MixedEmailKey, "true");
        if (absent)
            await seed.SystemSettings.Where(row => row.SettingKey == MixedOtherKey).ExecuteDeleteAsync();
        else
            await SeedSpecializedSettingAsync(seed, MixedOtherKey, "true");
        var processor = await seed.EmailDispatchProcessorStates.SingleOrDefaultAsync(row => row.ProcessorCode == "smtp");
        if (processor is null)
            seed.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
            {
                Id = Guid.CreateVersion7(),
                ProcessorCode = "smtp",
                DeliveryPolicyRevision = 17,
                UpdatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                UpdatedBy = fixture.Binding.LocalSubjectId
            });
        else
            processor.DeliveryPolicyRevision = 17;
        await seed.SaveChangesAsync();
    }

    private static void ExpireMixedCredential(LocalIdentityLifecycleHttpFixture fixture, IServiceProvider services)
    {
        var lifetime = services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
    }

    private static void ConfigureMixedObservation(IServiceCollection services, MixedObservation observation,
        IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated)
    {
        services.AddHumanMixedSettingsSources(topology);
        services.AddScoped<HumanEmailMutationPreparation>();
        services.RemoveAll<IHumanEmailMutationPreparation>();
        services.AddScoped<IHumanEmailMutationPreparation>(provider =>
            new ObservedMixedEmailPreparation(provider.GetRequiredService<HumanEmailMutationPreparation>(), provider, observation));
    }

    private sealed class MixedObservation
    {
        internal UpdateSettingBatchCommand? Command { get; set; }
        internal MixedSettingsBatchMutation? Prepared { get; set; }
        internal int Executions { get; set; }
        internal int EffectCallbacks { get; set; }
        internal bool SwallowOmission { get; init; }
        internal Func<MixedSettingsBatchMutation, IServiceProvider, CancellationToken, Task>? AfterObservation { get; set; }
        internal Func<MixedSettingsBatchMutation, IServiceProvider, CancellationToken, Task>? AfterEffects { get; set; }
    }

    private sealed class MixedNotifications(ExploreDbContext owner, MixedObservation observation)
        : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Published { get; } = [];
        internal Func<CancellationToken, Task>? AfterAck { get; init; }
        public async Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (owner.Database.CurrentTransaction is not null || observation.EffectCallbacks != 1)
                throw new InvalidOperationException("Mixed activation preceded its physical acknowledgment.");
            if (AfterAck is not null) await AfterAck(cancellationToken);
            Published.Add(notification);
        }
    }

    // Transparent instrumentation: every authority/source/owner method delegates to
    // the registered real issuer. Hooks run only after its actual observation or body.
    private sealed class ObservedMixedEmailPreparation(IHumanEmailMutationPreparation inner,
        IServiceProvider services, MixedObservation observation) : IHumanEmailMutationPreparation
    {
        public async Task<MixedSettingsBatchMutation> PrepareMixedAsync(UpdateSettingBatchCommand command, CancellationToken cancellationToken = default)
        {
            var prepared = await inner.PrepareMixedAsync(command, cancellationToken);
            observation.Prepared = prepared;
            if (observation.AfterObservation is not null)
                await observation.AfterObservation(prepared, services, cancellationToken);
            return prepared;
        }
        public Task<T> ExecuteMixedPreparedAsync<T>(MixedSettingsBatchMutation mutation,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            observation.Executions++;
            return inner.ExecuteMixedPreparedAsync(mutation, async token =>
            {
                T result;
                try { result = await operation(token); }
                catch (MixedSliceOmitted) when (observation.SwallowOmission) { result = default!; }
                observation.EffectCallbacks++;
                if (observation.AfterEffects is not null) await observation.AfterEffects(mutation, services, token);
                return result;
            }, cancellationToken);
        }
        public void RequireMixedSource(IHumanMixedSettingsSource source) => inner.RequireMixedSource(source);
        public Task CloseMixedAsync(MixedSettingsBatchMutation mutation, Exception? primaryFailure) =>
            inner.CloseMixedAsync(mutation, primaryFailure);
        public Task<IPreparedHumanEmailMutation> PreparePreviewAsync(Guid? tenantId, CancellationToken cancellationToken = default) => inner.PreparePreviewAsync(tenantId, cancellationToken);
        public Task<IPreparedHumanEmailMutation> PrepareApplyAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, CancellationToken cancellationToken = default) => inner.PrepareApplyAsync(mutations, actorUserId, cancellationToken);
        public Task<IPreparedHumanEmailMutation> PrepareDisableAsync(EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default) => inner.PrepareDisableAsync(confirmation, cancellationToken);
        public Task<IPreparedHumanEmailMutation> PrepareAsync(SetInstanceTenantSettingCommand command, string storedValue, CancellationToken cancellationToken = default) => inner.PrepareAsync(command, storedValue, cancellationToken);
        public Task<IPreparedHumanEmailMutation> PrepareAsync(LockInstanceTenantSettingCommand command, CancellationToken cancellationToken = default) => inner.PrepareAsync(command, cancellationToken);
        public Task<IPreparedHumanEmailMutation> PrepareAsync(UnlockInstanceTenantSettingCommand command, CancellationToken cancellationToken = default) => inner.PrepareAsync(command, cancellationToken);
        public Task<T> ExecutePreparedAsync<T>(IPreparedHumanEmailMutation preparation, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => inner.ExecutePreparedAsync(preparation, operation, cancellationToken);
        public Task<T> ExecuteApplyAsync<T>(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, bool instanceTenantAction, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => inner.ExecuteApplyAsync(mutations, actorUserId, instanceTenantAction, operation, cancellationToken);
        public Task<T> ExecuteDisableAsync<T>(EmailDeliveryDisableConfirmation confirmation, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => inner.ExecuteDisableAsync(confirmation, operation, cancellationToken);
        public Task<Guid> RequireApplyAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, bool instanceTenantAction, CancellationToken cancellationToken = default) => inner.RequireApplyAsync(mutations, actorUserId, instanceTenantAction, cancellationToken);
        public Task<Guid> RequireDisableAsync(EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default) => inner.RequireDisableAsync(confirmation, cancellationToken);
        public Task<Guid> RequireImpactAsync(Guid? tenantId, CancellationToken cancellationToken = default) => inner.RequireImpactAsync(tenantId, cancellationToken);
    }

    // Deliberately dishonest writer/callback outputs are negative attacks, never
    // successful fixture authority. The real completion barrier must reject both.
    private sealed class OmitMixedSlice(IEmailDeliverySettingsWriter inner, string omitted)
        : IEmailDeliverySettingsWriter, IMixedEmailDeliverySettingsWriter
    {
        public async Task<EmailDeliverySettingsWriteResult> ApplyMixedAsync(MixedSettingsBatchMutation mutation, CancellationToken cancellationToken = default)
        {
            if (omitted == "email")
                return new(EmailDeliverySettingsWriteStatus.NoChange, []);
            await ((IMixedEmailDeliverySettingsWriter)inner).ApplyMixedAsync(mutation, cancellationToken);
            throw new MixedSliceOmitted();
        }
        public Task<EmailDeliverySettingsWriteResult> ApplyAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, CancellationToken cancellationToken = default) => inner.ApplyAsync(mutations, actorUserId, cancellationToken);
        public Task<EmailDeliverySettingsWriteResult> ApplyInstanceTenantAsync(ImmutableArray<EmailDeliverySettingMutation> mutations, Guid? actorUserId, CancellationToken cancellationToken = default) => inner.ApplyInstanceTenantAsync(mutations, actorUserId, cancellationToken);
        public Task<EmailDeliverySettingsWriteResult> DisableAsync(EmailDeliveryDisableConfirmation confirmation, CancellationToken cancellationToken = default) => inner.DisableAsync(confirmation, cancellationToken);
    }
    private sealed class MixedSliceOmitted : Exception;
    private sealed class MixedEffectRejected : Exception;
}
