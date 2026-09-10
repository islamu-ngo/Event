
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Handlers.Queries;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Notifications.Handlers;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Mail;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryDisableCommandHandlerTests
{
    [Test]
    public async Task PreviewBindsActualAffectedScopesWithoutChangingSettings()
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open();
        string before = await scenario.StateAsync();
        var preview = await session.PreviewAsync(null);
        await using var observer = CreateContext(scenario.Path);
        long instanceRevision = (await observer.EmailDispatchProcessorStates.SingleAsync()).DeliveryPolicyRevision;
        long tenantRevision = await observer.EmailDispatchTenantControls
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
            .Where(row => row.TenantId == scenario.InheritedTenantId).Select(row => row.DeliveryPolicyRevision).SingleAsync();

        await Assert.That(preview.ExpectedRevision).IsEqualTo(instanceRevision);
        await Assert.That(preview.AffectedScopes.Length).IsEqualTo(2);
        await Assert.That(preview.AffectedScopes[0].TenantId).IsNull();
        await Assert.That(preview.AffectedScopes[0].Revision).IsEqualTo(instanceRevision);
        await Assert.That(preview.AffectedScopes[1].TenantId).IsEqualTo(scenario.InheritedTenantId);
        await Assert.That(preview.AffectedScopes[1].Revision).IsEqualTo(tenantRevision);
        var snapshot = new EmailDeliveryDisableImpactSnapshot(null, instanceRevision, false,
            [new(null, instanceRevision), new(scenario.InheritedTenantId, tenantRevision)]);
        await Assert.That(scenario.Tokens.Matches(preview.ConfirmationToken, scenario.PlatformActorId, snapshot)).IsTrue();
        await Assert.That(preview.ExpiresAtUtc).IsEqualTo(scenario.Clock.GetUtcNow().AddMinutes(5));
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
        await session.AssertFullLeaseAsync();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LockedOrNoOpPreviewNeverIssuesConfirmation(bool locked)
    {
        await using var scenario = await Scenario.CreateAsync();
        await using (var writer = CreateContext(scenario.Path))
        {
            if (locked)
                await SetEmailSettingAsync(writer, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
            else
                await ConfirmEmailDisableAsync(writer, scenario.TenantId, scenario.TenantActorId);
        }
        using var session = scenario.Open(tenantActor: true);
        string before = await scenario.StateAsync();
        var result = await session.Preview.Handle(new(scenario.TenantId), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.IsLocked).IsEqualTo(locked);
        await Assert.That(result.Id.CanDisable).IsFalse();
        await Assert.That(result.Id.ConfirmationToken).IsNull();
        await Assert.That(result.Id.ExpiresAtUtc).IsNull();
        if (!locked) await Assert.That(result.Id.AffectedScopes).IsEmpty();
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
    }

    [Test]
    [Arguments("missing-actor")]
    [Arguments("instance-target")]
    [Arguments("other-tenant")]
    [Arguments("revoked-admin")]
    public async Task UnauthorizedTargetsCannotPreviewOrDisable(string reason)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open(tenantActor: true,
            actor: reason == "missing-actor" ? Guid.Empty : null,
            currentTenant: reason == "other-tenant" ? scenario.InheritedTenantId : null);
        if (reason == "revoked-admin") await scenario.RevokeAsync(tenantActor: true);
        Guid? target = reason == "instance-target" ? null : scenario.TenantId;
        string before = await scenario.StateAsync();
        var preview = await session.Preview.Handle(new(target), CancellationToken.None);
        var result = await session.Disable.Handle(new(target, 0, null, null), CancellationToken.None);

        await Assert.That(preview.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoLeaseOrEffectsAsync();
    }

    [Test]
    public async Task EmptyTenantTargetReturnsValidationBeforeSharedLease()
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open();
        string before = await scenario.StateAsync();
        var preview = await session.Preview.Handle(new(Guid.Empty), CancellationToken.None);
        var result = await session.Disable.Handle(new(Guid.Empty, 0,
            DisableEmailDeliveryCommand.RequiredAcknowledgement, null), CancellationToken.None);

        await Assert.That(preview.IsSuccess).IsFalse();
        await Assert.That(preview.Errors).IsNotNull();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoLeaseOrEffectsAsync();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, false)]
    [Arguments(true, true)]
    [Arguments(false, true)]
    public async Task AuthorityRevokedWhileAcquiringLeaseIsRechecked(bool preview, bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        Guid? target = tenantTarget ? scenario.TenantId : null;
        DisableEmailDeliveryCommand command;
        using (var issuer = scenario.Open(tenantActor: tenantTarget))
            command = Command(await issuer.PreviewAsync(target));
        using var session = scenario.Open(tenantActor: tenantTarget);
        var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.BeforeLock = (key, _) =>
        {
            if (key == GovernanceSettingKeys.Email.DeliveryEnabled) reachedLock.TrySetResult();
            return Task.CompletedTask;
        };
        var ownsLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var holderContext = CreateContext(scenario.Path);
        var holderLock = new RelationalSettingMutationLock(holderContext, new EfCoreUnitOfWork(holderContext));
        Task holder = holderLock.ExecuteOrderedGroupsAsync([EmailDeliverySettingKeys.All], async token =>
        {
            ownsLock.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            return true;
        }, cancellation.Token);
        Task<string?>? action = null;
        try
        {
            await ownsLock.Task.WaitAsync(TimeSpan.FromSeconds(15));
            action = session.FailureAsync(preview, command, cancellation.Token);
            var observed = await Task.WhenAny(reachedLock.Task, session.Transactions.Started.Task, action)
                .WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(observed).IsSameReferenceAs(reachedLock.Task);
            await Assert.That(session.Transactions.Started.Task.IsCompleted).IsFalse();
            await Assert.That(action.IsCompleted).IsFalse();
            await scenario.RevokeAsync(tenantTarget);
            string before = await scenario.StateAsync();
            release.TrySetResult();
            await holder;
            await Assert.That(await action.WaitAsync(TimeSpan.FromSeconds(15))).IsEqualTo(FailureCodes.AdminRequired);
            await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
            await session.AssertNoEffectsAsync();
            await session.AssertFullLeaseAsync();
        }
        finally
        {
            release.TrySetResult();
            await holder;
            if (action is not null) await action;
        }
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task CachedAdministrationCannotOverrideRevokedGrant(bool preview, bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open(tenantActor: tenantTarget);
        await Assert.That(await session.CachedAdminAsync(tenantTarget)).IsTrue();
        await scenario.RevokeAsync(tenantTarget);
        await Assert.That(await session.CachedAdminAsync(tenantTarget)).IsTrue();
        string before = await scenario.StateAsync();

        string? failure = await session.FailureAsync(preview,
            new(tenantTarget ? scenario.TenantId : null, 0, DisableEmailDeliveryCommand.RequiredAcknowledgement, null));

        await Assert.That(failure).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoLeaseOrEffectsAsync();
    }

    [Test]
    public async Task MissingPreviewScopeReturnsNotFoundWithoutMutation()
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open();
        string before = await scenario.StateAsync();
        // The instance always exists; a nonexistent tenant is the native reader's missing-scope boundary.
        var preview = await session.Preview.Handle(new(Guid.CreateVersion7()), CancellationToken.None);

        await Assert.That(preview.FailureCode).IsEqualTo(FailureCodes.NotFound);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisablePublishesCommittedWriterChangesWithCurrentActorAndExactTarget(bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open(tenantActor: tenantTarget);
        // A real cached denial must not override a newly granted database role.
        await scenario.RevokeAsync(tenantTarget);
        await Assert.That(await session.CachedAdminAsync(tenantTarget)).IsFalse();
        await scenario.RestoreAsync(tenantTarget);
        await Assert.That(await session.CachedAdminAsync(tenantTarget)).IsFalse();
        Guid? target = tenantTarget ? scenario.TenantId : null;
        Guid actor = tenantTarget ? scenario.TenantActorId : scenario.PlatformActorId;
        var preview = await session.PreviewAsync(target);
        if (tenantTarget)
        {
            await Assert.That(preview.AffectedScopes.Length).IsEqualTo(1);
            await Assert.That(preview.AffectedScopes[0].TenantId).IsEqualTo(target);
        }
        await session.PrimeStaleCacheAsync();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Fixture.Notifications.OnPublishing = async (notification, token) =>
        {
            await using var observer = CreateContext(scenario.Path);
            var changed = await ReadSettingAsync(observer, target);
            await Assert.That(changed.Value).IsEqualTo("false");
            await Assert.That(changed.Actor).IsEqualTo(actor);
            await Assert.That(changed.UpdatedAt).IsEqualTo(notification.ChangedAt);
            await Assert.That(await session.CachedSentinelAsync()).IsTrue();
            await Assert.That(session.Audit.Entries).IsEmpty();
            await Assert.That(token.CanBeCanceled).IsFalse();
            published.SetResult();
        };

        var response = await session.Disable.Handle(Command(preview), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await published.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var verification = CreateContext(scenario.Path);
        await Assert.That((await ReadSettingAsync(verification, tenantTarget ? null : scenario.TenantId)).Value).IsEqualTo("true");
        await Assert.That(await session.CachedSentinelAsync()).IsFalse();
        var notification = session.Fixture.Notifications.Published.Single();
        await Assert.That(notification.TenantId).IsEqualTo(target);
        await Assert.That(notification.ActorUserId).IsEqualTo(actor);
        await Assert.That(notification.Key).IsEqualTo(GovernanceSettingKeys.Email.DeliveryEnabled);
        await Assert.That(notification.OldValue).IsEqualTo("true");
        await Assert.That(notification.NewValue).IsEqualTo("false");
        await Assert.That(notification.Scope).IsEqualTo(tenantTarget ? SettingSource.TenantLocked : SettingSource.SystemDefault);
        var audit = session.Audit.Entries.Single();
        await Assert.That(audit["SettingKey"]).IsEqualTo(notification.Key);
        await Assert.That(audit["ActorUserId"]).IsEqualTo(actor);
        await Assert.That(audit["TenantId"]).IsEqualTo(target);
        await Assert.That(audit["OldValue"]).IsEqualTo("true");
        await Assert.That(audit["NewValue"]).IsEqualTo("false");
        await session.AssertFullLeaseAsync();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("trailing-space")]
    [Arguments("lowercase")]
    public async Task ExactAcknowledgementCannotBeReplacedByValidToken(string? invalid)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open();
        var command = Command(await session.PreviewAsync(null)) with
        {
            Acknowledgement = invalid switch
            {
                "trailing-space" => DisableEmailDeliveryCommand.RequiredAcknowledgement + " ",
                "lowercase" => DisableEmailDeliveryCommand.RequiredAcknowledgement.ToLowerInvariant(),
                _ => invalid
            }
        };
        await session.PrimeStaleCacheAsync();
        string before = await scenario.StateAsync();
        var result = await session.Disable.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
        await Assert.That(await session.CachedSentinelAsync()).IsTrue();
    }

    [Test]
    [Arguments("actor")]
    [Arguments("target")]
    [Arguments("revision")]
    [Arguments("token")]
    public async Task ConfirmationBindsActorTargetRevisionAndProtectedToken(string changed)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var issuer = scenario.Open();
        var command = Command(await issuer.PreviewAsync(null));
        using var session = scenario.Open(actor: changed == "actor" ? scenario.OtherPlatformActorId : null);
        command = changed switch
        {
            "target" => command with { TenantId = scenario.TenantId },
            "revision" => command with { ExpectedRevision = command.ExpectedRevision + 1 },
            "token" => command with { ConfirmationToken = "invalid-protected-token" },
            _ => command
        };
        string before = await scenario.StateAsync();
        var result = await session.Disable.Handle(command, CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChangedRevisionOrAffectedMembershipRejectsOldConfirmation(bool addAffectedTenant)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open();
        var preview = await session.PreviewAsync(null);
        await using (var writer = CreateContext(scenario.Path))
        {
            if (addAffectedTenant) await AddTenantAsync(writer);
            else await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.FromName, "\"Changed sender\"");
        }
        var fresh = await session.PreviewAsync(null);
        if (addAffectedTenant)
        {
            await Assert.That(fresh.ExpectedRevision).IsEqualTo(preview.ExpectedRevision);
            await Assert.That(fresh.AffectedScopes.Length).IsEqualTo(preview.AffectedScopes.Length + 1);
        }
        else await Assert.That(fresh.ExpectedRevision).IsGreaterThan(preview.ExpectedRevision);
        string before = await scenario.StateAsync();

        var result = await session.Disable.Handle(Command(preview), CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoEffectsAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmedTokenCannotReplayImmediatelyOrAfterReenable(bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open(tenantActor: tenantTarget);
        Guid? target = tenantTarget ? scenario.TenantId : null;
        var command = Command(await session.PreviewAsync(target));
        await Assert.That((await session.Disable.Handle(command, CancellationToken.None)).IsSuccess).IsTrue();
        string disabled = await scenario.StateAsync();
        await Assert.That((await session.Disable.Handle(command, CancellationToken.None)).FailureCode)
            .IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(disabled);
        await using (var writer = CreateContext(scenario.Path))
            await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.DeliveryEnabled, "true", tenantId: target);
        string reenabled = await scenario.StateAsync();

        var replay = await session.Disable.Handle(command, CancellationToken.None);

        await Assert.That(replay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(reenabled);
        await Assert.That(session.Fixture.Notifications.Published.Count).IsEqualTo(1);
        await Assert.That(session.Audit.Entries.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedCommitLeavesPolicyCacheAndAuditUntouched(bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        using var session = scenario.Open(tenantActor: tenantTarget);
        Guid? target = tenantTarget ? scenario.TenantId : null;
        var command = Command(await session.PreviewAsync(target));
        await session.PrimeStaleCacheAsync();
        string before = await scenario.StateAsync();
        var context = session.Fixture.Context;
        var entity = context.Model.FindEntityType(tenantTarget ? typeof(EmailDispatchTenantControl) : typeof(EmailDispatchProcessorState))!;
        string table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!);
        await context.Database.OpenConnectionAsync();
        await using (var trigger = context.Database.GetDbConnection().CreateCommand())
        {
            trigger.CommandText = $"CREATE TRIGGER reject_handler_disable AFTER UPDATE ON {table} BEGIN SELECT RAISE(ABORT, 'handler_disable_rejected'); END";
            await trigger.ExecuteNonQueryAsync();
        }
        await context.Database.CloseConnectionAsync();
        Exception? failure = null;
        try { await session.Disable.Handle(command, CancellationToken.None); }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException) { failure = exception; }

        await Assert.That(failure).IsNotNull();
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await Assert.That(await session.CachedSentinelAsync()).IsTrue();
        await session.AssertNoEffectsAsync();
    }

    private static DisableEmailDeliveryCommand Command(EmailDeliveryDisablePreviewDto preview) =>
        new(preview.TenantId, preview.ExpectedRevision, DisableEmailDeliveryCommand.RequiredAcknowledgement, preview.ConfirmationToken);

    private static async Task<Guid> AddTenantAsync(ExploreDbContext context)
    {
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Native disable tenant",
            Slug = $"native-disable-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static async Task<(string Value, Guid? Actor, DateTime? UpdatedAt)> ReadSettingAsync(ExploreDbContext context, Guid? target)
    {
        if (target is Guid tenantId)
        {
            var row = await context.TenantSettingOverrides
                .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation).AsNoTracking()
                .SingleAsync(row => row.TenantId == tenantId && row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
            return (row.Value, row.UpdatedBy, row.UpdatedAt);
        }
        var system = await context.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled);
        return (system.Value, system.UpdatedBy, system.UpdatedAt);
    }

    private sealed class Scenario : IDisposable, IAsyncDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"email-disable-handlers-{Guid.CreateVersion7():N}.db");
        internal Guid PlatformActorId { get; private set; }
        internal Guid OtherPlatformActorId { get; private set; }
        internal Guid TenantActorId { get; private set; }
        internal Guid TenantId { get; private set; }
        internal Guid InheritedTenantId { get; private set; }
        internal FrozenClock Clock { get; } = new();
        internal EmailDeliveryDisableTokenService Tokens { get; }
        private PlatformUserRole _platformGrant = null!;

        private Scenario() => Tokens = new(new EphemeralDataProtectionProvider(), Clock);

        internal static async Task<Scenario> CreateAsync()
        {
            Scenario? scenario = null;
            try
            {
                scenario = new Scenario();
                await CreateDatabaseAsync(scenario.Path);
                await using var context = CreateContext(scenario.Path);
                await SetEmailSettingAsync(context, GovernanceSettingKeys.TenantDelegation.LockSmtp, "false");
                scenario.PlatformActorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
                scenario.OtherPlatformActorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
                scenario._platformGrant = await context.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.UserId == scenario.PlatformActorId);
                scenario.TenantId = await AddTenantAsync(context);
                scenario.InheritedTenantId = await AddTenantAsync(context);
                var user = new User
                {
                    Id = Guid.CreateVersion7(),
                    CreatedAt = DateTime.UtcNow,
                    Pii = new UserPii { Email = $"native-disable-{Guid.CreateVersion7():N}@example.test", FirstName = "Tenant", LastName = "Admin" }
                };
                var membership = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = scenario.TenantId,
                    Tenant = null!,
                    UserId = user.Id,
                    User = user,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = scenario.TenantId,
                    Tenant = null!,
                    TenantUserId = membership.Id,
                    TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                scenario.TenantActorId = user.Id;
                await context.SaveChangesAsync();
                await ApplyEmailSettingsAsync(context,
                    [new(scenario.TenantId, GovernanceSettingKeys.Email.DeliveryEnabled, EmailDeliverySettingMutationKind.SetValue, "true", true),
                     new(scenario.TenantId, GovernanceSettingKeys.Email.SmtpHost, EmailDeliverySettingMutationKind.SetValue, "\"smtp.native-tenant.test\""),
                     new(scenario.TenantId, GovernanceSettingKeys.Email.FromAddress, EmailDeliverySettingMutationKind.SetValue, "\"events@native-tenant.test\"")]);
                // Ensure both affected scopes have an actual persisted revision to compare with the preview.
                await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.FromName, "\"Native handler sender\"");
                await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.EventReporting.IntakeEnabled)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "true"));
                var result = scenario;
                scenario = null;
                return result;
            }
            finally
            {
                scenario?.Dispose();
            }
        }

        internal Session Open(bool tenantActor = false, Guid? actor = null, Guid? currentTenant = null) =>
            new(this, actor ?? (tenantActor ? TenantActorId : PlatformActorId), currentTenant ?? TenantId);

        internal async Task RevokeAsync(bool tenantActor)
        {
            await using var context = CreateContext(Path);
            if (tenantActor)
                await context.TenantUserRoleGrants.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                    .Where(row => row.TenantId == TenantId).ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
            else
                await context.PlatformUserRoles.Where(row => row.UserId == PlatformActorId).ExecuteDeleteAsync();
        }

        internal async Task RestoreAsync(bool tenantActor)
        {
            await using var context = CreateContext(Path);
            if (tenantActor)
                await context.TenantUserRoleGrants.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                    .Where(row => row.TenantId == TenantId).ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, (DateTime?)null));
            else
            {
                context.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = _platformGrant.Id,
                    UserId = PlatformActorId,
                    User = null!,
                    RoleId = _platformGrant.RoleId,
                    Role = null!,
                    GrantedAt = _platformGrant.GrantedAt
                });
                await context.SaveChangesAsync();
            }
        }

        internal async Task<string> StateAsync()
        {
            await using var context = CreateContext(Path);
            // Detached persisted scalars include policy values, revisions, suppression, locks, and actor/timestamp metadata.
            var system = await context.SystemSettings.AsNoTracking().OrderBy(row => row.SettingKey)
                .Select(row => new { row.Id, row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy }).ToArrayAsync();
            var tenants = await context.TenantSettingOverrides.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.TenantId, row.SettingKey, row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy }).ToArrayAsync();
            var processor = await context.EmailDispatchProcessorStates.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.DeliveryPolicyRevision, row.OptionalSuppressedThroughRevision, row.OptionalSuppressedThroughUtc }).ToArrayAsync();
            var controls = await context.EmailDispatchTenantControls.IgnoreTenantFilter(TenantFilterBypassReasons.EmailDeliveryPolicyReconciliation)
                .AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.TenantId, row.DeliveryPolicyRevision, row.OptionalSuppressedThroughRevision, row.OptionalSuppressedThroughUtc }).ToArrayAsync();
            return JsonSerializer.Serialize(new { system, tenants, processor, controls });
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(Path);
            File.Delete(Path + "-wal");
            File.Delete(Path + "-shm");
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly Scenario _scenario;
        private readonly ServiceProvider _provider;
        internal InstanceSettingsCommandFixture Fixture { get; }
        internal TransactionObserver Transactions { get; } = new();
        internal List<string> LeaseKeys { get; } = [];
        internal Func<string, CancellationToken, Task>? BeforeLock { get; set; }
        internal AuditSink Audit { get; }
        internal PreviewEmailDeliveryDisableQueryHandler Preview { get; }
        internal DisableEmailDeliveryCommandHandler Disable { get; }

        internal Session(Scenario scenario, Guid actor, Guid currentTenant)
        {
            _scenario = scenario;
            var context = CreateContext(scenario.Path, Transactions);
            var tenantContext = new BoundTenantContext(currentTenant);
            context.TenantContext = tenantContext;
            var mutationLock = new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context), async (key, token) =>
            {
                if (context.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException("The full SMTP lease must precede the transaction.");
                LeaseKeys.Add(key);
                if (BeforeLock is not null) await BeforeLock(key, token);
            });
            Fixture = new InstanceSettingsCommandFixture(context, actor, mutationLock);
            Audit = new AuditSink(context);
            _provider = new ServiceCollection()
                .AddSingleton<INotificationHandler<SettingChangedNotification>>(Fixture.Notifications)
                .AddSingleton<INotificationHandler<SettingChangedNotification>>(new SettingCacheInvalidationHandler(Fixture.Settings, [], []))
                .AddSingleton<INotificationHandler<SettingChangedNotification>>(new SettingAuditLogHandler(Audit))
                .BuildServiceProvider();
            var platformRoles = new PlatformUserRoleRepository(context);
            var tenantRoles = new TenantUserRoleGrantRepository(context);
            Preview = new(Fixture.AdminContext, tenantContext, new EmailDeliveryDisableImpactReader(context), scenario.Tokens,
                mutationLock, Fixture.UnitOfWork, platformRoles, tenantRoles);
            Disable = new(Fixture.AdminContext, tenantContext, CreateEmailSettingsWriter(context, mutationLock, scenario.Tokens),
                mutationLock, Fixture.UnitOfWork, new Mediator(_provider), platformRoles, tenantRoles);
        }

        internal async Task<EmailDeliveryDisablePreviewDto> PreviewAsync(Guid? target)
        {
            var result = await Preview.Handle(new(target), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id!.CanDisable).IsTrue();
            await Assert.That(result.Id.ConfirmationToken).IsNotNull();
            return result.Id;
        }

        internal async Task<string?> FailureAsync(bool preview, DisableEmailDeliveryCommand command, CancellationToken token = default) =>
            preview ? (await Preview.Handle(new(command.TenantId), token)).FailureCode
                : (await Disable.Handle(command, token)).FailureCode;

        internal Task<bool> CachedAdminAsync(bool tenantTarget) => tenantTarget
            ? Fixture.AdminContext.IsTenantAdminAsync(_scenario.TenantId) : Fixture.AdminContext.IsInstanceAdminAsync();

        internal Task<bool> CachedSentinelAsync() => Fixture.Settings.ResolveAsync<bool>(
            GovernanceSettingKeys.EventReporting.IntakeEnabled, new SettingContext());

        internal async Task PrimeStaleCacheAsync()
        {
            await Assert.That(await CachedSentinelAsync()).IsTrue();
            await using var writer = CreateContext(_scenario.Path);
            await writer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.EventReporting.IntakeEnabled)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
            await Assert.That(await CachedSentinelAsync()).IsTrue();
        }

        internal async Task AssertNoEffectsAsync()
        {
            await Assert.That(Fixture.Notifications.Published).IsEmpty();
            await Assert.That(Audit.Entries).IsEmpty();
        }

        internal async Task AssertNoLeaseOrEffectsAsync()
        {
            await Assert.That(LeaseKeys).IsEmpty();
            await Assert.That(Transactions.Started.Task.IsCompleted).IsFalse();
            await AssertNoEffectsAsync();
        }

        internal async Task AssertFullLeaseAsync()
        {
            await Assert.That(LeaseKeys.ToHashSet(StringComparer.Ordinal).SetEquals(EmailDeliverySettingKeys.All)).IsTrue();
            await Assert.That(LeaseKeys[0]).IsEqualTo(GovernanceSettingKeys.Email.DeliveryEnabled);
            await Assert.That(Transactions.Isolations.Count).IsGreaterThan(0);
            await Assert.That(Transactions.Isolations.All(level => level == IsolationLevel.Serializable)).IsTrue();
        }

        public void Dispose()
        {
            _provider.Dispose();
            Fixture.Dispose();
            Fixture.Context.Dispose();
        }
    }

    private sealed class TransactionObserver : DbTransactionInterceptor
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<IsolationLevel> Isolations { get; } = [];
        public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        {
            Isolations.Add(result.IsolationLevel);
            Started.TrySetResult();
            return result;
        }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TransactionStarted(connection, eventData, result));
    }

    private sealed class AuditSink(ExploreDbContext context) : ILogger<SettingAuditLogHandler>
    {
        internal List<Dictionary<string, object?>> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Audit escaped before commit.");
            Entries.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(pair => pair.Key, pair => pair.Value));
        }
    }

    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed record BoundTenantContext(Guid TenantId) : ITenantContext;
}
