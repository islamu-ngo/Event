namespace Event.Application.UnitTests.Settings;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain.Settings.Definitions;

public sealed class PublicationPolicyMutationBoundaryTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000010");
    private static readonly DateTime OccurredAtUtc = new(2026, 8, 25, 12, 34, 56, DateTimeKind.Utc);

    [Test]
    public async Task TenantAndInstanceMutations_UseOneFiveKeyLockForCompleteWorkflow()
    {
        var tenantLock = new RecordingMutationLock();
        var tenantStore = new RecordingStore(tenantLock)
        {
            Snapshot = Snapshot(systemValues: [System(EventSettingDefinitions.RequireApproval.Key, true)])
        };
        var tenantBoundary = Boundary(tenantLock, tenantStore);

        PublicationPolicyMutationResult tenantResult = await tenantBoundary.ApplyTenantAsync(
            TenantRequest([SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)]));

        var instanceLock = new RecordingMutationLock();
        var instanceStore = new RecordingStore(instanceLock)
        {
            Snapshot = Snapshot(systemValues: [System(EventSettingDefinitions.RequireApproval.Key, true)])
        };
        var instanceBoundary = Boundary(instanceLock, instanceStore);

        PublicationPolicyMutationResult instanceResult = await instanceBoundary.ApplyInstanceAsync(
            InstanceRequest([SetSystem(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)]));

        await Assert.That(tenantResult.Success).IsTrue();
        await Assert.That(instanceResult.Success).IsTrue();
        await AssertSingleCompleteWorkflowAsync(tenantLock, "read-tenant", "write-tenant");
        await AssertSingleCompleteWorkflowAsync(instanceLock, "read-instance", "write-instance");
    }

    [Test]
    public async Task TenantNotifications_MapScrambledNeutralChangesInCanonicalOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            WriteResult = WriteResult(
                Change(EventSettingDefinitions.GroupSubmissionEnabled.Key, "true", "false"),
                Change(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, null, "true"),
                Change(EventSettingDefinitions.RequireApproval.Key, "false", "true"))
        };
        IPublicationPolicyMutationBoundary boundary = Boundary(mutationLock, store);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
        [
            SetTenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true),
            SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true)
        ]), cancellation.Token);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(store.TenantWriteCount).IsEqualTo(1);
        await Assert.That(mutationLock.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(store.CancellationTokens.All(token => token == cancellation.Token)).IsTrue();
        await Assert.That(store.LastActorUserId).IsEqualTo(Actor);
        await Assert.That(store.LastOccurredAtUtc).IsEqualTo(OccurredAtUtc);
        await AssertNotificationAsync(result, 0,
            EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, null, "true",
            SettingSource.TenantOverride, TenantA);
        await AssertNotificationAsync(result, 1,
            EventSettingDefinitions.RequireApproval.Key, "false", "true",
            SettingSource.TenantOverride, TenantA);
        await AssertNotificationAsync(result, 2,
            EventSettingDefinitions.GroupSubmissionEnabled.Key, "true", "false",
            SettingSource.TenantOverride, TenantA);
    }

    [Test]
    public async Task UnsafeTenantState_UsesEvaluatorMessageAndStableCodeWithoutWriteOrEffects()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock) { Snapshot = Snapshot() };
        var boundary = Boundary(mutationLock, store);
        var unsafeState = new ReportingIntakePolicyState(false, false, true, true, true);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
            [SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)]));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(result.Message).IsEqualTo(ReportingIntakePolicyEvaluator.Evaluate(unsafeState).Message);
        await Assert.That(store.TotalWriteCount).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications).IsEmpty();
    }

    [Test]
    public async Task RepeatedInvalidInputs_ReturnSameNonemptyMessageAndStableCodeWithoutWrite()
    {
        PublicationPolicySettingMutation[][] invalidBatches =
        [
            [
                SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true),
                RemoveTenant(TenantA, EventSettingDefinitions.RequireApproval.Key)
            ],
            [SetTenant(TenantB, EventSettingDefinitions.RequireApproval.Key, true)],
            [SetSystem(EventSettingDefinitions.RequireApproval.Key, true)]
        ];
        var messages = new List<string>();

        foreach (PublicationPolicySettingMutation[] batch in invalidBatches)
        {
            var mutationLock = new RecordingMutationLock();
            var store = new RecordingStore(mutationLock) { Snapshot = Snapshot() };
            var boundary = Boundary(mutationLock, store);

            PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest([.. batch]));

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("event_reporting_intake_policy_invalid");
            await Assert.That(result.Message).IsNotNull().And.IsNotEmpty();
            await Assert.That(store.TotalWriteCount).IsEqualTo(0);
            await Assert.That(result.DeferredNotifications).IsEmpty();
            messages.Add(result.Message);
        }

        await Assert.That(messages.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ExternalReportingKey_IsNotOwnedAndFailsAsInvalidInput()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock) { Snapshot = Snapshot() };
        var boundary = Boundary(mutationLock, store);
        var externalMutation = new PublicationPolicySettingMutation(
            "moderation.reporting.provider",
            PublicationPolicyMutationKind.Set,
            "true",
            TenantA,
            IsLocked: null);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(
            TenantRequest([externalMutation]));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_reporting_intake_policy_invalid");
        await Assert.That(store.TotalWriteCount).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications).IsEmpty();
    }

    [Test]
    public async Task LockedReject_ForDifferentGuardedKeysUsesSameNonemptyMessageAndNoWrite()
    {
        string[] lockedKeys =
        [
            EventSettingDefinitions.RequireApproval.Key,
            EventSettingDefinitions.UserSubmissionEnabled.Key
        ];
        var messages = new List<string>();

        foreach (string lockedKey in lockedKeys)
        {
            var mutationLock = new RecordingMutationLock();
            var store = new RecordingStore(mutationLock)
            {
                Snapshot = Snapshot(systemValues: [System(lockedKey, true, isLocked: true)])
            };
            var boundary = Boundary(mutationLock, store);

            PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
                [SetTenant(TenantA, lockedKey, false)],
                PublicationPolicyLockedSystemBehavior.Reject));

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("event_reporting_policy_locked");
            await Assert.That(result.Message).IsNotNull().And.IsNotEmpty();
            await Assert.That(store.TotalWriteCount).IsEqualTo(0);
            await Assert.That(result.DeferredNotifications).IsEmpty();
            messages.Add(result.Message);
        }

        await Assert.That(messages.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task LockedRemoveOverride_RecompilesAndWritesOnlySafeEffectiveBatch()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(
                systemValues: [System(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false, isLocked: true)],
                tenantValues: [Tenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true)]),
            WriteResult = WriteResult(
                Change(EventSettingDefinitions.GroupSubmissionEnabled.Key, "true", "false"),
                Change(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, "true", null),
                Change(EventSettingDefinitions.OrganizationSubmissionEnabled.Key, "true", "false"),
                Change(EventSettingDefinitions.UserSubmissionEnabled.Key, "true", "false"))
        };
        var boundary = Boundary(mutationLock, store);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
        [
            SetTenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true),
            SetTenant(TenantA, EventSettingDefinitions.OrganizationSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.UserSubmissionEnabled.Key, false)
        ], PublicationPolicyLockedSystemBehavior.RemoveOverride));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(store.LastTenantMutations.SequenceEqual(
        [
            RemoveTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key),
            SetTenant(TenantA, EventSettingDefinitions.UserSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.OrganizationSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key, false)
        ])).IsTrue();
        await Assert.That(result.DeferredNotifications.Select(notification => notification.Key).SequenceEqual(
        [
            EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
            EventSettingDefinitions.UserSubmissionEnabled.Key,
            EventSettingDefinitions.OrganizationSubmissionEnabled.Key,
            EventSettingDefinitions.GroupSubmissionEnabled.Key
        ])).IsTrue();
    }

    [Test]
    public async Task UnsafeInstanceTenant_UsesEvaluatorMessageAndRejectsEntireWrite()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(
                systemValues: [System(EventSettingDefinitions.RequireApproval.Key, true)],
                tenantValues:
                [
                    Tenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true),
                    Tenant(TenantB, EventSettingDefinitions.RequireApproval.Key, false)
                ])
        };
        var boundary = Boundary(mutationLock, store);
        var unsafeTenantState = new ReportingIntakePolicyState(false, false, true, true, true);

        PublicationPolicyMutationResult result = await boundary.ApplyInstanceAsync(InstanceRequest(
            [SetSystem(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)]));

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(result.Message)
            .IsEqualTo(ReportingIntakePolicyEvaluator.Evaluate(unsafeTenantState).Message);
        await Assert.That(store.TotalWriteCount).IsEqualTo(0);
        await Assert.That(result.DeferredNotifications).IsEmpty();
    }

    [Test]
    public async Task InstanceNotifications_MapScrambledNeutralChangesInCanonicalOrder()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            WriteResult = WriteResult(
                Change(EventSettingDefinitions.GroupSubmissionEnabled.Key, "true", "false"),
                Change(EventSettingDefinitions.RequireApproval.Key, null, "true"),
                Change(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, "false", "true"))
        };
        var boundary = Boundary(mutationLock, store);

        PublicationPolicyMutationResult result = await boundary.ApplyInstanceAsync(InstanceRequest(
        [
            SetSystem(EventSettingDefinitions.GroupSubmissionEnabled.Key, false),
            SetSystem(EventSettingDefinitions.RequireApproval.Key, true),
            SetSystem(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true, isLocked: true)
        ]));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(store.InstanceWriteCount).IsEqualTo(1);
        await AssertNotificationAsync(result, 0,
            EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, "false", "true",
            SettingSource.SystemLocked, tenantId: null);
        await AssertNotificationAsync(result, 1,
            EventSettingDefinitions.RequireApproval.Key, null, "true",
            SettingSource.SystemDefault, tenantId: null);
        await AssertNotificationAsync(result, 2,
            EventSettingDefinitions.GroupSubmissionEnabled.Key, "true", "false",
            SettingSource.SystemDefault, tenantId: null);
    }

    [Test]
    public async Task InstanceCurrentTransactionMutation_ReusesCallerLockWithoutNesting()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            WriteResult = WriteResult(
                Change(EventSettingDefinitions.RequireApproval.Key, "false", "true"))
        };
        IPublicationPolicyMutationBoundary boundary =
            Boundary(mutationLock, store);

        PublicationPolicyMutationResult result =
            await mutationLock.ExecuteManyAsync(
                PublicationPolicySettingKeys.All,
                token => boundary.ApplyInstanceInCurrentTransactionAsync(
                    InstanceRequest(
                    [
                        SetSystem(
                            EventSettingDefinitions.RequireApproval.Key,
                            true)
                    ]),
                    token));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(mutationLock.InvocationCount).IsEqualTo(1);
        await Assert.That(mutationLock.Trace.SequenceEqual(
        [
            "execute-many",
            "delegate-enter",
            "read-instance",
            "write-instance",
            "commit",
            "delegate-exit"
        ])).IsTrue();
        await Assert.That(store.InstanceWriteCount).IsEqualTo(1);
    }

    [Test]
    public async Task TenantCurrentTransactionMutation_ReusesCallerLockWithoutNesting()
    {
        var mutationLock = new RecordingMutationLock();
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            WriteResult = WriteResult(
                Change(
                    EventSettingDefinitions.RequireApproval.Key,
                    "false",
                    "true"))
        };
        IPublicationPolicyMutationBoundary boundary =
            Boundary(mutationLock, store);

        PublicationPolicyMutationResult result =
            await mutationLock.ExecuteManyAsync(
                PublicationPolicySettingKeys.All,
                token => boundary.ApplyTenantInCurrentTransactionAsync(
                    TenantRequest(
                    [
                        SetTenant(
                            TenantA,
                            EventSettingDefinitions.RequireApproval.Key,
                            true)
                    ]),
                    token));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(mutationLock.InvocationCount).IsEqualTo(1);
        await Assert.That(mutationLock.Trace.SequenceEqual(
        [
            "execute-many",
            "delegate-enter",
            "read-tenant",
            "write-tenant",
            "commit",
            "delegate-exit"
        ])).IsTrue();
        await Assert.That(store.TenantWriteCount).IsEqualTo(1);
    }

    [Test]
    public async Task RetryRebasing_FinalUnsafeAttemptDropsRolledBackAttemptEffects()
    {
        var mutationLock = new RecordingMutationLock(attemptCount: 2);
        var store = new RecordingStore(mutationLock);
        store.Snapshots.Enqueue(Snapshot(
            systemValues: [System(EventSettingDefinitions.RequireApproval.Key, true)]));
        store.Snapshots.Enqueue(Snapshot());
        store.WriteResults.Enqueue(WriteResult(
            Change(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, "true", "false")));
        var boundary = Boundary(mutationLock, store);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
            [SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)]));

        await Assert.That(mutationLock.InvocationCount).IsEqualTo(1);
        await Assert.That(store.TenantWriteCount).IsEqualTo(1);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(result.DeferredNotifications).IsEmpty();
        await Assert.That(store.CommittedValues).IsEmpty();
    }

    [Test]
    public async Task RetryRebasing_FinalCommittedAttemptAloneSuppliesNotifications()
    {
        var mutationLock = new RecordingMutationLock(attemptCount: 2);
        var store = new RecordingStore(mutationLock) { Snapshot = Snapshot() };
        store.WriteResults.Enqueue(WriteResult(
            Change(EventSettingDefinitions.RequireApproval.Key, null, "rolled-back")));
        store.WriteResults.Enqueue(WriteResult(
            Change(EventSettingDefinitions.RequireApproval.Key, "false", "true")));
        var boundary = Boundary(mutationLock, store);

        PublicationPolicyMutationResult result = await boundary.ApplyTenantAsync(TenantRequest(
            [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(mutationLock.InvocationCount).IsEqualTo(1);
        await Assert.That(store.TenantWriteCount).IsEqualTo(2);
        await Assert.That(result.DeferredNotifications).HasSingleItem();
        await AssertNotificationAsync(result, 0,
            EventSettingDefinitions.RequireApproval.Key, "false", "true",
            SettingSource.TenantOverride, TenantA);
        await Assert.That(result.DeferredNotifications[0].NewValue).IsNotEqualTo("rolled-back");
        await Assert.That(store.CommittedValues[EventSettingDefinitions.RequireApproval.Key]).IsEqualTo("true");
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("instance")]
    public async Task WriteAfterWorkingStateMutation_RollsBackAndPropagatesWithoutDeferredEffects(string scope)
    {
        var expected = new InvalidOperationException($"{scope}-post-mutation-failure");
        var mutationLock = new RecordingMutationLock();
        var before = ImmutableDictionary<string, string?>.Empty.Add("sentinel", "committed");
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            CommittedValues = before,
            TenantWriteAfterMutationFailure = scope == "tenant" ? expected : null,
            InstanceWriteAfterMutationFailure = scope == "instance" ? expected : null
        };
        var boundary = Boundary(mutationLock, store);
        PublicationPolicyMutationResult? escapedResult = null;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            escapedResult = scope == "tenant"
                ? await boundary.ApplyTenantAsync(TenantRequest(
                    [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]))
                : await boundary.ApplyInstanceAsync(InstanceRequest(
                    [SetSystem(EventSettingDefinitions.RequireApproval.Key, true)]));
        });

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(escapedResult).IsNull();
        await Assert.That(store.CommittedValues).IsEquivalentTo(before);
        await Assert.That(store.WorkingValues).IsEquivalentTo(before);
        await Assert.That(mutationLock.RollbackCount).IsEqualTo(1);
        await Assert.That(mutationLock.CommitCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("instance")]
    public async Task DirectWriteFailure_PropagatesWithoutCommitOrDeferredEffects(string scope)
    {
        var expected = new InvalidOperationException($"{scope}-write-failure");
        var mutationLock = new RecordingMutationLock();
        var before = ImmutableDictionary<string, string?>.Empty.Add("sentinel", "committed");
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            CommittedValues = before,
            TenantWriteFailure = scope == "tenant" ? expected : null,
            InstanceWriteFailure = scope == "instance" ? expected : null
        };
        var boundary = Boundary(mutationLock, store);
        PublicationPolicyMutationResult? escapedResult = null;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            escapedResult = scope == "tenant"
                ? await boundary.ApplyTenantAsync(TenantRequest(
                    [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]))
                : await boundary.ApplyInstanceAsync(InstanceRequest(
                    [SetSystem(EventSettingDefinitions.RequireApproval.Key, true)]));
        });

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(escapedResult).IsNull();
        await Assert.That(store.CommittedValues).IsEquivalentTo(before);
        await Assert.That(mutationLock.RollbackCount).IsEqualTo(1);
        await Assert.That(mutationLock.CommitCount).IsEqualTo(0);
    }

    [Test]
    [Arguments("lock")]
    [Arguments("read")]
    public async Task LockOrReadFailure_PropagatesWithoutDeferredEffects(string failureSource)
    {
        var expected = new InvalidOperationException($"{failureSource}-failure");
        var mutationLock = new RecordingMutationLock
        {
            Failure = failureSource == "lock" ? expected : null
        };
        var store = new RecordingStore(mutationLock)
        {
            Snapshot = Snapshot(),
            ReadFailure = failureSource == "read" ? expected : null
        };
        var boundary = Boundary(mutationLock, store);
        PublicationPolicyMutationResult? escapedResult = null;

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            escapedResult = await boundary.ApplyTenantAsync(TenantRequest(
                [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]));
        });

        await Assert.That(actual).IsSameReferenceAs(expected);
        await Assert.That(escapedResult).IsNull();
    }

    private static IPublicationPolicyMutationBoundary Boundary(
        ISettingMutationLock mutationLock,
        ICoordinatedSettingMutationStore store) =>
        new PublicationPolicyMutationBoundary(mutationLock, store);

    private static PublicationPolicyTenantMutationRequest TenantRequest(
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        PublicationPolicyLockedSystemBehavior lockedBehavior = PublicationPolicyLockedSystemBehavior.Reject) =>
        new(TenantA, Actor, OccurredAtUtc, mutations, lockedBehavior);

    private static PublicationPolicyInstanceMutationRequest InstanceRequest(
        ImmutableArray<PublicationPolicySettingMutation> mutations) =>
        new(Actor, OccurredAtUtc, mutations);

    private static PublicationPolicyMutationSnapshot Snapshot(
        ImmutableArray<PublicationPolicySystemValueSnapshot> systemValues = default,
        ImmutableArray<PublicationPolicyTenantValueSnapshot> tenantValues = default) =>
        new(systemValues.IsDefault ? [] : systemValues, tenantValues.IsDefault ? [] : tenantValues);

    private static PublicationPolicySystemValueSnapshot System(string key, bool value, bool isLocked = false) =>
        new(key, value ? "true" : "false", isLocked);

    private static PublicationPolicyTenantValueSnapshot Tenant(Guid tenantId, string key, bool value) =>
        new(tenantId, key, value ? "true" : "false");

    private static PublicationPolicySettingMutation SetTenant(Guid tenantId, string key, bool value) =>
        new(key, PublicationPolicyMutationKind.Set, value ? "true" : "false", tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation RemoveTenant(Guid tenantId, string key) =>
        new(key, PublicationPolicyMutationKind.Remove, JsonValue: null, tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation SetSystem(string key, bool value, bool isLocked = false) =>
        new(key, PublicationPolicyMutationKind.Set, value ? "true" : "false", TenantId: null, isLocked);

    private static CoordinatedSettingValueChange Change(string key, string? oldValue, string? newValue) =>
        new(key, oldValue, newValue);

    private static CoordinatedSettingMutationWriteResult WriteResult(
        params CoordinatedSettingValueChange[] changes) => new([.. changes]);

    private static async Task AssertSingleCompleteWorkflowAsync(
        RecordingMutationLock mutationLock,
        string readCall,
        string writeCall)
    {
        await Assert.That(mutationLock.InvocationCount).IsEqualTo(1);
        await Assert.That(mutationLock.Keys.SequenceEqual(PublicationPolicySettingKeys.All)).IsTrue();
        await Assert.That(mutationLock.Trace.SequenceEqual(
            ["execute-many", "delegate-enter", readCall, writeCall, "commit", "delegate-exit"])).IsTrue();
        await Assert.That(mutationLock.CommitCount).IsEqualTo(1);
        await Assert.That(mutationLock.RollbackCount).IsEqualTo(0);
        await Assert.That(mutationLock.IsInsideDelegate).IsFalse();
    }

    private static async Task AssertNotificationAsync(
        PublicationPolicyMutationResult result,
        int index,
        string key,
        string? oldValue,
        string? newValue,
        SettingSource source,
        Guid? tenantId)
    {
        await Assert.That(result.DeferredNotifications.Count).IsGreaterThan(index);
        var notification = result.DeferredNotifications[index];
        await Assert.That(notification.Key).IsEqualTo(key);
        await Assert.That(notification.OldValue).IsEqualTo(oldValue);
        await Assert.That(notification.NewValue).IsEqualTo(newValue);
        await Assert.That(notification.Scope).IsEqualTo(source);
        await Assert.That(notification.TenantId).IsEqualTo(tenantId);
        await Assert.That(notification.ActorUserId).IsEqualTo(Actor);
        await Assert.That(notification.ChangedAt).IsEqualTo(OccurredAtUtc);
    }

    private sealed class RecordingMutationLock(int attemptCount = 1) : ISettingMutationLock
    {
        private RecordingStore? _store;

        public IReadOnlyList<string> Keys { get; private set; } = [];
        public CancellationToken CancellationToken { get; private set; }
        public List<string> Trace { get; } = [];
        public int InvocationCount { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public bool IsInsideDelegate { get; private set; }
        public Exception? Failure { get; init; }

        public void Attach(RecordingStore store) => _store = store;

        public Task<T> ExecuteAsync<T>(
            string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The coordinated boundary must use ExecuteManyAsync.");

        public async Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            Trace.Add("execute-many");
            if (Failure is not null)
                throw Failure;

            Keys = canonicalSettingKeys.ToArray();
            CancellationToken = cancellationToken;
            T result = default!;
            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                _store!.BeginAttempt();
                IsInsideDelegate = true;
                Trace.Add("delegate-enter");
                try
                {
                    result = await operation(cancellationToken);
                    if (attempt == attemptCount - 1)
                    {
                        _store.CommitAttempt();
                        CommitCount++;
                        Trace.Add("commit");
                    }
                    else
                    {
                        _store.RollbackAttempt();
                        RollbackCount++;
                        Trace.Add("retry-rollback");
                    }
                }
                catch
                {
                    _store.RollbackAttempt();
                    RollbackCount++;
                    Trace.Add("rollback");
                    throw;
                }
                finally
                {
                    Trace.Add("delegate-exit");
                    IsInsideDelegate = false;
                }
            }

            return result;
        }
    }

    // Contract expectation: both batch write methods participate in the transaction opened or joined by the lock.
    private sealed class RecordingStore : ICoordinatedSettingMutationStore
    {
        private readonly RecordingMutationLock _mutationLock;
        private ImmutableDictionary<string, string?> _workingValues = ImmutableDictionary<string, string?>.Empty;

        public RecordingStore(RecordingMutationLock mutationLock)
        {
            _mutationLock = mutationLock;
            mutationLock.Attach(this);
        }

        public Queue<PublicationPolicyMutationSnapshot> Snapshots { get; } = new();
        public Queue<CoordinatedSettingMutationWriteResult> WriteResults { get; } = new();
        public List<CancellationToken> CancellationTokens { get; } = [];
        public PublicationPolicyMutationSnapshot Snapshot { get; init; } = new([], []);
        public CoordinatedSettingMutationWriteResult WriteResult { get; init; } = new([]);
        public ImmutableDictionary<string, string?> CommittedValues { get; set; } =
            ImmutableDictionary<string, string?>.Empty;
        public ImmutableDictionary<string, string?> WorkingValues => _workingValues;
        public Exception? ReadFailure { get; init; }
        public Exception? TenantWriteFailure { get; init; }
        public Exception? InstanceWriteFailure { get; init; }
        public Exception? TenantWriteAfterMutationFailure { get; init; }
        public Exception? InstanceWriteAfterMutationFailure { get; init; }
        public int TenantWriteCount { get; private set; }
        public int InstanceWriteCount { get; private set; }
        public int TotalWriteCount => TenantWriteCount + InstanceWriteCount;
        public ImmutableArray<PublicationPolicySettingMutation> LastTenantMutations { get; private set; } = [];
        public Guid? LastActorUserId { get; private set; }
        public DateTime LastOccurredAtUtc { get; private set; }

        public void BeginAttempt() => _workingValues = CommittedValues;

        public void CommitAttempt() => CommittedValues = _workingValues;

        public void RollbackAttempt() => _workingValues = CommittedValues;

        public Task<PublicationPolicyMutationSnapshot> ReadTenantSnapshotAsync(
            Guid tenantId,
            CancellationToken cancellationToken)
        {
            RequireLock();
            Record("read-tenant", cancellationToken);
            Throw(ReadFailure);
            return Task.FromResult(NextSnapshot());
        }

        public Task<PublicationPolicyMutationSnapshot> ReadInstanceSnapshotAsync(
            CancellationToken cancellationToken)
        {
            RequireLock();
            Record("read-instance", cancellationToken);
            Throw(ReadFailure);
            return Task.FromResult(NextSnapshot());
        }

        public Task<CoordinatedSettingMutationWriteResult> WriteTenantAsync(
            Guid tenantId,
            ImmutableArray<PublicationPolicySettingMutation> mutations,
            Guid? actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            RequireLock();
            Record("write-tenant", cancellationToken);
            TenantWriteCount++;
            Throw(TenantWriteFailure);
            ApplyToWorkingState(mutations);
            Throw(TenantWriteAfterMutationFailure);
            LastTenantMutations = mutations;
            RecordWriteMetadata(actorUserId, occurredAtUtc);
            return Task.FromResult(NextWriteResult());
        }

        public Task<CoordinatedSettingMutationWriteResult> WriteInstanceAsync(
            ImmutableArray<PublicationPolicySettingMutation> mutations,
            Guid actorUserId,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            RequireLock();
            Record("write-instance", cancellationToken);
            InstanceWriteCount++;
            Throw(InstanceWriteFailure);
            ApplyToWorkingState(mutations);
            Throw(InstanceWriteAfterMutationFailure);
            RecordWriteMetadata(actorUserId, occurredAtUtc);
            return Task.FromResult(NextWriteResult());
        }

        private PublicationPolicyMutationSnapshot NextSnapshot() =>
            Snapshots.Count > 0 ? Snapshots.Dequeue() : Snapshot;

        private CoordinatedSettingMutationWriteResult NextWriteResult() =>
            WriteResults.Count > 0 ? WriteResults.Dequeue() : WriteResult;

        private void ApplyToWorkingState(ImmutableArray<PublicationPolicySettingMutation> mutations)
        {
            foreach (PublicationPolicySettingMutation mutation in mutations)
            {
                _workingValues = mutation.Kind == PublicationPolicyMutationKind.Set
                    ? _workingValues.SetItem(mutation.Key, mutation.JsonValue)
                    : _workingValues.Remove(mutation.Key);
            }
        }

        private void Record(string call, CancellationToken cancellationToken)
        {
            _mutationLock.Trace.Add(call);
            CancellationTokens.Add(cancellationToken);
        }

        private void RecordWriteMetadata(Guid? actorUserId, DateTime occurredAtUtc)
        {
            LastActorUserId = actorUserId;
            LastOccurredAtUtc = occurredAtUtc;
        }

        private void RequireLock()
        {
            if (!_mutationLock.IsInsideDelegate)
                throw new InvalidOperationException("Store access escaped the active setting-lock delegate.");
        }

        private static void Throw(Exception? failure)
        {
            if (failure is not null)
                throw failure;
        }
    }
}
