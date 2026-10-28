using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.QueryFilters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class EmailDisableHandlerAuthenticationTests
{
    [Test]
    public async Task PreviewBindsActualAffectedScopesWithoutChangingSettings()
    {
        await using var scenario = await Scenario.CreateAsync();
        await using var session = await scenario.OpenAsync();
        string before = await scenario.StateAsync();
        var preview = await session.PreviewAsync(null);
        await using var observer = scenario.CreateIndependentContext();
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
        await using (var writer = scenario.CreateIndependentContext())
        {
            if (locked)
                await SetEmailSettingAsync(writer, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");
            else
                await ConfirmEmailDisableAsync(writer, scenario.TenantId, scenario.TenantActorId);
        }
        await using var session = await scenario.OpenAsync(tenantActor: true);
        string before = await scenario.StateAsync();
        var result = await session.PreviewCommandAsync(new(scenario.TenantId), CancellationToken.None);

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
        await using var session = await scenario.OpenAsync(tenantActor: true,
            actor: reason == "missing-actor" ? Guid.Empty : null,
            currentTenant: reason == "other-tenant" ? scenario.InheritedTenantId : null);
        if (reason == "revoked-admin") await scenario.RevokeAsync(tenantActor: true);
        Guid? target = reason == "instance-target" ? null : scenario.TenantId;
        string before = await scenario.StateAsync();
        var preview = await session.PreviewCommandAsync(new(target), CancellationToken.None);
        var result = await session.DisableCommandAsync(new(target, 0, null, null), CancellationToken.None);

        await Assert.That(preview.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await session.AssertNoLeaseOrEffectsAsync();
    }

    [Test]
    public async Task EmptyTenantTargetReturnsValidationBeforeSharedLease()
    {
        await using var scenario = await Scenario.CreateAsync();
        await using var session = await scenario.OpenAsync();
        string before = await scenario.StateAsync();
        var preview = await session.PreviewCommandAsync(new(Guid.Empty), CancellationToken.None);
        var result = await session.DisableCommandAsync(new(Guid.Empty, 0,
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
        await using (var issuer = await scenario.OpenAsync(tenantActor: tenantTarget))
            command = Command(await issuer.PreviewAsync(target));
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        var reachedLock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.BeforeLock = async (key, token) =>
        {
            if (key != GovernanceSettingKeys.Email.DeliveryEnabled) return;
            reachedLock.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<string?>? action = null;
        try
        {
            action = session.FailureAsync(preview, command, cancellation.Token);
            var observed = await Task.WhenAny(reachedLock.Task, session.Transactions.Started.Task, action)
                .WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(observed).IsSameReferenceAs(reachedLock.Task);
            await Assert.That(session.Transactions.Started.Task.IsCompleted).IsFalse();
            await Assert.That(action.IsCompleted).IsFalse();
            await scenario.RevokeAsync(tenantTarget);
            string before = await scenario.StateAsync();
            release.TrySetResult();
            await Assert.That(await action.WaitAsync(TimeSpan.FromSeconds(15))).IsEqualTo(FailureCodes.AdminRequired);
            await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
            await session.AssertNoEffectsAsync();
            await session.AssertFullLeaseAsync();
        }
        finally
        {
            release.TrySetResult();
            if (action is not null) await action;
        }
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(false, false)]
    public async Task EarlierAdministrationCannotOverrideRevokedGrant(bool preview, bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        await Assert.That(await session.CurrentAdminAsync(tenantTarget)).IsTrue();
        await scenario.RevokeAsync(tenantTarget);
        await Assert.That(await session.CurrentAdminAsync(tenantTarget)).IsFalse();
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
        await using var session = await scenario.OpenAsync();
        string before = await scenario.StateAsync();
        // The instance always exists; a nonexistent tenant is the native reader's missing-scope boundary.
        var preview = await session.PreviewCommandAsync(new(Guid.CreateVersion7()), CancellationToken.None);

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
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        // An earlier denial must not override a newly granted database role.
        await scenario.RevokeAsync(tenantTarget);
        await Assert.That(await session.CurrentAdminAsync(tenantTarget)).IsFalse();
        await scenario.RestoreAsync(tenantTarget);
        await Assert.That(await session.CurrentAdminAsync(tenantTarget)).IsTrue();
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
        session.Notifications.OnPublishing = async (notification, token) =>
        {
            await using var observer = scenario.CreateIndependentContext();
            var changed = await ReadSettingAsync(observer, target);
            await Assert.That(changed.Value).IsEqualTo("false");
            await Assert.That(changed.Actor).IsEqualTo(actor);
            await Assert.That(changed.UpdatedAt).IsEqualTo(notification.ChangedAt);
            await Assert.That(await session.CachedSentinelAsync()).IsTrue();
            await Assert.That(session.Audit.Entries).IsEmpty();
            await Assert.That(token.CanBeCanceled).IsFalse();
            published.SetResult();
        };

        var response = await session.DisableCommandAsync(Command(preview), CancellationToken.None);

        await Assert.That(response.IsSuccess).IsTrue();
        await published.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await using var verification = scenario.CreateIndependentContext();
        await Assert.That((await ReadSettingAsync(verification, tenantTarget ? null : scenario.TenantId)).Value).IsEqualTo("true");
        await Assert.That(await session.CachedSentinelAsync()).IsFalse();
        var notification = session.Notifications.Published.Single();
        await Assert.That(notification.TenantId).IsEqualTo(target);
        await Assert.That(notification.ActorUserId).IsEqualTo(actor);
        await Assert.That(notification.Key).IsEqualTo(GovernanceSettingKeys.Email.DeliveryEnabled);
        await Assert.That(notification.OldValue).IsEqualTo("true");
        await Assert.That(notification.NewValue).IsEqualTo("false");
        await Assert.That(notification.Scope).IsEqualTo(tenantTarget ? SettingSource.TenantLocked : SettingSource.SystemDefault);
        var audit = session.Audit.Entries.Single();
        await Assert.That(audit["Scope"]).IsEqualTo(notification.Scope.ToString());
        await Assert.That(audit.Keys.Where(key => key != "{OriginalFormat}")).IsEquivalentTo(["Scope"]);
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
        await using var session = await scenario.OpenAsync();
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
        var result = await session.DisableCommandAsync(command, CancellationToken.None);

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
        await using var issuer = await scenario.OpenAsync();
        var command = Command(await issuer.PreviewAsync(null));
        await using var session = await scenario.OpenAsync(actor: changed == "actor" ? scenario.OtherPlatformActorId : null,
            ownerTenant: changed == "target" ? scenario.TenantId : null);
        command = changed switch
        {
            "target" => command with { TenantId = scenario.TenantId },
            "revision" => command with { ExpectedRevision = command.ExpectedRevision + 1 },
            "token" => command with { ConfirmationToken = "invalid-protected-token" },
            _ => command
        };
        string before = await scenario.StateAsync();
        var result = await session.DisableCommandAsync(command, CancellationToken.None);

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
        await using var session = await scenario.OpenAsync();
        var preview = await session.PreviewAsync(null);
        await using (var writer = scenario.CreateIndependentContext())
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

        var result = await session.DisableCommandAsync(Command(preview), CancellationToken.None);

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
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        Guid? target = tenantTarget ? scenario.TenantId : null;
        var command = Command(await session.PreviewAsync(target));
        await Assert.That((await session.DisableCommandAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        string disabled = await scenario.StateAsync();
        await Assert.That((await session.DisableCommandAsync(command, CancellationToken.None)).FailureCode)
            .IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(disabled);
        await using (var writer = scenario.CreateIndependentContext())
            await SetEmailSettingAsync(writer, GovernanceSettingKeys.Email.DeliveryEnabled, "true", tenantId: target);
        string reenabled = await scenario.StateAsync();

        var replay = await session.DisableCommandAsync(command, CancellationToken.None);

        await Assert.That(replay.FailureCode).IsEqualTo(FailureCodes.ConcurrencyConflict);
        await Assert.That(await scenario.StateAsync()).IsEqualTo(reenabled);
        await Assert.That(session.Notifications.Published.Count).IsEqualTo(1);
        await Assert.That(session.Audit.Entries.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailedCommitLeavesPolicyCacheAndAuditUntouched(bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        Guid? target = tenantTarget ? scenario.TenantId : null;
        var command = Command(await session.PreviewAsync(target));
        await session.PrimeStaleCacheAsync();
        string before = await scenario.StateAsync();
        var context = session.Context;
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
        try { await session.DisableCommandAsync(command, CancellationToken.None); }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException) { failure = exception; }

        await Assert.That(failure).IsNotNull();
        await Assert.That(await scenario.StateAsync()).IsEqualTo(before);
        await Assert.That(await session.CachedSentinelAsync()).IsTrue();
        await session.AssertNoEffectsAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostCommitAuditFailurePreservesDisabledPolicyAndInvalidatedCacheAfterDisconnect(bool tenantTarget)
    {
        await using var scenario = await Scenario.CreateAsync();
        await using var session = await scenario.OpenAsync(tenantActor: tenantTarget);
        Guid? target = tenantTarget ? scenario.TenantId : null;
        var command = Command(await session.PreviewAsync(target));
        await session.PrimeStaleCacheAsync();
        using var cancellation = new CancellationTokenSource();
        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Notifications.OnPublishing = async (_, token) =>
        {
            await using var observer = scenario.CreateIndependentContext();
            await Assert.That((await ReadSettingAsync(observer, target)).Value).IsEqualTo("false");
            await Assert.That(token.CanBeCanceled).IsFalse();
            cancellation.Cancel();
            committed.SetResult();
        };
        var failure = new InvalidOperationException("Post-commit audit failed.");
        session.Audit.Failure = failure;

        Exception? observed = null;
        try { await session.DisableCommandAsync(command, cancellation.Token); }
        catch (Exception exception) { observed = exception; }

        await Assert.That(observed).IsSameReferenceAs(failure);
        await committed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(await session.CachedSentinelAsync()).IsFalse();
        await using var verification = scenario.CreateIndependentContext();
        await Assert.That((await ReadSettingAsync(verification, target)).Value).IsEqualTo("false");
        await Assert.That((await ReadSettingAsync(verification, tenantTarget ? null : scenario.TenantId)).Value).IsEqualTo("true");
        await Assert.That(session.Notifications.Published.Count).IsEqualTo(1);
        await Assert.That(session.Audit.Entries.Count).IsEqualTo(1);
    }

}
