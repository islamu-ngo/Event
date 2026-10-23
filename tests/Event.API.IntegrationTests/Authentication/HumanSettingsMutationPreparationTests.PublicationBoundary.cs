using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    public async Task TenantAndInstanceMutations_UseOneFiveKeyLockForCompleteWorkflow()
    {
        foreach (bool instance in new[] { false, true })
        {
            await using var fixture = await PublicationFixture.CreateAsync();
            await fixture.SeedSystemAsync(ApprovalKey, "true");
            var result = await fixture.ApplyAsync(instance, PublicationValues((IntakeKey, "false")));
            await Assert.That(result.Success).IsTrue();
            await AssertPublicationWorkflowAsync(fixture.Observation);
            await Assert.That(await fixture.ReadAsync(instance, IntakeKey)).IsEqualTo("false");
        }
    }

    [Test]
    public async Task TenantNotifications_MapScrambledNeutralChangesInPolicyOrder()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedTenantAsync(GroupSubmissionKey, "true");
        await fixture.SeedTenantAsync(ApprovalKey, "false");
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.ApplyAsync(false, PublicationValues(
            (GroupSubmissionKey, "false"), (ApprovalKey, "true"), (IntakeKey, "true")),
            cancellationToken: cancellation.Token);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(fixture.Observation.Saves).IsEqualTo(1);
        await Assert.That(fixture.Observation.Tokens).IsNotEmpty();
        await Assert.That(fixture.Observation.Tokens.All(token => token == cancellation.Token)).IsTrue();
        await AssertPublicationNotificationAsync(fixture, result, 0, IntakeKey, null, "true", SettingSource.TenantOverride);
        await AssertPublicationNotificationAsync(fixture, result, 1, ApprovalKey, "false", "true", SettingSource.TenantOverride);
        await AssertPublicationNotificationAsync(fixture, result, 2, GroupSubmissionKey, "true", "false", SettingSource.TenantOverride);
    }

    [Test]
    public async Task UnsafeTenantState_UsesEvaluatorMessageAndStableCodeWithoutWriteOrEffects()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        var result = await fixture.ApplyAsync(false, PublicationValues((IntakeKey, "false")));
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(result.Message).IsEqualTo(ReportingIntakePolicyEvaluator
            .Evaluate(new(false, false, true, true, true)).Message);
        await Assert.That(fixture.Observation.Saves).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await Assert.That(await fixture.ReadAsync(false, IntakeKey)).IsNull();
    }

    [Test]
    public async Task LockedReject_ForDifferentGuardedKeysUsesSameNonemptyMessageAndNoWrite()
    {
        var messages = new List<string>();
        foreach (string key in new[] { ApprovalKey, UserSubmissionKey })
        {
            await using var fixture = await PublicationFixture.CreateAsync();
            await fixture.SeedSystemAsync(key, "true", locked: true);
            var result = await fixture.ApplyAsync(false, PublicationValues((key, "false")));
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("event_reporting_policy_locked");
            await Assert.That(result.Message).IsNotNull().And.IsNotEmpty();
            await Assert.That(result.DeferredNotifications).IsEmpty();
            await Assert.That(fixture.Observation.Saves).IsEqualTo(0);
            await Assert.That(await fixture.ReadAsync(false, key)).IsNull();
            messages.Add(result.Message);
        }
        await Assert.That(messages.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LockedRemoveOverride_RecompilesAndWritesOnlySafeEffectiveBatch()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedSystemAsync(IntakeKey, "false", locked: true);
        foreach (string key in new[] { IntakeKey, UserSubmissionKey, OrganizationSubmissionKey, GroupSubmissionKey })
            await fixture.SeedTenantAsync(key, "true");
        var result = await fixture.ApplyAsync(false, PublicationValues(
            (GroupSubmissionKey, "false"), (IntakeKey, "true"),
            (OrganizationSubmissionKey, "false"), (UserSubmissionKey, "false")),
            PublicationPolicyLockedSystemBehavior.RemoveOverride);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(fixture.Observation.Saves).IsEqualTo(1);
        await Assert.That(fixture.Observation.Effects.Count).IsEqualTo(4);
        await Assert.That(fixture.Observation.Effects.Single(effect => effect.Key == IntakeKey).State)
            .IsEqualTo(EntityState.Deleted);
        await Assert.That(await fixture.ReadAsync(false, IntakeKey)).IsNull();
        foreach (string key in new[] { UserSubmissionKey, OrganizationSubmissionKey, GroupSubmissionKey })
        {
            await Assert.That(fixture.Observation.Effects.Single(effect => effect.Key == key).Value).IsEqualTo("false");
            await Assert.That(await fixture.ReadAsync(false, key)).IsEqualTo("false");
        }
        await Assert.That(result.DeferredNotifications.Select(item => item.Key))
            .IsEquivalentTo([IntakeKey, UserSubmissionKey, OrganizationSubmissionKey, GroupSubmissionKey]);
        await AssertPublicationNotificationAsync(fixture, result, 0, IntakeKey, "true", null, SettingSource.TenantOverride);
        await AssertPublicationNotificationAsync(fixture, result, 1, UserSubmissionKey, "true", "false", SettingSource.TenantOverride);
        await AssertPublicationNotificationAsync(fixture, result, 2, OrganizationSubmissionKey, "true", "false", SettingSource.TenantOverride);
        await AssertPublicationNotificationAsync(fixture, result, 3, GroupSubmissionKey, "true", "false", SettingSource.TenantOverride);
    }

    [Test]
    public async Task UnsafeInstanceTenant_UsesEvaluatorMessageAndRejectsEntireWrite()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedSystemAsync(ApprovalKey, "true");
        Guid foreign = Guid.CreateVersion7();
        await using (var seed = fixture.Identity.Native.CreateDatabase())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = foreign,
                Slug = "unsafe-publication",
                FullName = "Unsafe publication tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync();
        }
        await fixture.SeedTenantAsync(IntakeKey, "true");
        await fixture.SeedTenantAsync(ApprovalKey, "false", foreign);
        var result = await fixture.ApplyAsync(true, PublicationValues((IntakeKey, "false")));
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(result.Message).IsEqualTo(ReportingIntakePolicyEvaluator
            .Evaluate(new(false, false, true, true, true)).Message);
        await Assert.That(fixture.Observation.Saves).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await Assert.That(await fixture.ReadAsync(true, IntakeKey)).IsEqualTo("true");
    }

    [Test]
    public async Task InstanceNotifications_MapScrambledNeutralChangesInPolicyOrder()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedSystemAsync(IntakeKey, "false", locked: true);
        await using (var seed = fixture.Identity.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == ApprovalKey).ExecuteDeleteAsync();
        var result = await fixture.ApplyAsync(true, PublicationValues(
            (GroupSubmissionKey, "false"), (ApprovalKey, "true"), (IntakeKey, "true")));
        await Assert.That(result.Success).IsTrue();
        await Assert.That(fixture.Observation.Saves).IsEqualTo(1);
        await AssertPublicationNotificationAsync(fixture, result, 0, IntakeKey, "false", "true", SettingSource.SystemLocked);
        await AssertPublicationNotificationAsync(fixture, result, 1, ApprovalKey, null, "true", SettingSource.SystemDefault);
        await AssertPublicationNotificationAsync(fixture, result, 2, GroupSubmissionKey, "true", "false", SettingSource.SystemDefault);
    }

    [Test]
    public async Task InstanceCurrentTransactionMutation_ReusesCallerLockWithoutNesting()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        var result = await fixture.ApplyAsync(true, PublicationValues((ApprovalKey, "true")), callerLock: true);
        await Assert.That(result.Success).IsTrue();
        await AssertPublicationWorkflowAsync(fixture.Observation);
    }

    [Test]
    public async Task TenantCurrentTransactionMutation_ReusesCallerLockWithoutNesting()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedTenantAsync(ApprovalKey, "false");
        var result = await fixture.ApplyAsync(false, PublicationValues((ApprovalKey, "true")), callerLock: true);
        await Assert.That(result.Success).IsTrue();
        await AssertPublicationWorkflowAsync(fixture.Observation);
    }

    [Test]
    public async Task SeparateInvocation_FinalUnsafeAttemptDropsRolledBackAttemptEffects()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await fixture.SeedSystemAsync(ApprovalKey, "true");
        var failure = new InvalidOperationException("rollback-first-publication-invocation");
        PublicationPolicyMutationResult? escaped = null;
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            escaped = await fixture.ApplyAsync(false, PublicationValues((IntakeKey, "false")),
                afterWrite: async (_, result, _) =>
                {
                    await Assert.That(result.Success).IsTrue();
                    await Assert.That(result.DeferredNotifications).HasSingleItem();
                    throw failure;
                }));
        await Assert.That(caught).IsSameReferenceAs(failure);
        await Assert.That(escaped).IsNull();
        await Assert.That(fixture.Observation.Starts).IsEqualTo(1);
        await Assert.That(fixture.Observation.Saves).IsEqualTo(1);
        await Assert.That(fixture.Observation.Commits).IsEqualTo(0);
        await Assert.That(await fixture.ReadAsync(false, IntakeKey)).IsNull();
        // This is a second explicit invocation with its own observation and proof,
        // not a replay of the first owner's callback.
        await fixture.SeedSystemAsync(ApprovalKey, "false");
        var final = await fixture.ApplyAsync(false, PublicationValues((IntakeKey, "false")));
        await Assert.That(final.Success).IsFalse();
        await Assert.That(final.FailureCode).IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(final.DeferredNotifications).IsEmpty();
        await Assert.That(fixture.Observation.Starts).IsEqualTo(1);
        await Assert.That(fixture.Observation.Saves).IsEqualTo(0);
        await Assert.That(await fixture.ReadAsync(false, IntakeKey)).IsNull();
    }

    [Test]
    public async Task SeparateInvocation_FinalCommittedAttemptAloneSuppliesNotifications()
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        PublicationPolicyMutationResult? escaped = null;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            escaped = await fixture.ApplyAsync(false, PublicationValues((ApprovalKey, "true")),
                afterWrite: async (_, result, _) =>
                {
                    await Assert.That(result.DeferredNotifications[0].OldValue).IsNull();
                    throw new InvalidOperationException("rollback-first-publication-invocation");
                }));
        await Assert.That(escaped).IsNull();
        await Assert.That(fixture.Observation.Starts).IsEqualTo(1);
        await Assert.That(fixture.Observation.Commits).IsEqualTo(0);
        await Assert.That(await fixture.ReadAsync(false, ApprovalKey)).IsNull();
        await fixture.SeedTenantAsync(ApprovalKey, "false");
        var final = await fixture.ApplyAsync(false, PublicationValues((ApprovalKey, "true")));
        await Assert.That(final.Success).IsTrue();
        await Assert.That(final.DeferredNotifications).HasSingleItem();
        await AssertPublicationNotificationAsync(fixture, final, 0, ApprovalKey, "false", "true", SettingSource.TenantOverride);
        await AssertPublicationWorkflowAsync(fixture.Observation);
        await Assert.That(await fixture.ReadAsync(false, ApprovalKey)).IsEqualTo("true");
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("instance")]
    public async Task WriteAfterWorkingStateMutation_RollsBackAndPropagatesWithoutDeferredEffects(string scope)
    {
        await AssertPublicationFailureAsync(scope == "instance", "saved");
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("instance")]
    public async Task DirectWriteFailure_PropagatesWithoutCommitOrDeferredEffects(string scope)
    {
        await AssertPublicationFailureAsync(scope == "instance", "saving");
    }

    [Test]
    [Arguments("lock")]
    [Arguments("read")]
    public async Task LockOrReadFailure_PropagatesWithoutDeferredEffects(string failureSource)
    {
        await AssertPublicationFailureAsync(false, failureSource);
    }

    [Test]
    [Arguments("duplicate")]
    [Arguments("cross-tenant")]
    [Arguments("instance")]
    [Arguments("unowned")]
    public async Task PublicationShapeAttackCannotBorrowAuthenticatedOriginalEffects(string shape)
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        await using var selected = await fixture.Request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(fixture.Tenant), CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = ApprovalKey, Value = "true", Scope = SettingScope.Tenant }, "true");
        var boundary = services.GetRequiredService<IPublicationPolicyMutationBoundary>();
        var original = new PublicationPolicySettingMutation(ApprovalKey, PublicationPolicyMutationKind.Set,
            "true", fixture.Tenant, null);
        ImmutableArray<PublicationPolicySettingMutation> mutations = shape switch
        {
            "duplicate" => [original, original with { Kind = PublicationPolicyMutationKind.Remove, JsonValue = null }],
            "cross-tenant" => [original with { TenantId = Guid.CreateVersion7() }],
            "instance" => [original with { TenantId = null, IsLocked = false }],
            _ => [original with { Key = "moderation.reporting.provider" }]
        };
        fixture.Observation.Begin(services.GetRequiredService<Explore.Persistence.ExploreDbContext>());
        try
        {
            await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => boundary.ApplyTenantAsync(
                    new(fixture.Tenant, fixture.Actor, PublicationTime, mutations,
                        PublicationPolicyLockedSystemBehavior.Reject), token));
                return true;
            });
            await Assert.That(fixture.Observation.Saves).IsEqualTo(0);
            await Assert.That(await fixture.ReadAsync(false, ApprovalKey)).IsNull();
        }
        finally { fixture.Observation.Owner = null; }
    }

    private static ImmutableDictionary<string, string> PublicationValues(params (string Key, string Value)[] values) =>
        values.ToImmutableDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static async Task AssertPublicationWorkflowAsync(PublicationObservation observation)
    {
        await Assert.That(observation.LockKeys.Count).IsEqualTo(1);
        await Assert.That(observation.LockKeys[0].SequenceEqual(PublicationPolicySettingKeys.All)).IsTrue();
        await Assert.That(observation.Starts).IsEqualTo(1);
        await Assert.That(observation.Saves).IsEqualTo(1);
        await Assert.That(observation.Commits).IsEqualTo(1);
        int entered = observation.Trace.IndexOf("lock-enter");
        int read = observation.Trace.IndexOf("read");
        int saving = observation.Trace.IndexOf("saving");
        int saved = observation.Trace.IndexOf("saved");
        int exited = observation.Trace.IndexOf("lock-exit");
        int committed = observation.Trace.IndexOf("commit");
        await Assert.That(entered >= 0 && entered < read && read < saving && saving < saved
            && saved < exited && exited < committed).IsTrue();
    }

    private static async Task AssertPublicationNotificationAsync(PublicationFixture fixture,
        PublicationPolicyMutationResult result, int index, string key, string? oldValue, string? newValue,
        SettingSource source)
    {
        await Assert.That(result.DeferredNotifications.Length).IsGreaterThan(index);
        var notification = result.DeferredNotifications[index];
        await Assert.That(notification.Key).IsEqualTo(key);
        await Assert.That(notification.OldValue).IsEqualTo(oldValue);
        await Assert.That(notification.NewValue).IsEqualTo(newValue);
        await Assert.That(notification.Scope).IsEqualTo(source);
        await Assert.That(notification.TenantId).IsEqualTo(source == SettingSource.TenantOverride ? fixture.Tenant : (Guid?)null);
        await Assert.That(notification.ActorUserId).IsEqualTo(fixture.Actor);
        await Assert.That(notification.ChangedAt).IsEqualTo(PublicationTime);
    }

    private static async Task AssertPublicationFailureAsync(bool instance, string phase)
    {
        await using var fixture = await PublicationFixture.CreateAsync();
        if (!instance)
            await fixture.SeedTenantAsync(ApprovalKey, "false");
        string? before = await fixture.ReadAsync(instance, ApprovalKey);
        var failure = new InvalidOperationException("publication-native-" + phase);
        fixture.Observation.Failure = failure;
        fixture.Observation.FailurePhase = phase;
        PublicationPolicyMutationResult? escaped = null;
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            escaped = await fixture.ApplyAsync(instance, PublicationValues((ApprovalKey, "true"))));
        await Assert.That(caught).IsSameReferenceAs(failure);
        await Assert.That(escaped).IsNull();
        await Assert.That(fixture.Observation.Commits).IsEqualTo(0);
        if (phase == "saved")
            await Assert.That(fixture.Observation.Trace.Contains("saved")).IsTrue();
        await Assert.That(await fixture.ReadAsync(instance, ApprovalKey)).IsEqualTo(before);
    }
}
