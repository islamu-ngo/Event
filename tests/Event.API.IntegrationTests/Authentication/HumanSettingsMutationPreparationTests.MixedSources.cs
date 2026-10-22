using System.Diagnostics;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Operations;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("active")]
    [Arguments("revoked")]
    [Arguments("replaced")]
    [Arguments("original-horizon")]
    public async Task MixedTenantBatchRetainsTheGenuineForwardedSupportSession(string change)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedTenantAdministratorAsync(fixture);
        Guid sessionId;
        var horizon = fixture.Clock.GetUtcNow().AddMinutes(5);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.Enabled, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
            var row = await seed.TenantSettingOverrides.SingleOrDefaultAsync(setting =>
                setting.TenantId == PlatformDefaults.DefaultTenantId && setting.SettingKey == GovernanceSettingKeys.Email.SmtpPort);
            if (row is null)
                seed.TenantSettingOverrides.Add(new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = GovernanceSettingKeys.Email.SmtpPort,
                    Value = "2525",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    CreatedBy = fixture.Binding.LocalSubjectId
                });
            else row.Value = "2525";
            var support = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Mixed settings control", "mixed-control",
                fixture.Clock.GetUtcNow().AddMinutes(-1), horizon);
            sessionId = support.Id;
            seed.SupportAccessSessions.Add(support);
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        var sink = new MixedNotifications(owner, observation);
        observation.AfterObservation = async (_, _, token) =>
        {
            if (change is not ("revoked" or "replaced")) return;
            await using var competing = fixture.Native.CreateDatabase();
            if (change == "revoked")
                await competing.SupportAccessSessions.Where(session => session.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(session => session.StatusId,
                        (int)SupportAccessSessionStatusEnum.Revoked), token);
            else
                await competing.SupportAccessSessions.Where(session => session.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(session => session.ConcurrencyStamp, Guid.CreateVersion7()), token);
        };
        observation.AfterEffects = (_, selectedServices, _) =>
        {
            if (change == "original-horizon")
                selectedServices.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(horizon - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            return Task.CompletedTask;
        };
        var command = new UpdateSettingBatchCommand
        {
            Category = "Email",
            Scope = SettingScope.Tenant,
            Mode = BatchUpdateMode.BestEffort,
            Values = new Dictionary<string, string>
            {
                [GovernanceSettingKeys.Email.SmtpPort] = "2526",
                ["unregistered.mixed.key"] = "false"
            }
        };
        observation.Command = command;
        var attempt = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(services,
                (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink])
            .ExecuteAsync(command, CancellationToken.None);
        if (change == "active")
        {
            var response = await attempt;
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Single(result => result.Key == "unregistered.mixed.key").Applied).IsFalse();
        }
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        await Assert.That(sink.Published.Count).IsEqualTo(change == "active" ? 1 : 0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(change == "active" ? "2526" : "2525");
    }

    [Test]
    public async Task MixedTenantPurposeDoesNotBorrowTheInstanceAdministratorPredicate()
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var command = MixedCommand() with { Scope = SettingScope.Tenant };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
                .ExecuteAsync(command, CancellationToken.None));
        await Assert.That(observation.Executions).IsEqualTo(0);
    }

    [Test]
    [Arguments(BatchUpdateMode.Strict)]
    [Arguments(BatchUpdateMode.BestEffort)]
    public async Task GenuineTenantAdministratorStillCannotCompileInstanceOnlyMixedMembers(BatchUpdateMode mode)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedMixedTenantAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var command = MixedCommand() with { Scope = SettingScope.Tenant, Mode = mode };
        observation.Command = command;
        var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Results.Count).IsEqualTo(2);
        await Assert.That(response.Results.Any(result => result.Applied)).IsFalse();
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && (row.SettingKey == MixedEmailKey || row.SettingKey == MixedOtherKey))).IsFalse();
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("email")]
    [Arguments("non-email-group")]
    [Arguments("mixed")]
    public async Task ActualCommandSelectionPreservesPureBranchesAndSelectsOnlyTheMixedGroup(string family)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await SeedMixedSettingsAsync(fixture);
        if (family == "non-email-group")
        {
            await using var seed = fixture.Native.CreateDatabase();
            foreach (string key in PublicationPolicySettingKeys.All)
                await SeedSpecializedSettingAsync(seed, key, "true");
            await SeedSpecializedSettingAsync(seed, EventSettingDefinitions.MaxSessionsPerEvent.Key, "100");
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        var command = family switch
        {
            "ordinary" => MixedCommand() with { Values = new Dictionary<string, string> { [MixedOtherKey] = "false" } },
            "email" => MixedCommand() with { Values = new Dictionary<string, string> { [MixedEmailKey] = "false" } },
            "non-email-group" => MixedCommand() with
            {
                Category = "Events",
                Values = new Dictionary<string, string>
                {
                    [EventSettingDefinitions.RequireApproval.Key] = "true",
                    [EventSettingDefinitions.MaxSessionsPerEvent.Key] = "101"
                }
            },
            _ => MixedCommand()
        };
        observation.Command = command;
        var selection = await PolicyMutationCommandScope.SelectAsync(command, request.Services, CancellationToken.None);
        await Assert.That(selection!.Purpose).IsEqualTo(family switch
        {
            "email" => PolicyMutationPurpose.EmailDelivery,
            "mixed" => PolicyMutationPurpose.MixedSettings,
            _ => PolicyMutationPurpose.Standard
        });
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(selection, CancellationToken.None);
        var response = await ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services)
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.Success).IsTrue();
        await Assert.That(observation.Executions).IsEqualTo(family == "mixed" ? 1 : 0);
        await using var observer = fixture.Native.CreateDatabase();
        string keyToRead = family == "non-email-group" ? EventSettingDefinitions.MaxSessionsPerEvent.Key
            : family == "email" ? MixedEmailKey : MixedOtherKey;
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == keyToRead)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(family == "non-email-group" ? "101" : "false");
    }

    [Test]
    public async Task LostPhysicalCommitAcknowledgmentDoesNotActivateOrReplayTheMixedGroup()
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
        var sink = new MixedNotifications(owner, observation);
        using var acknowledgement = new MixedAcknowledgmentLoss(owner);
        var command = MixedCommand();
        observation.Command = command;
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
            ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services,
                    (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink])
                .ExecuteAsync(command, CancellationToken.None));
        await Assert.That(acknowledgement.Observed).IsTrue();
        await Assert.That(observation.Executions).IsEqualTo(1);
        await Assert.That(observation.EffectCallbacks).IsEqualTo(1);
        await Assert.That(sink.Published.Count).IsEqualTo(0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == MixedEmailKey || row.SettingKey == MixedOtherKey)
            .CountAsync(row => row.Value == "false")).IsEqualTo(2);
        await Assert.That(await observer.EmailDispatchProcessorStates.Where(row => row.ProcessorCode == "smtp")
            .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(18L);
    }

    private static async Task SeedMixedTenantAdministratorAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        seed.BindTenantLease();
        var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId);
        if (member is null)
        {
            member = new TenantUser
            {
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
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await seed.SaveChangesAsync();
    }

    private sealed class MixedAcknowledgmentLoss : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly ExploreDbContext owner;
        private readonly IDisposable all;
        private readonly List<IDisposable> listeners = [];
        private readonly object subscriptionGate = new();
        private bool disposed;
        private int injected;
        internal bool Observed => injected != 0;
        internal MixedAcknowledgmentLoss(ExploreDbContext owner)
        {
            this.owner = owner;
            all = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                lock (subscriptionGate)
                    if (!disposed) listeners.Add(listener.Subscribe(this));
        }
        public void OnNext(KeyValuePair<string, object?> entry)
        {
            if (entry.Key == RelationalEventId.TransactionCommitted.Name
                && entry.Value is TransactionEndEventData data && ReferenceEquals(data.Context, owner)
                && Interlocked.Exchange(ref injected, 1) == 0)
                throw new IOException("mixed_commit_acknowledgment_lost");
        }
        public void OnError(Exception error) => throw new InvalidOperationException("Mixed settlement observation failed.", error);
        public void OnCompleted() { }
        public void Dispose()
        {
            all.Dispose();
            lock (subscriptionGate)
            {
                disposed = true;
                foreach (var listener in listeners) listener.Dispose();
            }
        }
    }
}
